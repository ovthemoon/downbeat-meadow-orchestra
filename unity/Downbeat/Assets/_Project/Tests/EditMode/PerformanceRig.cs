using System;
using System.Collections.Generic;
using Downbeat.Core;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The pieces of the app that decide what a hand does, wired the way <c>DownbeatApp.Tick</c> and <c>TickNature</c> wire them, driven by hands that are posed over time or by a recording, with a fake sound:
    /// tracker pictures (at 30 or 6 a second) -> pose and <see cref="HandMotion.CanPlay"/> -> conducting engine, cutoff director, presence, the visit's state machine, the energy, and the follow-along tutorial
    /// (<see cref="TutorialHandSignals"/> and <see cref="TutorialDirector"/>). The real scene is in the PlayMode tests.
    /// </summary>
    sealed class PerformanceRig
    {
        public const double FrameSec = 1.0 / 60.0;

        readonly ConductRig _conduct = new ConductRig();
        readonly ConductPlayer _player;
        readonly HandPresenceMonitor _presence = new HandPresenceMonitor(0.5, 2.0);
        readonly List<NoteIntent> _frameNotes = new List<NoteIntent>();
        readonly TutorialHandSignals _tutorialSignals;
        readonly TutorialDirector _tutorial;
        readonly double _origin = ConductRig.GridOrigin - 1.0;
        long _seq = 1;
        double _next;
        double _elapsed;

        readonly ContentDto _content;
        SessionRecorder _recorder;

        /// <param name="playMode"><c>--play-mode</c> ("conduct", "strike") or null for what the content says.</param>
        public PerformanceRig(string playMode = null)
        {
            var content = TestContent.LoadShipped();
            _content = content;
            Audio = new FakeAudioEngine();
            Energy = new PerformanceEnergy(NatureSettingsFactory.Energy(content.nature.energy));
            Settings = CutoffSettings.From(content.cutoff);
            Experience = new ExperienceStateMachine(NatureSettingsFactory.Experience(content.experience).WithRestartLockSec(Settings.RestartLockSec));
            _player = new ConductPlayer(() => Audio.Now);
            _player.Configure(content, playMode);
            _player.OnGridReady(_conduct.Grid);
            _player.SetPlayGate(() => !Experience.AwaitsOpenHand);   // as DownbeatApp.BuildNature: after a cutoff nothing plays until an open hand starts the next visit
            Director = new CutoffDirector(Settings, Experience, Audio, Energy, _player.Reset, HandCellTracker.HoldSecOf(_player.Settings, _conduct.Hand.Settings));
            TutorialSettings = TutorialSettings.From(content.tutorial);
            _tutorialSignals = new TutorialHandSignals(HandCellTracker.From(content));
            _tutorial = new TutorialDirector(TutorialSettings);
            Experience.SessionStarted += t => Started.Add(t - _origin);
            Experience.SessionEnded += t => Ended.Add(t - _origin);
            Director.Cut += t =>
            {
                Cuts.Add(t - _origin);
                CutAudioTimes.Add(t);
            };
        }

        public FakeAudioEngine Audio { get; }
        public PerformanceEnergy Energy { get; }
        public CutoffSettings Settings { get; }
        public ExperienceStateMachine Experience { get; }
        public CutoffDirector Director { get; }
        public ConductPlayer Player => _player;
        /// <summary>The song session, once <see cref="UseSongs"/> made one (the app's songs are off in the rig until a test asks for them).</summary>
        public SongSession Songs { get; private set; }
        public TutorialSettings TutorialSettings { get; }
        public HandMotionTracker Motion => _conduct.Motion;
        /// <summary>Where the pose thresholds come from (give it to the <see cref="ReplayPlayer"/> of a recording).</summary>
        public HandSettingsHolder Hand => _conduct.Hand;
        /// <summary>What the tutorial showed in the latest frame.</summary>
        public TutorialFrame Tutorial { get; private set; }
        public readonly List<double> Started = new List<double>();
        public readonly List<double> Ended = new List<double>();
        public readonly List<double> Cuts = new List<double>();
        /// <summary>The audio time of each end of the piece, as the director was told it (<see cref="Cuts"/> is the same relative to the start of the run).</summary>
        public readonly List<double> CutAudioTimes = new List<double>();
        /// <summary>When (seconds into the run) each note that was played was made.</summary>
        public readonly List<double> NoteTimes = new List<double>();
        /// <summary>The notes that were played (those of a frame in which the piece was ended are not).</summary>
        public readonly List<NoteIntent> Played = new List<NoteIntent>();
        /// <summary>Each song that began (the song and the audio time of its bar 1), in order.</summary>
        public readonly List<(SongScore song, double audioTime)> SongStarts = new List<(SongScore, double)>();
        /// <summary>The cards of the visits that made one, once <see cref="UseRecorder"/> was called.</summary>
        public readonly List<SessionCard> Cards = new List<SessionCard>();

        public double Elapsed => _elapsed;

        /// <summary>
        /// Wires a <see cref="SongSession"/> the way the app does (the visit's start and end, every note that is played, a tick per frame after the cutoff director looked at the hands) with the songs of the
        /// shipped content unless <paramref name="songs"/> are given.
        /// </summary>
        public SongSession UseSongs(bool enabled = true, IReadOnlyList<SongScore> songs = null)
        {
            var scale = new ScaleSpec(_content.song.scaleSteps, _content.song.pitchSteps);
            Songs = new SongSession(songs ?? SongBook.Build(_content).Songs, _player, Director, scale, _content.song.rootMidi, _content.record.minNotes, enabled);
            Songs.SongStarted += (song, barOneAt) => SongStarts.Add((song, barOneAt));
            Experience.SessionStarted += Songs.OnSessionStarted;
            Experience.SessionEnded += Songs.OnSessionEnded;
            return Songs;
        }

        /// <summary>Wires the recorder of the cards: a visit with enough notes ends in a card in <see cref="Cards"/>, and the card names the song <see cref="Songs"/> says the visit was played to.</summary>
        public void UseRecorder()
        {
            _recorder = new SessionRecorder(_content.record.minNotes, _content.record.cardMaxMarks);
            _recorder.SetSongSource(() => Songs?.VisitLabel ?? default);
            var day = new DateTime(2026, 11, 1, 12, 0, 0);
            Experience.SessionStarted += t => _recorder.OnSessionStarted(t, day.AddSeconds(t - _origin));
            Experience.SessionEnded += t =>
            {
                var card = _recorder.OnSessionEnded(t);
                if (card != null) Cards.Add(card);
            };
        }

        /// <summary>Runs for this long (60 frames a second). <paramref name="hands"/> says which hands are posed where at a time (seconds into the run); the tracker's pictures come <paramref name="fps"/> a second.</summary>
        public void Run(double seconds, int fps, Func<double, PosedHand[]> hands)
        {
            var end = _elapsed + seconds;
            while (_elapsed < end - 1e-9)
            {
                _elapsed += FrameSec;
                while (_next <= _elapsed + 1e-9)
                {
                    _conduct.Motion.Update(PosedFrame(_seq++, _next * 1000.0, _conduct.Hand.Pose, hands(_next)));
                    _next += 1.0 / fps;
                }
                Frame(_origin + _elapsed);
            }
        }

        /// <summary>
        /// Runs a recording as the camera (60 frames a second) until <see cref="Elapsed"/> reaches <paramref name="untilSec"/>: the recording's clock is the clock of the rig, so a run can be done in steps (look at the
        /// state, go on). Use it instead of <see cref="Run"/>, not after it.
        /// </summary>
        public void RunReplayUntil(ReplayPlayer player, double untilSec)
        {
            var frames = new List<HandFrame>();
            while (_elapsed < untilSec - 1e-9)
            {
                _elapsed += FrameSec;
                frames.Clear();
                player.Advance(_elapsed * 1000.0, frames);
                foreach (var frame in frames) _conduct.Motion.Update(frame);
                Frame(_origin + _elapsed);
            }
        }

        /// <summary>Runs until <paramref name="condition"/> holds (at most <paramref name="maxSeconds"/>); returns whether it did.</summary>
        public bool RunUntil(int fps, Func<double, PosedHand[]> hands, Func<bool> condition, double maxSeconds)
        {
            var end = _elapsed + maxSeconds;
            while (_elapsed < end && !condition()) Run(FrameSec, fps, hands);
            return condition();
        }

        void Frame(double now)
        {
            Audio.Now = now;
            // DownbeatApp.Tick: the hand input conducts, the cutoff looks at the hands, the notes of a frame that ended the piece are not played
            _frameNotes.Clear();
            _player.Update(_conduct.Motion, _conduct.Motion.Aspect, _frameNotes);
            var cut = Director.Update(now, 0, _conduct.Motion, _conduct.Motion.Aspect);
            if (Songs != null && Songs.Tick(now)) cut = true;   // after the cutoff director: when both end the piece in one frame the fists were first
            var note = false;
            if (!cut)
            {
                foreach (var n in _frameNotes)
                {
                    Energy.AddNote(n.Velocity, n.EnergyWeight);
                    NoteTimes.Add(_elapsed);
                    Played.Add(n);
                    Songs?.OnNote();
                    _recorder?.OnNote(new ScheduledNote(0, now, now, n.Part, n.PitchStep, n.Velocity, false));
                    note = true;
                }
            }
            // DownbeatApp.TickNature: the energy fades, who is there, the visit
            Energy.Advance(FrameSec);
            HandPresenceMonitor.CountPlayers(_conduct.Motion, out var playing, out var open);
            var has = _presence.Update(now, TrackerState.Running, _conduct.Motion.FrameSeq, playing, open, false, out var openPresent);
            Experience.Update(now, has, note, Energy.Value, openPresent);
            // DownbeatApp.TickTutorial: it only watches
            _tutorialSignals.Read(now, _conduct.Motion, false);
            Tutorial = _tutorial.Update(now, Experience.State, Experience.StateSince, _tutorialSignals.OpenHandVisible, _tutorialSignals.CellChanged, _tutorialSignals.BothFists);
        }
    }
}
