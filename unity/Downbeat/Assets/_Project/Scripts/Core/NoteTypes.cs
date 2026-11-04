using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The six instruments, one for each cell of the hand's place in the picture: three columns (left to right) in two rows (the upper one is the sky, the lower one the ground).
    /// The value is <c>row * 3 + column</c>, so the upper row is 0..2 and the lower row 3..5.
    /// </summary>
    public enum Part
    {
        Harp = 0,
        Recorder = 1,
        Bell = 2,
        LowString = 3,
        Marimba = 4,
        HandDrum = 5,
    }

    public static class Parts
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int Count = Columns * Rows;

        public static int Row(Part part) => (int)part / Columns;

        public static int Column(Part part) => (int)part % Columns;

        public static Part At(int row, int column)
        {
            if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row), row, "0 (the sky) or 1 (the ground)");
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column), column, "0..2");
            return (Part)(row * Columns + column);
        }

        /// <summary>The upper row: its notes are effects in the air, they open no flowers and do not push the grass.</summary>
        public static bool IsSky(Part part) => Row(part) == 0;

        /// <summary>The names of the parts as content.json and the cards write them; the names of the three parts before the six cells are read too (old cards), and mean the part that took their place.</summary>
        public static bool TryParse(string value, out Part part)
        {
            switch (value)
            {
                case "harp": case "strings": part = Part.Harp; return true;
                case "recorder": case "winds": part = Part.Recorder; return true;
                case "bell": part = Part.Bell; return true;
                case "lowstring": part = Part.LowString; return true;
                case "marimba": case "perc": part = Part.Marimba; return true;
                case "handdrum": part = Part.HandDrum; return true;
                default: part = Part.Harp; return false;
            }
        }

        public static string Name(Part part)
        {
            switch (part)
            {
                case Part.Harp: return "harp";
                case Part.Recorder: return "recorder";
                case Part.Bell: return "bell";
                case Part.LowString: return "lowstring";
                case Part.Marimba: return "marimba";
                case Part.HandDrum: return "handdrum";
                default: throw new ArgumentOutOfRangeException(nameof(part), part, "not one of the six parts");
            }
        }
    }

    /// <summary>Where a hand note came from, for latency measurement. All times are ms on the page clock (performance.now()).</summary>
    public readonly struct HandTiming
    {
        public bool IsValid { get; }
        /// <summary>When the camera frame that completed the decision was captured.</summary>
        public double CaptureMs { get; }
        public double InferEndMs { get; }
        /// <summary>When Unity read that frame from the page.</summary>
        public double ReceivedMs { get; }
        public long FrameSeq { get; }
        /// <summary>Capture time of the frame where the strike was first detected (NaN when not applicable). The decision frame comes later: the strike waits for its speed peak.</summary>
        public double TriggerCaptureMs { get; }

        public HandTiming(long frameSeq, double captureMs, double inferEndMs, double receivedMs, double triggerCaptureMs = double.NaN)
        {
            IsValid = true;
            FrameSeq = frameSeq; CaptureMs = captureMs; InferEndMs = inferEndMs; ReceivedMs = receivedMs; TriggerCaptureMs = triggerCaptureMs;
        }

        public HandTiming WithTrigger(double triggerCaptureMs) => new HandTiming(FrameSeq, CaptureMs, InferEndMs, ReceivedMs, triggerCaptureMs);
    }

    /// <summary>
    /// What an input wants to play: which part, which step of the scale, how hard. Time is decided by the scheduler
    /// (at the audio clock "now", unless the input is a replay that supplies the moment the recording would have asked).
    /// </summary>
    public readonly struct NoteIntent
    {
        public Part Part { get; }
        public int PitchStep { get; }
        public float Velocity { get; }
        public HandTiming Timing { get; }
        /// <summary>Audio-clock time at which this note is requested; NaN means "now".</summary>
        public double RequestedAt { get; }
        /// <summary>Where across the picture the note was played, 0 left .. 1 right; NaN when the input does not know (the flowers then use the middle of the instrument's zone).</summary>
        public float X01 { get; }
        /// <summary>How much of a normal note's energy this note adds to the performance energy: 1 for a strike or a click; a conducted note of a hand held still counts for less.</summary>
        public float EnergyWeight { get; }
        /// <summary>Where up and down the picture the note was played, 0 top .. 1 bottom (the y of the hand; a mouse click uses the screen's y); NaN when the input does not know.</summary>
        public float Y01 { get; }
        /// <summary>The exact MIDI note this note must sound (a song's note), or -1 when the input has only a step of the scale to offer. The step still decides how the note looks; the MIDI decides what it sounds like.</summary>
        public int Midi { get; }
        /// <summary>True when <see cref="Midi"/> names a note to sound.</summary>
        public bool HasMidi => Midi >= 0;

        public NoteIntent(Part part, int pitchStep, float velocity, HandTiming timing = default, double requestedAt = double.NaN, float x01 = float.NaN, float energyWeight = 1f, float y01 = float.NaN, int midi = -1)
        {
            if (pitchStep < 0) throw new ArgumentOutOfRangeException(nameof(pitchStep));
            EnergyWeight = float.IsNaN(energyWeight) || energyWeight < 0f ? 0f : energyWeight;
            Part = part;
            PitchStep = pitchStep;
            Velocity = velocity < 0f ? 0f : (velocity > 1f ? 1f : velocity);
            Timing = timing;
            RequestedAt = requestedAt;
            X01 = float.IsNaN(x01) ? float.NaN : (x01 < 0f ? 0f : (x01 > 1f ? 1f : x01));
            Y01 = float.IsNaN(y01) ? float.NaN : (y01 < 0f ? 0f : (y01 > 1f ? 1f : y01));
            Midi = midi < 0 ? -1 : midi;
        }
    }

    /// <summary>A note that has been placed on the beat grid.</summary>
    public readonly struct ScheduledNote
    {
        public long Slot { get; }
        /// <summary>Grid time of the slot in audio-clock seconds. The visual flash and the perceived onset are aligned to this.</summary>
        public double AudioTime { get; }
        public double RequestedAt { get; }
        public Part Part { get; }
        public int PitchStep { get; }
        public float Velocity { get; }
        public bool WasPushed { get; }
        /// <summary>Where across the picture the note was played (see <see cref="NoteIntent.X01"/>).</summary>
        public float X01 { get; }
        /// <summary>Where up and down the picture the note was played (see <see cref="NoteIntent.Y01"/>).</summary>
        public float Y01 { get; }
        /// <summary>The exact MIDI note to sound, or -1 when the note sounds by its step of the scale (see <see cref="NoteIntent.Midi"/>).</summary>
        public int Midi { get; }

        public ScheduledNote(long slot, double audioTime, double requestedAt, Part part, int pitchStep, float velocity, bool wasPushed, float x01 = float.NaN, float y01 = float.NaN, int midi = -1)
        {
            Midi = midi < 0 ? -1 : midi;
            X01 = x01;
            Y01 = y01;
            Slot = slot;
            AudioTime = audioTime;
            RequestedAt = requestedAt;
            Part = part;
            PitchStep = pitchStep;
            Velocity = velocity;
            WasPushed = wasPushed;
        }

        public ScheduledNote WithAudioTime(double audioTime) =>
            new ScheduledNote(Slot, audioTime, RequestedAt, Part, PitchStep, Velocity, WasPushed, X01, Y01, Midi);
    }

    /// <summary>Which sample to play and how: output of <see cref="PitchMapper"/>.</summary>
    public readonly struct SampleResolution
    {
        public int SampleIndex { get; }
        public string SampleId { get; }
        public int TargetMidi { get; }
        public int Semitones { get; }
        public double PlaybackRate { get; }
        /// <summary>Seconds from the start of the (possibly pitch-shifted) sample to where it is perceived to begin (half of its peak).</summary>
        public double OnsetOffsetSec { get; }

        public SampleResolution(int sampleIndex, string sampleId, int targetMidi, int semitones, double playbackRate, double onsetOffsetSec)
        {
            SampleIndex = sampleIndex;
            SampleId = sampleId;
            TargetMidi = targetMidi;
            Semitones = semitones;
            PlaybackRate = playbackRate;
            OnsetOffsetSec = onsetOffsetSec;
        }
    }
}
