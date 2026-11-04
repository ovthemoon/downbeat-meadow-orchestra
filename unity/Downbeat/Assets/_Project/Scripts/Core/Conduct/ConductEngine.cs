using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Conducting play: a hand that is raised keeps the music going on the beat grid (docs/conducting-play.md). A hand is a voice; its side of the picture is its part (with the same
    /// dead band as strikes), its height is the strength of its notes, the amount it moves says how often it plays (every beat, every half beat, every quarter beat) and the notes
    /// are the steps of a short phrase of the part. It needs no strike and no quick frames: the movement is measured over a window of a few hundred milliseconds, so a tracker that
    /// delivers 5 to 10 pictures a second is enough. A hand that has gone fades out over <c>releaseSec</c>.
    /// Pure logic on the time and the hands it is given: the same hands at the same times give the same notes.
    /// </summary>
    public sealed class ConductEngine
    {
        const int HistoryCapacity = 32;
        /// <summary>A palm that wanders less than this from where it was last counted has not moved (the picture of a still hand jitters by a few thousandths of its height).</summary>
        const float DeadBandPalms = 0.05f;
        const double MotionSmoothSec = 0.15;
        const double FadeInSec = 0.3;
        /// <summary>The density goes down a step only when the movement is this much below the threshold it went up at, so a hand at a threshold does not flip.</summary>
        const float DensityHysteresis = 0.8f;
        const double MaxStepSec = 0.5;
        const float SilentLevel = 0.01f;
        const int MinSamplesForMotion = 2;
        /// <summary>After a pause of the app a long run of slots is not walked slot by slot: only the last bar or so matters for the phrase and the density.</summary>
        const int MaxSkippedBars = 4;
        /// <summary>The start of a song that has been prepared but not started: it lies beyond every slot, so the song counts as not begun.</summary>
        const long NoSongStart = long.MaxValue;

        sealed class HandTrack
        {
            public int Epoch = int.MinValue;
            public bool Active;
            public double LastSeenAudio = double.NegativeInfinity;
            public int Part = -1;
            public float X, Y, Size;
            /// <summary>The hand is a fist (as the motion tracker has confirmed it): it conducts nothing, and the part it was playing fades out quickly.</summary>
            public bool IsFist;
            /// <summary>The hand plays (<see cref="HandPlayRule"/>): not a fist, and not a hand that has only just come in.</summary>
            public bool CanPlay;

            public readonly double[] SampleT = new double[HistoryCapacity];
            public int SampleHead = -1;
            public int SampleCount;
            public readonly double[] SegmentT = new double[HistoryCapacity];
            public readonly float[] SegmentPalms = new float[HistoryCapacity];
            public int SegmentHead = -1;
            public int SegmentCount;
            public float AnchorX, AnchorY;
            public float Motion;

            public double LatestSampleMs => SampleCount == 0 ? double.NegativeInfinity : SampleT[SampleHead];

            public void ClearHistory()
            {
                SampleHead = -1;
                SampleCount = 0;
                SegmentHead = -1;
                SegmentCount = 0;
                Motion = 0f;
            }
        }

        sealed class Voice
        {
            public bool Present;
            public float TargetVelocity;
            public float X = float.NaN;
            public float Y = float.NaN;
            public float Motion;
            public float Level;
            public int Density;
            public long LastSlot = long.MinValue;
            public int PhraseIndex;
            /// <summary>A hand crossed out of this part into the next one: it fades out quickly, not over the release of a hand that has gone.</summary>
            public bool Crossfade;
        }

        readonly HandTrack[] _hands = new HandTrack[HandBufferLayout.MaxHands];
        readonly Voice[] _voices = new Voice[Parts.Count];
        readonly int[] _minGapSlots = new int[Parts.Count];

        ConductSettings _settings = ConductSettings.Default;
        bool _configured;
        bool _gridReady;
        BeatGrid _grid;
        int _slotsPerBeat = 4;
        int _slotsPerBar = 16;
        double _leadSec = 0.1;
        double _holdSec = 0.8;
        float _columnMarginBands = 0.09f;
        CellSettings _cells = CellSettings.Default;
        long _lastScannedSlot = long.MinValue;
        double _lastUpdateAudio = double.NaN;
        double _lastCaptureMs;
        SongScore _song;
        SongPitch _songPitch;
        long _songStartSlot = NoSongStart;

        public ConductEngine()
        {
            Reset();
        }

        public bool IsConfigured => _configured;

        /// <summary>The newest slot the engine has looked at for notes (also the slots it skipped over), or <see cref="long.MinValue"/> when it has looked at none since the grid was set or the engine reset.</summary>
        public long LastScannedSlot => _lastScannedSlot;

        /// <summary>A song is prepared: the upper row plays its melody and the lower row its chords instead of the free phrases.</summary>
        public bool HasSong => _song != null;

        /// <summary>The prepared song has a start slot.</summary>
        public bool IsSongStarted => _song != null && _songStartSlot != NoSongStart;

        /// <summary>The bar line at which the song is over (its start plus its bars); <see cref="long.MaxValue"/> while the song has not been started or there is none.</summary>
        public long SongEndSlot => IsSongStarted ? _songStartSlot + (long)_song.BarCount * _slotsPerBar : long.MaxValue;

        /// <summary>Time on the audio clock of a slot of the beat grid the engine plays on.</summary>
        public double SlotTime(long slot) => _grid.SlotTime(slot);

        /// <summary>
        /// The first bar line that lies strictly after the slot the engine has already looked at and after the slot it looks at for this moment (a note is made a little ahead of its slot), so
        /// that a song started on it never has a slot behind it that was scanned without the song.
        /// </summary>
        public long FirstBarLineAfter(double audioNowSec)
        {
            if (!_gridReady) throw new InvalidOperationException("the beat grid is not ready");
            var scanned = Math.Max(_lastScannedSlot, _grid.SlotForTrigger(audioNowSec, _leadSec));
            return (FloorDiv(scanned, _slotsPerBar) + 1) * _slotsPerBar;
        }

        /// <summary>
        /// Takes the song to play from now on (it replaces a song already prepared). The song counts as not begun until <see cref="StartSong"/>: the upper row is silent then and the lower row
        /// plays the chord of the first bar. <paramref name="pitch"/> gives the notes of the song the scale step the instruments show them with.
        /// </summary>
        public void PrepareSong(SongScore score, SongPitch pitch)
        {
            _song = score ?? throw new ArgumentNullException(nameof(score));
            _songPitch = pitch ?? throw new ArgumentNullException(nameof(pitch));
            _songStartSlot = NoSongStart;
        }

        /// <summary>Bar 1 of the prepared song is the bar that begins at this slot, which has to be a bar line of the grid.</summary>
        public void StartSong(long startSlot)
        {
            if (_song == null) throw new InvalidOperationException("no song is prepared");
            if (startSlot < 0 || Mod(startSlot, _slotsPerBar) != 0) throw new ArgumentOutOfRangeException(nameof(startSlot), startSlot, "a song starts on a bar line of the grid");
            _songStartSlot = startSlot;
        }

        /// <summary>Back to free play: the phrases of the content, and the density of the hand for the upper row too.</summary>
        public void ClearSong()
        {
            _song = null;
            _songPitch = null;
            _songStartSlot = NoSongStart;
        }

        /// <param name="minGapSlotsByPart">The fewest slots between two notes of a part (<c>song.partMinGapSlots</c>); the scheduler keeps the same rule.</param>
        /// <param name="leadSec"><c>audio.minLeadSec</c>: the scheduler puts a note on the first slot at least this far ahead, and a conducted note names the slot it wants by asking this much earlier.</param>
        /// <param name="partHysteresis"><c>hand.partHysteresis</c>, as a fraction of the picture's width (the dead band around the edges of the columns).</param>
        /// <param name="maxGapMs"><c>hand.maxGapMs</c>: a hand that is missing for longer starts over.</param>
        /// <param name="cells">The rows (where they meet, the dead band between them); null for the defaults.</param>
        public void Configure(ConductSettings settings, int slotsPerBeat, int slotsPerBar, int[] minGapSlotsByPart, double leadSec, float partHysteresis, double maxGapMs, CellSettings cells = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (slotsPerBeat < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerBeat));
            if (slotsPerBar < slotsPerBeat) throw new ArgumentOutOfRangeException(nameof(slotsPerBar));
            if (minGapSlotsByPart == null || minGapSlotsByPart.Length != Parts.Count) throw new ArgumentException("one gap per part", nameof(minGapSlotsByPart));
            _slotsPerBeat = slotsPerBeat;
            _slotsPerBar = slotsPerBar;
            for (var i = 0; i < _minGapSlots.Length; i++) _minGapSlots[i] = Math.Max(1, minGapSlotsByPart[i]);
            _leadSec = leadSec;
            _columnMarginBands = CellPicker.ColumnMarginFor(partHysteresis);
            _cells = cells ?? CellSettings.Default;
            _holdSec = Math.Max(settings.HoldMs, maxGapMs) / 1000.0;
            _configured = true;
        }

        public void OnGridReady(in BeatGrid grid)
        {
            _grid = grid;
            _gridReady = true;
            _lastScannedSlot = long.MinValue;
            _songStartSlot = NoSongStart; // the slots of a song belong to the grid it was started on
        }

        /// <summary>Forget the hands and the voices (the recording started again, or the tracker was restarted). The grid, the settings and the song (prepared, and started if it was) stay.</summary>
        public void Reset()
        {
            for (var i = 0; i < _hands.Length; i++) _hands[i] = new HandTrack();
            for (var i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
            _lastScannedSlot = long.MinValue;
            _lastUpdateAudio = double.NaN;
            _lastCaptureMs = 0.0;
        }

        /// <summary>
        /// Call once per frame. Appends the notes that are due for the next slot the scheduler can still reach; each carries <c>RequestedAt</c> so that the scheduler
        /// puts it on exactly that slot.
        /// </summary>
        /// <param name="audioNowSec">The audio clock.</param>
        /// <param name="aspect">Width / height of the tracker's picture.</param>
        public void Update(double audioNowSec, IHandMotionView hands, float aspect, List<NoteIntent> output)
        {
            if (!_configured || !_gridReady) return;
            var dt = double.IsNaN(_lastUpdateAudio) ? 0.0 : Math.Min(MaxStepSec, Math.Max(0.0, audioNowSec - _lastUpdateAudio));
            _lastUpdateAudio = audioNowSec;

            Observe(hands, aspect, audioNowSec);
            UpdateVoices(dt);

            var slot = _grid.SlotForTrigger(audioNowSec, _leadSec);
            if (slot <= _lastScannedSlot) return;
            if (_lastScannedSlot != long.MinValue && slot - _lastScannedSlot > 1) KeepBooks(_lastScannedSlot + 1, slot - 1, slot); // a long frame jumped over slots: no notes for those, but a bar line is still a bar line
            _lastScannedSlot = slot;
            if (slot < 0) return; // before the first bar line of the grid
            ScanSlot(slot, hands.FrameSeq, _lastCaptureMs, output);
        }

        /// <summary>
        /// Slots that a frame longer than a slot jumped over are not played (their time has gone), but what a bar line and a beat do is still done: the phrase starts again at a bar line, and the density
        /// follows the movement at a beat. Done once for the jump, and not for a beat that the slot being scanned is itself.
        /// </summary>
        void KeepBooks(long from, long to, long current)
        {
            from = Math.Max(from, to - MaxSkippedBars * _slotsPerBar + 1);
            var bar = false;
            var beat = false;
            for (var s = from; s <= to; s++)
            {
                if (s < 0) continue;
                if (Mod(s, _slotsPerBar) == 0) bar = true;
                if (Mod(s, _slotsPerBeat) == 0) beat = true;
            }
            var beatNow = Mod(current, _slotsPerBeat) == 0;
            foreach (var voice in _voices)
            {
                if (bar) voice.PhraseIndex = 0;
                if (beat && !beatNow) voice.Density = DensityFor(voice);
            }
        }

        // ---- the hands -------------------------------------------------------------------------------------------------------------------

        void Observe(IHandMotionView view, float aspect, double audioNow)
        {
            for (var id = 0; id < _hands.Length; id++)
            {
                var track = _hands[id];
                if (view.TryGetById(id, out var m) && m.IsTracked)
                {
                    if (m.Epoch != track.Epoch)
                    {
                        // a new hand, or one that came back after a long gap: it starts over. A hand that is back within the hold time is the same hand for the music (the tracker restarted its own
                        // history after 250 ms, but a hand that was waving is still waving): the movement it had stays
                        if (audioNow - track.LastSeenAudio > _holdSec) track.ClearHistory();
                        track.Epoch = m.Epoch;
                    }
                    if (m.CaptureMs > track.LatestSampleMs)
                    {
                        // a picture that has not been seen before: the view goes on reporting the last one until the next arrives, and that is not a sighting
                        AddSample(track, m, aspect);
                        track.X = m.X;
                        track.Y = m.Y;
                        track.Size = m.Size;
                        track.IsFist = m.Pose == HandPose.Fist;
                        track.CanPlay = HandPlayRule.Resolve(m, audioNow - track.LastSeenAudio <= _holdSec, track.CanPlay);
                        track.LastSeenAudio = audioNow;
                        _lastCaptureMs = m.CaptureMs;
                    }
                }
                var wasActive = track.Active;
                // a fist conducts nothing and is not a voice, nor is a hand that has only just come in (a hand that arrives as a fist never sounds); a half open hand, which the pose reading is unsure of, is a voice
                // once it has been there for the confirming pictures. The part a fist was playing fades out quickly, as when a hand crosses into the next part
                track.Active = audioNow - track.LastSeenAudio <= _holdSec && track.CanPlay;
                if (wasActive && !track.Active && track.IsFist && track.Part >= 0) _voices[track.Part].Crossfade = true;
                if (!track.Active) continue;
                var part = ConductPart(track);
                if (wasActive && track.Part >= 0 && part != track.Part) _voices[track.Part].Crossfade = true;   // the hand crossed into the next band: the part it leaves fades quickly, so that two instruments do not play on
                track.Part = part;
            }
        }

        int ConductPart(HandTrack track) => (int)CellPicker.Pick(track.X, track.Y, (Part)Math.Max(track.Part, 0), track.Part >= 0, _columnMarginBands, _cells);

        void AddSample(HandTrack track, in HandMotion hand, float aspect)
        {
            var t = hand.CaptureMs;
            var size = hand.Size > 0.01f ? hand.Size : 0.01f;
            var previous = track.LatestSampleMs;
            // a picture that comes after a gap longer than the window (a hand the tracker lost for a moment): the distance between the two places says nothing about how fast the hand was
            // going, so it is not counted; the movement the hand had before the gap stays until the next pictures show it again
            var afterGap = !double.IsNegativeInfinity(previous) && t - previous > _settings.MotionWindowMs;
            if (track.SampleCount == 0 || afterGap)
            {
                track.AnchorX = hand.X;
                track.AnchorY = hand.Y;
            }
            else
            {
                var dx = (hand.X - track.AnchorX) * aspect;
                var dy = hand.Y - track.AnchorY;
                var palms = (float)Math.Sqrt(dx * dx + dy * dy) / size;
                if (palms >= DeadBandPalms)
                {
                    track.SegmentHead = (track.SegmentHead + 1) % HistoryCapacity;
                    track.SegmentT[track.SegmentHead] = t;
                    track.SegmentPalms[track.SegmentHead] = palms;
                    if (track.SegmentCount < HistoryCapacity) track.SegmentCount++;
                    track.AnchorX = hand.X;
                    track.AnchorY = hand.Y;
                }
            }
            track.SampleHead = (track.SampleHead + 1) % HistoryCapacity;
            track.SampleT[track.SampleHead] = t;
            if (track.SampleCount < HistoryCapacity) track.SampleCount++;
            if (afterGap) return;

            var raw = MeasureMotion(track, t);
            var step = double.IsNegativeInfinity(previous) ? 0.0 : (t - previous) / 1000.0;
            var alpha = step <= 0.0 ? 1.0 : 1.0 - Math.Exp(-step / MotionSmoothSec);
            track.Motion += (float)((raw - track.Motion) * alpha);
        }

        /// <summary>Palm lengths per second that the palm travelled in the last <c>motionWindowMs</c> (at least over the last two pictures, however slowly they came).</summary>
        float MeasureMotion(HandTrack track, double nowMs)
        {
            if (track.SampleCount < 2) return 0f;
            var windowStart = nowMs - _settings.MotionWindowMs;
            var oldest = track.SampleHead;
            var inWindow = 0;
            for (var back = 0; back < track.SampleCount; back++)
            {
                var index = (track.SampleHead - back + HistoryCapacity) % HistoryCapacity;
                if (track.SampleT[index] < windowStart) break;
                oldest = index;
                inWindow++;
            }
            if (inWindow < MinSamplesForMotion)
            {
                oldest = (track.SampleHead - 1 + HistoryCapacity) % HistoryCapacity; // the picture before this one, however long ago
            }
            var spanMs = nowMs - track.SampleT[oldest];
            if (spanMs <= 0.0) return 0f;
            var sum = 0f;
            for (var back = 0; back < track.SegmentCount; back++)
            {
                var index = (track.SegmentHead - back + HistoryCapacity) % HistoryCapacity;
                if (track.SegmentT[index] <= track.SampleT[oldest]) break;
                sum += track.SegmentPalms[index];
            }
            return sum / (float)(spanMs / 1000.0);
        }

        // ---- the voices ------------------------------------------------------------------------------------------------------------------

        void UpdateVoices(double dtSec)
        {
            for (var part = 0; part < _voices.Length; part++)
            {
                var voice = _voices[part];
                var present = false;
                var strongest = -1f;
                var motion = 0f;
                var x = voice.X;
                var y = voice.Y;
                foreach (var track in _hands)
                {
                    if (!track.Active || track.Part != part) continue;
                    present = true;
                    var m = track.Motion < _settings.StillPalms ? 0f : track.Motion;
                    var velocity = _settings.VelocityForMotion(m);
                    if (velocity > strongest)
                    {
                        strongest = velocity;
                        x = track.X;
                        y = track.Y;
                    }
                    if (m > motion) motion = m;
                }

                if (present)
                {
                    voice.TargetVelocity = strongest;
                    voice.X = x;
                    voice.Y = y;
                    voice.Motion = motion;
                    // a voice that had fallen silent starts at once; one that is still fading grows back from where it is
                    voice.Level = voice.Level <= SilentLevel ? 1f : (float)Math.Min(1.0, voice.Level + dtSec / FadeInSec);
                    voice.Crossfade = false;
                }
                else
                {
                    var releaseSec = voice.Crossfade ? Math.Min(_settings.CrossfadeSec, _settings.ReleaseSec) : _settings.ReleaseSec;
                    voice.Level = (float)Math.Max(0.0, voice.Level - dtSec / releaseSec);
                    if (voice.Level <= 0f) voice.Crossfade = false;
                }
                voice.Present = present;
            }
        }

        void ScanSlot(long slot, long frameSeq, double captureMs, List<NoteIntent> output)
        {
            var inBeat = (int)Mod(slot, _slotsPerBeat);
            var isBeat = inBeat == 0;
            var isBar = Mod(slot, _slotsPerBar) == 0;
            for (var part = 0; part < _voices.Length; part++)
            {
                var voice = _voices[part];
                if (isBar) voice.PhraseIndex = 0; // every bar line starts the phrase again, so that phrases stay on the bars
                if (isBeat) voice.Density = DensityFor(voice);
                if (voice.Level <= SilentLevel) continue;
                var interval = Math.Max(IntervalOf(voice.Density), _minGapSlots[part]);
                var sings = _song != null && Parts.IsSky((Part)part);
                var midi = -1;
                var notesInBar = 0;
                // in a song the upper row plays where the score has a note and the density of the hand has no say; the lower row keeps its density
                var isDue = sings ? TryMelodyAt(slot, (Part)part, out midi, out notesInBar) : inBeat % interval == 0;
                if (!isDue) continue;
                if (voice.LastSlot != long.MinValue && slot - voice.LastSlot < _minGapSlots[part]) continue; // a slot is played once, a part keeps its rest
                output.Add(sings ? MakeMelodyNote(part, voice, slot, isBar, midi, notesInBar, interval, frameSeq, captureMs) : MakeNote(part, voice, slot, isBar, frameSeq, captureMs));
                voice.LastSlot = slot;
            }
        }

        NoteIntent MakeNote(int part, Voice voice, long slot, bool isBar, long frameSeq, double captureMs)
        {
            var step = NextStep((Part)part, voice, slot, out var midi);
            return Build(part, voice, slot, isBar, step, midi, ConductEnergy.Share(voice.Motion, _settings.StillPalms), frameSeq, captureMs);
        }

        /// <summary>
        /// A note of the melody. A hand that conducts would have made <c>slotsPerBar / interval</c> notes in this bar, and the notes of the score are fewer, so each of them carries the energy of
        /// those notes divided by the number of notes this bar has: the same movement fills the meadow at the same speed in a song as in free play.
        /// </summary>
        NoteIntent MakeMelodyNote(int part, Voice voice, long slot, bool isBar, int midi, int notesInBar, int interval, long frameSeq, double captureMs)
        {
            var conducting = ConductEnergy.Share(voice.Motion, _settings.StillPalms) * ((float)_slotsPerBar / interval);
            return Build(part, voice, slot, isBar, _songPitch.StepFor((Part)part, midi), midi, conducting / notesInBar, frameSeq, captureMs);
        }

        NoteIntent Build(int part, Voice voice, long slot, bool isBar, int step, int midi, float weight, long frameSeq, double captureMs)
        {
            var strength = voice.TargetVelocity + (isBar ? _settings.DownbeatAccent : 0f);
            var velocity = Math.Min(1f, strength) * voice.Level;
            var timing = new HandTiming(frameSeq, captureMs, captureMs, double.NaN);
            return new NoteIntent((Part)part, step, velocity, timing, _grid.SlotTime(slot) - _leadSec, voice.X, weight, voice.Y, midi);
        }

        /// <summary>
        /// The scale step of the next note of the phrase of a part. A part that has a song phrase reads it, while a song is prepared, as the number of a tone of the chord of the bar the slot is in
        /// (the first bar before the song starts, the last once it is over), and the note sounds at that MIDI; every other note is the step of the ordinary phrase (<paramref name="midi"/> is -1).
        /// </summary>
        int NextStep(Part part, Voice voice, long slot, out int midi)
        {
            var tones = _song == null ? null : _settings.SongPhrase(part);
            var index = voice.PhraseIndex++;
            if (tones == null)
            {
                midi = -1;
                var phrase = _settings.Phrase(part);
                return phrase[index % phrase.Count];
            }
            midi = BarAt(slot).ChordTone(tones[index % tones.Count]);
            return _songPitch.StepFor(part, midi);
        }

        /// <summary>The bar of the prepared song that a slot is played in: the first before the song starts, the last once it is over.</summary>
        SongBar BarAt(long slot)
        {
            if (slot < _songStartSlot) return _song.Bars[0];
            var index = (slot - _songStartSlot) / _slotsPerBar;
            return _song.Bars[(int)Math.Min(index, _song.BarCount - 1)];
        }

        /// <summary>The melody note of the started song on a slot, as the part sounds it (the octave of the song for that instrument added), and how many melody notes the bar has.</summary>
        bool TryMelodyAt(long slot, Part part, out int midi, out int notesInBar)
        {
            midi = -1;
            notesInBar = 0;
            if (!IsSongStarted || slot < _songStartSlot || slot >= SongEndSlot) return false;
            var offset = slot - _songStartSlot;
            var bar = _song.Bars[(int)(offset / _slotsPerBar)];
            if (!bar.TryMelodyAt((int)(offset % _slotsPerBar), out var scoreMidi)) return false;
            midi = scoreMidi + _song.OctaveFor(part);
            notesInBar = bar.Melody.Count;
            return true;
        }

        /// <summary>0 = a note every beat, 1 = every half beat, 2 = every quarter beat; a step up needs the threshold, a step down needs a little less.</summary>
        int DensityFor(Voice voice)
        {
            var m = voice.Motion;
            var density = voice.Density;
            if (density < 2 && m >= _settings.QuarterBeatPalms) density = 2;
            else if (density < 1 && m >= _settings.HalfBeatPalms) density = 1;
            if (density == 2 && m < _settings.QuarterBeatPalms * DensityHysteresis) density = m >= _settings.HalfBeatPalms * DensityHysteresis ? 1 : 0;
            else if (density == 1 && m < _settings.HalfBeatPalms * DensityHysteresis) density = 0;
            return density;
        }

        int IntervalOf(int density)
        {
            if (density >= 2) return Math.Max(1, _slotsPerBeat / 4);
            return density == 1 ? Math.Max(1, _slotsPerBeat / 2) : _slotsPerBeat;
        }

        static long Mod(long value, int modulus) => ((value % modulus) + modulus) % modulus;

        static long FloorDiv(long value, int divisor) => (value - Mod(value, divisor)) / divisor;
    }
}
