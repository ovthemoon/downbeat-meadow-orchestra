using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Downbeat.Audio;
using Downbeat.Content;
using Downbeat.Core;
using Downbeat.Meadow;
using Downbeat.Record;
using UnityEngine;
using UnityEngine.Profiling;
using VContainer.Unity;
using Debug = UnityEngine.Debug;

namespace Downbeat.App
{
    /// <summary>
    /// The game loop. Boot: content, the meadow, samples, then wait for the audio clock to move before placing the beat grid.
    /// Each frame: input intents become grid-quantized notes and the audio engine plays them at absolute audio times. Notes are
    /// queued for the picture and released when they are HEARD (audio time re-anchored every frame); the meadow's note visuals
    /// (flowers, wind, particles) are connected to that queue by the next tasks. Every hand note also feeds the performance energy; the energy decides which
    /// nature layers (fireflies, sky, hills) play and how the picture follows, and the flow of a visit (Idle, Waking, Playing, Settling) says when somebody is there.
    /// </summary>
    public sealed class DownbeatApp : IStartable, ITickable
    {
        const float FlashLeadSec = 0.007f; // release a picture event up to half a frame early so it is drawn with the sound
        const float BootTimeoutSec = 30f;          // how long the app waits for the sound before it gives up (--audio-timeout SEC changes it: tests)
        const float DbToLinearDivisor = 20f;
        const float MaxEnergyStepSec = 0.25f;      // a frame longer than this (a stall) does not make the energy fade by the whole stall at once
        const float DefaultHandAspect = 4f / 3f;   // the picture of a hand source that does not tell (a tracker always does)
        const double HandFrameStaleSec = 0.5;      // a tracker that sends no frame for this long is gone, whatever its last frame said
        const float LogIntervalSec = 1f;
        const double HealthRowIntervalSec = 60.0;   // one row of the health CSV a minute
        const double HeartbeatIntervalSec = 5.0;    // --heartbeat-interval SEC changes it (tests)
        const double KeepAwakeRefreshSec = 60.0;
        const string LogFolder = "logs";

        enum State
        {
            Loading,
            Running,
            Failed,
        }

        readonly IAudioEngine _audio;
        readonly IContentRepository _content;
        readonly IPlayInput _input;
        readonly PlayableRange _range;
        readonly HandSettingsHolder _handSettings;
        readonly IHandSource _handSource;
        readonly IHandMotionView _handMotion;
        readonly MeadowStage _meadow;
        readonly LaunchArgs _launch;
        readonly CoroutineRunner _runner;
        readonly ConductPlayer _conduct;
        readonly SettingsPanelView _settingsPanel;
        AutoQualityRunner _quality;
        readonly NoteVisualQueue _queue = new NoteVisualQueue();
        readonly List<ScheduledNote> _due = new List<ScheduledNote>(16);
        readonly List<NatureEvent> _natureEvents = new List<NatureEvent>(16);

        readonly List<float> _frameTimes = new List<float>(4096);
        State _state = State.Loading;
        PitchMapper _mapper;
        QuantizedNoteScheduler _scheduler;
        BeatGrid _grid;
        SongDto _song;
        float _stripFraction;
        SongModeStore _songModeStore;
        SongSession _songs;
        bool _songModeOn = true;
        readonly SongTitleCue _titleCue = new SongTitleCue();
        double _songTitleSec = SongTitleCue.DefaultDurationSec;
        NatureMapper _natureMapper;
        PerformanceEnergy _energy;
        ExperienceStateMachine _experience;
        HandPresenceMonitor _presence;
        NatureAudioBridge _natureBridge;
        NatureVisuals _natureVisuals;
        ExperienceLog _experienceLog;
        RunRecorder _recorder;
        DisplayRouter _display;
        RecordCoordinator _record;
        TutorialCoordinator _tutorial;
        PoseLogger _poseLog;
        ScreensCapture _screensCapture;
        HealthLogger _health;
        PlayerGuard _guard;
        KeepAwake _keepAwake;
        double _nextKeepAwakeRefresh;
        long _lastHandFrameSeq = -1;
        bool _lastHandFrameArrived;
        bool _audioFailed;
        int _sessions;
        static int _errorCount;
        float _holdEnergy = float.NaN;
        bool _inputNoteThisFrame;
        bool _noteThisFrame;
        bool _hasHands;
        bool _openHandPresent;
        /// <summary>
        /// The audio clock as it was read at the start of this frame: the one clock of the visit. The cutoff (EndNow and so the start of the restart lock), the visit's state machine (<c>Experience.Update</c>), the
        /// record screen and the tutorial (which compares it with <c>StateSince</c>, the time the meadow became Idle) all take their time from it, so that no two of them can disagree by the length of a frame.
        /// </summary>
        double _frameNow;
        int _handNotes;
        CutoffDirector _cutoff;
        EndWordView _endWord;
        SongTitleView _songTitle;
        string[] _endWordFonts;

        public DownbeatApp(IAudioEngine audio, IContentRepository content, IPlayInput input, PlayableRange range, HandSettingsHolder handSettings,
            IHandSource handSource, IHandMotionView handMotion, MeadowStage meadow, LaunchArgs launch, CoroutineRunner runner, ConductPlayer conduct, SettingsPanelView settingsPanel)
        {
            _conduct = conduct;
            _settingsPanel = settingsPanel;
            _handSettings = handSettings;
            _handSource = handSource;
            _handMotion = handMotion;
            _audio = audio;
            _content = content;
            _input = input;
            _range = range;
            _meadow = meadow;
            _launch = launch;
            _runner = runner;
            foreach (var seconds in SecondsList.Parse(launch.Get("reload-content-at"))) _reloadAtSec.Enqueue(seconds);
        }

        public bool IsRunning => _state == State.Running;
        public PerformanceEnergy Energy => _energy;
        /// <summary>Automatic quality (null until the content is loaded).</summary>
        public AutoQualityRunner Quality => _quality;
        /// <summary>Notes the hand (or the mouse) played since the app started.</summary>
        public int HandNotes => _handNotes;
        public ExperienceStateMachine Experience => _experience;
        /// <summary>
        /// The piece was ended, at this audio time: by the player with both fists spread apart, or by the end of the last bar of a song (the visit is over, the voices fade out, the card is made, the word is shown).
        /// It is raised in the frame it happens, also when no visit was going on (the follow-along guide shows the gesture to somebody who is not playing).
        /// </summary>
        public event Action<double> PerformanceCutoff;
        /// <summary>Every note that was scheduled for the hands, with the sound that was chosen for it (for tests and measurements; nothing in the app listens).</summary>
        public event Action<ScheduledNote, SampleResolution> NoteScheduled;
        /// <summary>Every note of the nature score (fireflies, sky, hills) in the frame in which it was scheduled (for tests and measurements; nothing in the app listens).</summary>
        public event Action<NatureEvent> NatureScheduled;
        /// <summary>The word that says the piece is over: made when the meadow is built, shown whenever a piece is ended, with the hands or at the end of a song.</summary>
        public EndWordView EndWord => _endWord;
        /// <summary>The title of the song, shown when its first bar begins (null until the meadow is built; it is hidden except for the few seconds of the title).</summary>
        public SongTitleView SongTitle => _songTitle;
        /// <summary>Which song a visit plays and where it is (null until the content is loaded, and when the content has no song the instruments can play: every visit is free play).</summary>
        public SongSession Songs => _songs;
        /// <summary>How many times the piece was ended with the hands (0 until the content is loaded); the end of a song is not counted.</summary>
        public int CutoffCount => _cutoff?.CutCount ?? 0;
        public NatureAudioBridge NatureBridge => _natureBridge;
        public NatureVisuals NatureVisuals => _natureVisuals;
        /// <summary>The record screen (null with <c>--no-record</c>).</summary>
        public RecordCoordinator Record => _record;
        public DisplayRouter Display => _display;
        /// <summary>The follow-along tutorial (null until the meadow runs, and with <c>--no-tutorial</c>).</summary>
        public TutorialCoordinator Tutorial => _tutorial;
        /// <summary>Reads the content again on F5 (null until the app is running).</summary>
        public ContentReload ContentReload { get; private set; }
        /// <summary>The panel that the Tab key opens: the song switch and the strip switch. It exists from the start; it is set up when the content is loaded.</summary>
        public SettingsPanelView SettingsPanel => _settingsPanel;
        /// <summary>Where the choice of the song mode switch is kept (null until the content is loaded).</summary>
        public SongModeStore SongModeStore => _songModeStore;
        /// <summary>The white square of <c>--latency-mode</c> (null without it).</summary>
        public LatencyMarker Latency { get; private set; }
        /// <summary><c>--reload-content-at 6,14</c>: presses "F5" by itself at these seconds of the run (checking the reload without a keyboard), in order.</summary>
        readonly Queue<double> _reloadAtSec = new Queue<double>();

        /// <summary>Keeps the performance energy at <paramref name="value"/> (0..1) and the meadow awake, as <c>--hold-energy</c> does (tests and captures).</summary>
        public void HoldEnergy(float value) => _holdEnergy = Mathf.Clamp01(value);

        /// <summary>The energy follows the notes again.</summary>
        public void ReleaseEnergy() => _holdEnergy = float.NaN;
        public string FailureMessage { get; private set; } = string.Empty;
        /// <summary>The self-exit of a player (null in the editor and with <c>--stall-exit-sec 0</c>); see <see cref="PlayerGuard"/>.</summary>
        public PlayerGuard Guard => _guard;

        public void Start()
        {
            Application.runInBackground = true; // an exhibition PC must keep playing when another window (or the supervisor) takes focus
            Application.logMessageReceivedThreaded -= CountError; // a scene that is loaded again must not count every error twice
            Application.logMessageReceivedThreaded += CountError;
            var syncPath = _launch.Get("log-bloom-sync");
            if (!string.IsNullOrEmpty(syncPath)) Application.quitting += () => WriteBloomSync(syncPath);
            BuildOps(); // the heartbeat and the health log run from the first frame: a sound device that never comes up must not look like a dead app
            BuildGuard(); // after the health folder is known (the reason of a self-exit is written there); the device-lost hook works from here, the freeze watch from the moment the app runs
            _runner.StartCoroutine(GuardedRoutine.Run(Boot(), OnBootThrew)); // an error in Boot must end in Failed (and a log line), not in a coroutine that Unity stops without a word
        }

        void OnBootThrew(Exception error)
        {
            Debug.LogException(error);
            Fail($"boot: {error.GetType().Name}: {error.Message}");
        }

        public void Tick()
        {
            _guard?.Beat();   // first thing: this is the beat of the main loop that the watchdog thread watches
            _audio.Tick();
            _frameNow = _audio.Now;
            TickOps();
            if (_state != State.Running) return;

            _input.Tick(Time.unscaledTime);
            _meadow.SetHands(_handMotion);
            _noteThisFrame = false;
            _inputNoteThisFrame = false;
            var piecePlayed = _cutoff != null && _cutoff.Update(_frameNow, _handSource.Generation, _handMotion, HandAspect());
            piecePlayed |= _songs != null && _songs.Tick(_frameNow);   // after the hands: when both end the piece in one frame the cutoff ended it and the song has nothing left to end
            while (_input.TryDequeue(out var intent))
            {
                if (!piecePlayed) Handle(intent);   // the notes of the frame in which the piece was ended are not played: the voices were just faded out
            }

            var heard = _audio.VisualNow;
            if (_reloadAtSec.Count > 0 && Time.realtimeSinceStartupAsDouble >= _reloadAtSec.Peek()) { _reloadAtSec.Dequeue(); ContentReload.Request(); }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F5)) ContentReload.Request();   // the namespace Downbeat.Input hides the class Input here
            ContentReload.Tick(heard);
            TickNature(heard);
            TickQuality();   // after the hands and the notes of this frame have been seen: a rebuild of the grass must not come on the frame in which a visit begins
            TickSettingsPanel();
            TickRecord(heard);
            TickTutorial();
            _poseLog?.Tick(Time.realtimeSinceStartupAsDouble);
            if (_frameTimes.Count < _frameTimes.Capacity) _frameTimes.Add(Time.unscaledDeltaTime);
            _endWord?.Tick(Time.realtimeSinceStartupAsDouble);
            TickSongTitle(heard);
            _meadow.Tick(heard);
            _due.Clear();
            _queue.DrainDue(heard, FlashLeadSec, _due);
            for (var i = 0; i < _due.Count; i++)
            {
                _audio.LogFlash(_due[i].AudioTime, heard);
                Latency?.NoteHeard();
            }
        }

        IEnumerator Boot()
        {
            Debug.Log("[Downbeat] loading content");
            ContentBundle bundle = null;
            string failure = null;
            yield return _content.Load(b => bundle = b, e => failure = e);
            WriteContentSourceNote(_health?.Directory); // which copy of the content runs, also when none could be loaded
            if (bundle == null) { Fail(failure); yield break; }

            var dto = bundle.Dto;
            _song = dto.song;
            var scale = new ScaleSpec(_song.scaleSteps, _song.pitchSteps);
            _mapper = new PitchMapper(dto.instruments, scale);
            _natureMapper = new NatureMapper(dto.nature.instruments, scale, _mapper.AllSamples.Count); // their samples are loaded after the hand instruments' samples
            _range.Set(scale.PitchSteps);
            _handSettings.Set(dto.hand, dto.pose, dto.cells);
            // --play-mode conduct|strike overrides content conduct.mode; the latency measurement is strike to sound, so --latency-mode strikes unless --play-mode says otherwise
            var conductProblem = _conduct.Configure(dto, ConductSettings.ModeOverride(_launch));
            if (conductProblem != null) Debug.LogWarning("[Downbeat] " + conductProblem);
            Debug.Log($"[Downbeat] hand play mode: {(_conduct.IsConducting ? ConductSettings.ConductName : ConductSettings.StrikeName)}");
            if (_handSource is IPreparable preparable)
            {
                Debug.Log("[Downbeat] preparing the hand input");
                yield return preparable.Prepare(e => failure = e);
                if (failure != null) { Fail(failure); yield break; }
            }
            _meadow.WindMuted = ReadWindMute(_launch);
            _meadow.DemoWaves = _launch.Has("demo-waves"); // --demo-waves: a sweep and two rings shortly after start (captures)
            if (_launch.TryGetDouble("fireflies", out var fireflies)) _meadow.SetFireflyLevel((float)fireflies); // --fireflies 0..1: start with fireflies (captures, tests)
            // --quality High|Low locks a tier of meadow.quality; Auto (the default) starts from the graphics device and goes on by itself (autoQuality in content.json)
            _quality = AutoQualityRunner.Create(bundle.Dto, _launch, _meadow, new GpuInfo(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize), out var startTier);
            _meadow.Launch = _launch;
            _meadow.Configure(bundle, startTier);
            _quality.ApplyStart();
            ApplyFullscreenKind(dto.display);
            if (_launch.TryGetDouble("sky", out var skyT)) _meadow.Backdrop.SetSunset((float)skyT); // --sky 0..1: start on a chosen point of the sky arc (captures, tests)

            _audio.Configure(Mathf.Pow(10f, dto.audio.masterGainDb / DbToLinearDivisor), dto.audio.maxVoices);
            var requests = new List<SampleLoadRequest>(_mapper.AllSamples.Count + _natureMapper.AllSamples.Count);
            for (var i = 0; i < _mapper.AllSamples.Count; i++) requests.Add(new SampleLoadRequest(i, bundle.BaseUrl + _mapper.AllSamples[i].file));
            for (var i = 0; i < _natureMapper.AllSamples.Count; i++) requests.Add(new SampleLoadRequest(_mapper.AllSamples.Count + i, bundle.BaseUrl + _natureMapper.AllSamples[i].file, isNature: true));
            _audio.LoadSamples(requests);

            Debug.Log("[Downbeat] loading sounds");
            var started = Time.realtimeSinceStartup;
            var timeoutSec = _launch.TryGetDouble("audio-timeout", out var seconds) && seconds > 0.0 ? (float)seconds : BootTimeoutSec;
            while (!_audio.IsRunning)
            {
                if (_audio.SamplesFailed > 0) { Fail($"{_audio.SamplesFailed} sample(s) failed to load"); yield break; }
                if (Time.realtimeSinceStartup - started > timeoutSec) { Fail("audio did not start (is a sound device there and switched on?)", audioProblem: true); yield break; }
                yield return null;
            }

            _content.ConfirmLoaded(); // the sounds are in: this content really starts, so it may become the last known good copy
            // Day 0: currentTime can sit still for ~1 s after resume(), so the grid origin is taken only now that the clock moves.
            _grid = new BeatGrid(_audio.Now + dto.audio.originDelaySec, _song.bpm, _song.slotsPerBeat);
            _scheduler = new QuantizedNoteScheduler(_grid, BuildSettings(dto));
            (_input as IGridAwareInput)?.OnGridReady(_grid, _song.beatsPerBar * _song.slotsPerBeat);
            BuildNature(dto);
            Latency = LatencyMarker.Begin(_launch);
            ContentReload = new ContentReload(_content, _runner, _meadow, bundle, _grid, _song.beatsPerBar * _song.slotsPerBeat, () => _audio.VisualNow);
            _state = State.Running;
            _guard?.MarkRunning();   // only now: a slow boot is not a freeze (the supervisor has a grace period of its own for it)
            Debug.Log("[Downbeat] ready");
        }

        /// <summary><c>--wind-mute breeze,waves,hands,sparkle,phase,direction|all</c>: switches parts of the wind off (measurements); a name that is not a part is reported.</summary>
        static WindTerm ReadWindMute(LaunchArgs launch)
        {
            var muted = WindMute.Parse(launch.Get("wind-mute"), out var unknown);
            if (unknown.Length > 0) Debug.LogWarning($"[Downbeat] --wind-mute: unknown part(s) '{unknown}' ignored");
            if (muted != WindTerm.None) Debug.Log($"[Downbeat] wind muted: {muted}");
            return muted;
        }

        SchedulerSettings BuildSettings(ContentDto dto)
        {
            var gaps = new int[Parts.Count];
            foreach (var entry in dto.song.partMinGapSlots)
            {
                if (Parts.TryParse(entry.part, out var part)) gaps[(int)part] = entry.slots;
            }
            return new SchedulerSettings(dto.audio.minLeadSec, dto.song.maxPushSlots, gaps);
        }

        /// <summary>The energy, the flow of a visit, the nature score and what it does to the picture; <c>--hold-energy V</c>, <c>--log-experience PATH</c>, <c>--record-dir DIR</c> are for captures and measurements.</summary>
        void BuildNature(ContentDto dto)
        {
            var nature = dto.nature;
            var slotsPerBar = _song.beatsPerBar * _song.slotsPerBeat;
            _energy = new PerformanceEnergy(NatureSettingsFactory.Energy(nature.energy));
            var cutoffSettings = CutoffSettings.From(dto.cutoff);
            _experience = new ExperienceStateMachine(NatureSettingsFactory.Experience(dto.experience).WithRestartLockSec(cutoffSettings.RestartLockSec));
            _conduct.SetPlayGate(() => !_experience.AwaitsOpenHand);   // after a cutoff nothing plays (an unsure hand included) until an open hand starts the next visit
            _cutoff = new CutoffDirector(cutoffSettings, _experience, _audio, _energy, _conduct.Reset, HandCellTracker.HoldSecOf(_conduct.Settings, _handSettings.Settings));
            _cutoff.Cut += OnCutoff;
            _endWordFonts = dto.record.fontNames;
            _presence = new HandPresenceMonitor(HandFrameStaleSec, dto.experience.inputPresenceSec);
            var conductor = new NatureLayerConductor(NatureSettingsFactory.Rules(nature.rules, _song.slotsPerBeat), slotsPerBar, _song.pitchSteps, nature.seed);
            var barSec = 60.0 / _song.bpm * _song.beatsPerBar;
            _natureBridge = new NatureAudioBridge(_audio, _grid, conductor, _natureMapper, nature.leadBars * barSec, dto.audio.minLeadSec);
            _natureVisuals = new NatureVisuals(_meadow, nature, _grid, _song.beatsPerBar, controlSky: !_launch.Has("sky"), controlFireflies: !_launch.Has("fireflies"));
            _experience.SessionStarted += _ => _sessions++;
            _experience.SessionStarted += _ => WakeMeadow();
            BuildSongs(dto, conductor);   // before the record screen: the card asks the songs which one the visit played
            if (_launch.TryGetDouble("hold-energy", out var hold)) _holdEnergy = Mathf.Clamp01((float)hold); // --hold-energy 0..1: the energy stays there (captures)

            var logPath = _launch.Get("log-experience");
            if (!string.IsNullOrEmpty(logPath))
            {
                _experienceLog = new ExperienceLog(LogIntervalSec, "Mac player, window; t is run time in seconds since the meadow started; energy/level/state are the performance energy, its level 0..3 and the flow of a visit");
                _experience.SessionStarted += t => _experienceLog.SessionStarted(t, _energy.Value);
                _experience.SessionEnded += t => _experienceLog.SessionEnded(t, _energy.Value);
                Application.quitting += () => WriteExperienceLog(logPath);
            }
            if (!string.IsNullOrEmpty(_launch.Get("record-dir")))
            {
                _recorder = new RunRecorder(_audio, _runner, _launch);
                _recorder.Start();
            }
            BuildRecord(dto);
            BuildSettingsPanel(dto);
            EnsureEndWord();
            EnsureSongTitle();
            BuildTutorial(dto);
            BuildPoseLog(dto);
            WindowCompare.Begin(_launch, _meadow.Camera);
            FrameDump.Begin(_launch, _meadow);
            BloomTuning.Apply(_launch);
        }

        /// <summary>
        /// The songs. <c>--songs on|off</c> beats the choice saved by the Tab panel, which beats content <c>experience.songMode</c>; the book is the songs of the content that the instruments can play (what is left out is
        /// in the log). With no song left, and with <c>--latency-mode</c> (a measurement must not be stopped by the end of a song), every visit is free play and there is no session at all. The session is subscribed to the visit before the record screen is built, and gives the sky its chord and the
        /// card its song. A visit can only begin once the app is running, which is after the beat grid was handed to the hands' input (<see cref="Boot"/>), so a song can always be started on a bar line of it.
        /// </summary>
        void BuildSongs(ContentDto dto, NatureLayerConductor sky)
        {
            var book = SongBook.Build(dto);
            foreach (var line in SongBookReport.Lines(book))
            {
                if (line.IsWarning) Debug.LogWarning(line.Text);
                else Debug.Log(line.Text);
            }
            _songModeStore = SongModeStore.Open(_launch);
            _songModeOn = SongModeSetting.Resolve(_launch.Get("songs"), _songModeStore.Load(), dto.experience.songMode, out var songsProblem);
            if (songsProblem != null) Debug.LogWarning("[songs] " + songsProblem);
            _songTitleSec = SongTitleCue.DurationSec(dto.experience.songTitleSec);
            if (book.Songs.Count == 0) return;
            if (_launch.Has("latency-mode"))
            {
                Debug.Log("[songs] --latency-mode: no song is played (the end of a song must not stop a measurement)");
                return;
            }

            var scale = new ScaleSpec(_song.scaleSteps, _song.pitchSteps);
            _songs = new SongSession(book.Songs, _conduct, _cutoff, scale, SkyRootMidi(dto), dto.record.minNotes, _songModeOn);
            _songs.SongStarted += OnSongStarted;
            _experience.SessionStarted += _songs.OnSessionStarted;
            _experience.SessionEnded += _songs.OnSessionEnded;
            _experience.SessionEnded += _ => CancelSongTitle();
            sky.SetChordSource(_songs.SkyStepsForBar);
        }

        /// <summary>The MIDI note of step 0 of the sky's instrument: the chord of a bar of a song is played by the sky in steps of its scale.</summary>
        int SkyRootMidi(ContentDto dto)
        {
            foreach (var instrument in dto.nature.instruments)
            {
                if (NatureLayers.TryParse(instrument.layer, out var layer) && layer == NatureLayer.Sky) return instrument.rootMidi;
            }
            return _song.rootMidi;
        }

        void OnSongStarted(SongScore song, double barOneAudioTime)
        {
            Debug.Log($"[songs] start {song.Id} at slot {_grid.FirstSlotAtOrAfter(barOneAudioTime)}");
            _titleCue.Arm(song.Title, barOneAudioTime);
        }

        /// <summary>The title shows when the first bar of the song is heard, and goes with the visit or the piece.</summary>
        void TickSongTitle(double heard)
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (_titleCue.TryTake(heard, out var title))
            {
                EnsureSongTitle();
                _songTitle?.SetTitle(title);
                _songTitle?.Show(now, _songTitleSec);
            }
            _songTitle?.Tick(now);
        }

        void CancelSongTitle()
        {
            _titleCue.Cancel();
            _songTitle?.Hide();
        }

        /// <summary>
        /// The record screen and which screen shows what. <c>--display auto|dual|single</c> overrides content <c>display.mode</c>; <c>--no-record</c> leaves the record out; <c>--cards-dir PATH</c> keeps the cards
        /// somewhere else than the app's data folder; <c>--record-offscreen</c> draws the record into a picture of its own every frame (to measure what a second screen costs), and
        /// <c>--capture-screens PATH</c> draws it into one picture next to the meadow once, and <c>--record-source record</c> makes <c>--record-dir</c> record the record screen instead of the meadow.
        /// </summary>
        void BuildRecord(ContentDto dto)
        {
            if (_launch.Has("no-record")) return;
            if (RunRecorder.RecordSourceIgnored(_launch)) Debug.LogWarning("[record] --record-source record needs --record-dir (there is no recorder to film the record screen): the option is ignored");
            var offscreen = _launch.Has("record-offscreen") || _launch.Has("capture-screens") || RunRecorder.WantsRecordScreen(_launch);
            _display = new DisplayRouter(dto, _launch, new UnityDisplayApi(), Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor);
            if (!offscreen) _display.Apply(_meadow.Camera);
            var mount = offscreen ? RecordMount.Offscreen : _display.Mount;

            var view = new GameObject("RecordScreenView").AddComponent<RecordScreenView>();
            // the editor (and so the tests) keep their cards in the temporary cache, never among the real cards of the app
            var cardsRoot = !string.IsNullOrEmpty(_launch.Get("cards-dir")) ? Path.GetFullPath(_launch.Get("cards-dir"))
                : Path.Combine(Application.isEditor ? Application.temporaryCachePath : Application.persistentDataPath, dto.record.cardFolder);
            _record = new RecordCoordinator(dto, cardsRoot, _meadow.Camera, view, _experience);
            if (_songs != null) _record.SetSongSource(() => _songs.VisitLabel);   // a card names the song its visit was played to
            view.Configure(dto, _record.Score, _meadow.Camera, mount, _display.Assignment.RecordDisplay, continuousOffscreen: _launch.Has("record-offscreen"));
            _record.Start();
            _stripFraction = mount == RecordMount.Strip ? dto.display.overlayFraction : 0f;
            _meadow.Overlay.StripFraction = _stripFraction;   // what the effects drawn at a hand's place keep clear of
            if (RunRecorder.WantsRecordScreen(_launch)) _recorder?.UseRecordScreen(view);
            _screensCapture = new ScreensCapture(_launch, _meadow.Camera, view);
            Debug.Log($"[record] {mount}, meadow on screen {_display.Assignment.MeadowDisplay}, record on screen {_display.Assignment.RecordDisplay} ({UnityEngine.Display.displays.Length} screen(s)); cards in {cardsRoot}");
        }

        /// <summary>The follow-along tutorial on the overlay layer in front of the meadow camera; <c>--no-tutorial</c> leaves it out (captures and measurements of a meadow that waits).</summary>
        void BuildTutorial(ContentDto dto)
        {
            if (_launch.Has("no-tutorial") || _meadow.Overlay == null) return;
            _tutorial = new TutorialCoordinator(dto, _meadow.Overlay, _meadow.Bundle);
        }

        /// <summary><c>--log-pose</c>: a line per hand about once a second in Player.log (finger ratios, pose, <c>canPlay</c>, cell) to tune the pose thresholds on the machine. Off without the option.</summary>
        void BuildPoseLog(ContentDto dto)
        {
            if (!_launch.Has("log-pose")) return;
            _poseLog = new PoseLogger(HandCellTracker.From(dto), _handMotion, PoseSettings.From(dto.pose));
        }

        /// <summary>After the visit and the notes of this frame are known: the tutorial only watches the hands and the flow of the visit, it never holds anything back.</summary>
        void TickTutorial()
        {
            _tutorial?.Tick(_frameNow, _experience.State, _experience.StateSince, _handMotion, _inputNoteThisFrame, Time.realtimeSinceStartupAsDouble);
        }

        void TickRecord(double heard)
        {
            if (_record == null) return;
            _record.Tick(heard, _frameNow, _energy.Level, _natureEvents);
            _screensCapture?.Tick(Time.realtimeSinceStartupAsDouble, heard);
        }

        /// <summary>
        /// The Tab panel. The content gives it the fonts; the choice of the song mode is kept in <see cref="SongModeStore"/> (<c>--settings-file PATH</c>, else the data folder), and the panel starts with the answer
        /// that <see cref="BuildSongs"/> worked out and passes what the operator chooses on to the songs (with no song session the row only says that there is no song); on one screen with the record strip the panel also gets the strip switch, the job that the Tab key had before the panel.
        /// </summary>
        void BuildSettingsPanel(ContentDto dto)
        {
            _settingsPanel.Configure(dto.record?.fontNames, _songModeStore);
            _settingsPanel.SongModeOn = _songModeOn;
            _settingsPanel.HasSongs = _songs != null;   // with no session the row says "곡 없음" and a click would change nothing
            _settingsPanel.SongModeChanged += on => _songs?.SetEnabled(on);
            if (_record != null && _display.Mode == DisplayMode.SingleOverlay && _record.View.Mount == RecordMount.Strip) _settingsPanel.AddStripSwitch(() => _record.View.Visible, ToggleStrip);
        }

        /// <summary>Tab opens and closes the panel in every display mode; the panel closes itself when nobody touches it.</summary>
        void TickSettingsPanel()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) _settingsPanel.ToggleOpen(now);
            _settingsPanel.Tick(now);
        }

        /// <summary>The strip switch of the panel: hides the record strip that is shown, shows the one that is hidden, and tells the overlay how much of the picture the strip covers (the cells and the hands keep clear of it).</summary>
        void ToggleStrip()
        {
            _record.View.Visible = _display.ToggleStrip();
            _meadow.Overlay.StripFraction = _record.View.Visible ? _stripFraction : 0f;
        }

        /// <summary>Borderless or exclusive full screen (<c>display.fullscreenMode</c>, <c>--fullscreen-mode</c>): only for a Windows player that was started full screen.</summary>
        void ApplyFullscreenKind(DisplayDto display)
        {
            if (!FullscreenChoices.ShouldApply(Application.platform == RuntimePlatform.WindowsPlayer, Screen.fullScreen)) return;
            var choice = FullscreenChoices.Parse(_launch.Has("fullscreen-mode") ? _launch.Get("fullscreen-mode") : display.fullscreenMode);
            Screen.fullScreenMode = choice == FullscreenChoice.Exclusive ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
            Debug.Log($"[display] full screen kind: {choice}");
        }

        /// <summary>Which copy of the content runs (the folder next to the program, the last known good copy or the built-in one) and why, for the operator: <c>content-source.txt</c> beside the health log.</summary>
        static void WriteContentSourceNote(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            var source = ContentLocator.Active;
            var text = source == null
                ? "origin=Editor\nthe Editor reads the content folder of the repository\n"
                : $"time_utc={DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}\norigin={source.Origin}\nroot={source.RootDir}\nreason={(source.Reason.Length == 0 ? "(none: the content folder next to the program is in use)" : source.Reason)}\n";
            try { File.WriteAllText(Path.Combine(directory, "content-source.txt"), text); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning("[content] could not write content-source.txt: " + e.Message); }
        }

        /// <summary>
        /// The health log and the request to keep the PC awake. A player writes <c>logs/health-yyyyMMdd.csv</c> and <c>logs/heartbeat.txt</c> next to the executable (the editor writes nothing
        /// unless <c>--health-dir PATH</c> says where); <c>--soak</c> puts them in <c>measurements/soak-yyyyMMdd</c> next to the executable; <c>--health-interval SEC</c> changes the minute.
        /// On a Windows player the PC is asked to stay awake until the app quits (<c>--allow-sleep</c> turns that off).
        /// </summary>
        void BuildOps()
        {
            var explicitDir = _launch.Get("health-dir");
            var wantsLog = !Application.isEditor || !string.IsNullOrEmpty(explicitDir);
            if (wantsLog)
            {
                var executableDir = ContentLocator.ExecutableDirectory();
                var primary = !string.IsNullOrEmpty(explicitDir) ? Path.GetFullPath(explicitDir)
                    : _launch.Has(SoakMode.Flag) ? Path.Combine(executableDir, "measurements", "soak-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                    : Path.Combine(executableDir, LogFolder);
                var writer = new HealthFileWriter(primary, Path.Combine(Application.persistentDataPath, LogFolder), message => Debug.LogWarning("[health] " + message));
                var interval = _launch.TryGetDouble("health-interval", out var seconds) && seconds > 0.0 ? seconds : HealthRowIntervalSec;
                var beatInterval = _launch.TryGetDouble("heartbeat-interval", out var beatSeconds) && beatSeconds > 0.0 ? beatSeconds : HeartbeatIntervalSec;
                _health = new HealthLogger(writer, new HealthSource(this), interval, beatInterval);
                Application.quitting += () => _health.Flush(Time.realtimeSinceStartupAsDouble);
                Debug.Log($"[health] writing to {writer.Directory ?? "(nowhere: no folder can be written)"} every {interval:0} s");
                WarnIfTheSupervisorCannotSee(writer);
            }

            if (_launch.Has("allow-sleep")) return;
            _keepAwake = new KeepAwake(Application.platform, NativePower.SetState);
            if (_keepAwake.Enter()) Debug.Log("[power] asked Windows to stay awake while the app runs (no setting is changed)");
            else if (KeepAwake.IsSupported(Application.platform)) Debug.LogWarning("[power] could not ask Windows to stay awake: " + _keepAwake.LastProblem);
            Application.quitting += () => _keepAwake.Release();
            _nextKeepAwakeRefresh = Time.realtimeSinceStartupAsDouble + KeepAwakeRefreshSec;
        }

        /// <summary>
        /// The player ends itself when its main loop stands still for the limit, or at once when the log hook receives a line that says the graphics device is lost (<see cref="PlayerGuard"/>;
        /// <c>--stall-exit-sec N</c>, 0 = off). The reason goes to the player log and to <c>exit-reasons.txt</c> in the health folder. A player only: the editor never ends itself.
        /// </summary>
        void BuildGuard()
        {
            Action<string> record = _health == null ? null : _health.AppendExitRecord;
            _guard = PlayerGuard.Begin(_launch, Application.isEditor, record, message => Debug.LogWarning("[selfexit] " + message));
            if (_guard == null) return;
            Application.quitting += _guard.NoteQuitting;
            Debug.Log($"[selfexit] on: the player ends itself when its main loop stands still for {_guard.Watchdog.StallSec.ToString("0.##", CultureInfo.InvariantCulture)} s, or at once when the log hook receives a line that says the graphics device is lost (--{StallExitSetting.Flag} 0 turns this off)");
        }

        /// <summary>The supervisor reads the folder it started the app with (<c>--health-dir</c>); when the app has to write somewhere else it sees no heartbeat and restarts the app again and again. That must not pass quietly.</summary>
        static void WarnIfTheSupervisorCannotSee(HealthFileWriter writer)
        {
            if (writer.IsActive && !writer.UsedFallback) return;
            var where = writer.IsActive ? $"in {writer.Directory} instead" : "nowhere";
            Debug.LogError($"[health] the heartbeat and the health log cannot be written in {writer.PrimaryDirectory}; they go {where}. A supervisor reads {writer.PrimaryDirectory}, finds no heartbeat and will restart this app over and over. Make that folder writable (or run the app from a folder you can write to).");
        }

        void TickOps()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (_health != null)
            {
                var frameSeq = _handMotion.FrameSeq;
                var frameArrived = frameSeq != _lastHandFrameSeq;
                _lastHandFrameSeq = frameSeq;
                _lastHandFrameArrived = frameArrived;
                var packetAgeMs = frameArrived && _handSource is UdpHandSource udp ? udp.PacketAgeMs : double.NaN;
                _health.Tick(now, Time.unscaledDeltaTime, _audio.Now, _hasHands, frameArrived, countFrames: _state == State.Running, trackerAgeMs: packetAgeMs);
            }
            if (_keepAwake != null && _keepAwake.IsWanted && now >= _nextKeepAwakeRefresh)
            {
                _keepAwake.Refresh();
                _nextKeepAwakeRefresh = now + KeepAwakeRefreshSec;
            }
        }

        static void CountError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Interlocked.Increment(ref _errorCount);
        }

        /// <summary>What the health logger reads from the running app when a row is due.</summary>
        sealed class HealthSource : IHealthSource
        {
            readonly DownbeatApp _app;

            public HealthSource(DownbeatApp app)
            {
                _app = app;
            }

            public DateTime UtcNow => DateTime.UtcNow;
            public AudioStats Audio => _app._audio.Stats;
            public int Errors => Volatile.Read(ref _errorCount);
            public bool AudioFailed => _app._audioFailed;
            public AppPhase App => _app._state == State.Running ? AppPhase.Running : _app._state == State.Failed ? AppPhase.Failed : AppPhase.Loading;
            public string ExperienceState => _app._experience != null ? _app._experience.State.ToString() : _app._state.ToString(); // Loading or Failed until the meadow runs
            public int EnergyLevel => _app._energy?.Level ?? 0;
            public int Sessions => _app._sessions;
            public float RenderScale => Downbeat.Meadow.RenderLevers.RenderScale;
            public int QualityLevel => _app._quality == null ? -1 : _app._quality.Level;
            public bool StarveIgnored => _app._quality != null && _app._quality.StarveIgnored;
            public int TakeQualityChanges() => _app._quality == null ? 0 : _app._quality.TakeChangeCount();
            public int NatureDropped => _app._natureBridge?.Dropped ?? 0;
            public long MonoBytes => GC.GetTotalMemory(false);
            public long AllocatedBytes => Profiler.GetTotalAllocatedMemoryLong();

            bool _workingSetProblemReported;

            public long WorkingSetBytes
            {
                get
                {
                    if (ProcessMemory.TryGetWorkingSetBytes(out var bytes, out var problem)) return bytes;
                    if (_workingSetProblemReported) return 0L;
                    _workingSetProblemReported = true;   // once: the column is read every minute
                    Debug.LogWarning("[health] working_set_mb stays 0: " + problem);
                    return 0L;
                }
            }
        }

        void TickQuality()
        {
            if (_quality == null) return;
            var trackerRunning = _handSource is UdpHandSource && _handSource.State == TrackerState.Running;
            var idle = _experience != null && QualityIdle.Is(_experience.State, _handMotion.Count, _noteThisFrame);
            _quality.Tick(Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime * 1000f, trackerRunning, _lastHandFrameArrived, idle);
        }

        void TickNature(double heard)
        {
            if (_energy == null) return;
            var now = _frameNow;
            var dt = Mathf.Min(Time.unscaledDeltaTime, MaxEnergyStepSec);
            if (float.IsNaN(_holdEnergy)) _energy.Advance(dt);
            else _energy.Set(_holdEnergy);

            // only a hand that plays counts as somebody being there (HandPlayRule: not a fist, not a hand that has just come in): two fists alone never wake the meadow or start a visit; a visit that was ended with the hands
            // starts again for an open hand
            HandPresenceMonitor.CountPlayers(_handMotion, out var playing, out var open);
            _hasHands = _presence.Update(now, _handSource.State, _handMotion.FrameSeq, playing, open, _inputNoteThisFrame, out _openHandPresent);
            _experience.Update(now, _hasHands, _noteThisFrame, _energy.Value, _openHandPresent);
            var awake = _experience.State != ExperienceState.Idle || !float.IsNaN(_holdEnergy);

            _natureEvents.Clear();
            _natureBridge.Tick(now, _energy.Level, _natureEvents);
            if (NatureScheduled != null)
            {
                for (var i = 0; i < _natureEvents.Count; i++) NatureScheduled(_natureEvents[i]);
            }
            _natureVisuals.Tick(heard, dt, _energy.Value, _energy.Level, awake, _natureEvents);

            _experienceLog?.Observe(now, _energy.Value, _energy.Level, _experience.State, _natureVisuals.Model.SkyT, _natureVisuals.Model.FireflyLevel, _hasHands, _handNotes, _natureBridge.Scheduled, _natureBridge.Dropped);
        }

        /// <summary>Width / height of the picture the tracker sees (the hands' x and y are fractions of different lengths).</summary>
        float HandAspect() => _handMotion is HandMotionTracker tracker ? tracker.Aspect : DefaultHandAspect;

        /// <summary>The word that ends a piece, made in advance (its font and its glyph are not made in the frame in which it is wanted): on the meadow's overlay layer, hidden until the piece is ended.</summary>
        void EnsureEndWord()
        {
            if (_meadow == null || !_meadow.IsConfigured || _meadow.Overlay == null) return;
            if (_endWord != null && _endWord.Layer == _meadow.Overlay) return;
            if (_endWord != null) UnityEngine.Object.Destroy(_endWord.gameObject);
            _endWord = EndWordView.Create(_meadow.Overlay, _endWordFonts);
        }

        /// <summary>The title of a song, made in advance like the word of the end: on the meadow's overlay layer, hidden until a song begins.</summary>
        void EnsureSongTitle()
        {
            if (_meadow == null || !_meadow.IsConfigured || _meadow.Overlay == null) return;
            if (_songTitle != null && _songTitle.Layer == _meadow.Overlay) return;
            if (_songTitle != null) UnityEngine.Object.Destroy(_songTitle.gameObject);
            _songTitle = SongTitleView.Create(_meadow.Overlay, _endWordFonts);
        }

        /// <summary>The piece was ended, with the hands or at the end of a song: the word "끝" in the middle of the meadow, and the event for whoever follows.</summary>
        void OnCutoff(double now)
        {
            EnsureEndWord();   // made when the meadow was built; only a layer that was made again since needs a new one
            _endWord?.Show(Time.realtimeSinceStartupAsDouble, _cutoff.Settings.EndWordSec);
            // the notes that were asked for and are not heard yet (their sound was cancelled by FadeOutAll) show nothing either: no flower opens, no wind, petal or star dust, no mark on the score, no flash
            _queue.RemoveAfter(now);
            _record?.CancelNotesAfter(now);
            _meadow?.CancelNotesHeardAfter(now, _audio.VisualNow);
            if (_cutoff.LastEndReason == PieceEndReason.SongEnd) Debug.Log($"[songs] end {_songs?.VisitSong?.Id}");
            else Debug.Log($"[cutoff] the piece was ended with both fists at {now:0.00} s");
            PerformanceCutoff?.Invoke(now);
        }

        /// <summary>A visit began: the buds around the hand that came in glint and a soft gust leaves it (the middle of the field when there is no hand, as with the mouse).</summary>
        void WakeMeadow()
        {
            if (_meadow == null || !_meadow.IsConfigured) return;
            _meadow.Wake(WakeOrigin.Of(_handMotion, _meadow.Layout, _meadow.Placement.TryGroundOfHand), _audio.VisualNow);
        }

        void WriteExperienceLog(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, _experienceLog.ToJson());
                var summary = _experienceLog.Data.summary;
                Debug.Log($"[experience] max level {summary.maxLevel}, peak energy {summary.peakEnergy:0.00}, {summary.sessions} visit(s), back to idle at {summary.returnedToIdleAt:0.0} s, {summary.natureScheduled} nature notes ({summary.natureDropped} dropped) -> {path}");
            }
            catch (IOException e)
            {
                Debug.LogError("[experience] could not write the log: " + e.Message);
            }
        }

        /// <summary>The sound for a note: the exact MIDI note when the note carries one (a song's), else the step of the scale. A note whose pitch no sample can reach is simply not played.</summary>
        bool TryResolveSound(in NoteIntent intent, out SampleResolution resolution) =>
            intent.HasMidi ? _mapper.TryResolveMidi(intent.Part, intent.Midi, out resolution) : _mapper.TryResolve(intent.Part, intent.PitchStep, out resolution);

        void Handle(NoteIntent intent)
        {
            if (!TryResolveSound(intent, out var resolution)) return;
            var audioNow = _audio.Now;
            var requestedAt = double.IsNaN(intent.RequestedAt) ? audioNow : intent.RequestedAt;
            if (!_scheduler.TrySchedule(requestedAt, intent, out var note)) return;

            var instrument = _mapper.InstrumentFor(intent.Part);
            var gain = VelocityMapper.ToGain(intent.Velocity) * Mathf.Pow(10f, instrument.gainDb / DbToLinearDivisor);
            // start the sample early by its attack so that the perceived onset (not the file start) lands on the grid slot
            var wanted = note.AudioTime - resolution.OnsetOffsetSec;
            var actual = _audio.Schedule(resolution.SampleIndex, wanted, resolution.PlaybackRate, gain);
            if (actual > wanted + 1e-6) note = note.WithAudioTime(actual + resolution.OnsetOffsetSec); // too late: follow the real start

            _queue.Enqueue(note);
            _meadow.OnNote(note, _audio.VisualNow);

            _record?.OnNote(note);
            _songs?.OnNote();   // the same notes as the recorder counts, so that a visit that makes a card is a visit that used up its song
            _energy?.AddNote(intent.Velocity, intent.EnergyWeight);
            _handNotes++;
            _noteThisFrame = true;
            if (!intent.Timing.IsValid) _inputNoteThisFrame = true; // a note without hand timing came from the mouse or keyboard
            NoteScheduled?.Invoke(note, resolution);
        }

        void WriteBloomSync(string path)
        {
            if (_meadow.Flowers == null) return;
            var report = BloomSyncReport.From(_meadow.Flowers.HalfOpenLagSec, _frameTimes);
            report.note = "Mac player, window; lag = (audio time of the frame where the bloom first reached 0.5) - (time the note is heard); the bloom starts bloom.leadSec before the note is heard";
            report.Save(path);
            Debug.Log($"[bloom-sync] {report.flowersOpened} flowers, median |lag| {report.medianAbsLagMs} ms = {report.medianAbsLagFrames} frames");
        }

        void Fail(string message, bool audioProblem = false)
        {
            _state = State.Failed;
            _audioFailed |= audioProblem;
            FailureMessage = message;
            Debug.LogError("[Downbeat] " + message);
        }
    }
}
