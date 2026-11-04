using System.Collections.Generic;

namespace Downbeat.Core
{
    public readonly struct RectF
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public RectF(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public bool Contains(float px, float py) => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    }

    /// <summary>Where the stage view is on the screen, in pixels with the origin at the bottom left (Unity screen space).</summary>
    public interface IViewLayout
    {
        RectF StagePixels { get; }
    }

    /// <summary>Source of note intents: mouse and keyboard today, hand tracking and replay later.</summary>
    public interface IPlayInput
    {
        void Tick(double unscaledTimeSec);
        bool TryDequeue(out NoteIntent intent);
    }

    /// <summary>An input that needs to know when the beat grid exists (a replay starts on a bar line of the grid).</summary>
    public interface IGridAwareInput
    {
        void OnGridReady(in BeatGrid grid, int slotsPerBar);
    }

    /// <summary>
    /// Turns hand motion into discrete strikes (notes). The strike-to-note rule is a placeholder that can be replaced: a design without
    /// notes registers an implementation that never produces a strike and reads <see cref="IHandMotionView"/> instead.
    /// </summary>
    public interface IHandInterpreter
    {
        /// <summary>Called once when the pitch ladder and tuning are known (content loaded).</summary>
        void Configure(in HandInterpreterSettings settings, int pitchSteps);
        void Reset();
        /// <summary><paramref name="motion"/> has already been updated with <paramref name="frame"/>. Decided strikes are appended to <paramref name="output"/>.</summary>
        void Process(in HandFrame frame, IHandMotionView motion, System.Collections.Generic.List<HandStrike> output);
    }

    /// <summary>Where hand frames come from: the page tracker (live camera) or a recording.</summary>
    public interface IHandSource
    {
        /// <summary>State as of the last <see cref="Poll"/>.</summary>
        TrackerState State { get; }
        /// <summary>Increases whenever the stream restarts from the beginning (a looping replay). Time-dependent state must be reset then.</summary>
        int Generation { get; }
        /// <summary>Appends the frames that arrived since the last call, oldest first.</summary>
        void Poll(System.Collections.Generic.List<HandFrame> frames);
        /// <summary>Audio-clock time at which a frame captured at <paramref name="captureMs"/> would have been requested; NaN for a live camera ("now").</summary>
        double RequestTimeFor(double captureMs);
    }

    public readonly struct SampleLoadRequest
    {
        public int Index { get; }
        public string Url { get; }
        /// <summary>A sample of the nature layers (they are scheduled by the score, not by a player): <see cref="IAudioEngine.FadeOutAll"/> leaves the voices that play it alone.</summary>
        public bool IsNature { get; }

        public SampleLoadRequest(int index, string url, bool isNature = false)
        {
            Index = index;
            Url = url;
            IsNature = isNature;
        }
    }

    public readonly struct AudioStats
    {
        public int Scheduled { get; }
        public int Late { get; }
        public int Stolen { get; }
        public int MissingSample { get; }

        public AudioStats(int scheduled, int late, int stolen, int missingSample)
        {
            Scheduled = scheduled; Late = late; Stolen = stolen; MissingSample = missingSample;
        }
    }

    /// <summary>
    /// The sound side. Implementations own the audio clock; Unity only reads it. Times are audio-clock seconds.
    /// </summary>
    public interface IAudioEngine
    {
        /// <summary>The audio clock has started moving and every sample finished loading.</summary>
        bool IsRunning { get; }
        bool SamplesReady { get; }
        int SamplesFailed { get; }
        double Now { get; }
        /// <summary>Audio time of the sample that is leaving the speakers right now (output latency included).</summary>
        double HeardNow { get; }
        /// <summary><see cref="HeardNow"/> for the eyes: the audio clock moves once per audio block (21 ms), so this one follows the wall clock between its steps (see SmoothClock). Visuals use it; scheduling never does.</summary>
        double VisualNow { get; }

        void Configure(float masterGainLinear, int maxVoices);
        void LoadSamples(IReadOnlyList<SampleLoadRequest> samples);
        /// <summary>Call once per frame; refreshes Now / HeardNow and polls loading.</summary>
        void Tick();
        /// <summary>Plays a sample at an audio time. Returns the audio time it will actually start (later than asked if the request was late).</summary>
        double Schedule(int sampleIndex, double when, double playbackRate, double gain);
        /// <summary>
        /// Brings every voice of the players down to silence over <paramref name="seconds"/>: what sounds now fades, what is scheduled and has not started is cancelled. Voices of samples loaded
        /// with <see cref="SampleLoadRequest.IsNature"/> are not touched. Notes scheduled afterwards play as usual.
        /// </summary>
        void FadeOutAll(double seconds);
        /// <summary>Measurement hook: the visual for a note was released at <paramref name="heardNow"/>.</summary>
        void LogFlash(double when, double heardNow);
        AudioStats Stats { get; }
    }
}

namespace Downbeat.Core
{
    /// <summary>How many pitch steps the loaded content offers. Set once content is loaded; inputs read it.</summary>
    public sealed class PlayableRange
    {
        public int PitchSteps { get; private set; } = 1;

        public void Set(int pitchSteps)
        {
            if (pitchSteps < 1) throw new System.ArgumentOutOfRangeException(nameof(pitchSteps));
            PitchSteps = pitchSteps;
        }
    }
}

namespace Downbeat.Core
{
    /// <summary>How the hand tracker is reached (hand.udpPort, hand.trackerTimeoutSec, hand.mirrorX of content.json).</summary>
    public readonly struct HandLinkSettings
    {
        public const int DefaultPort = 47800;
        public const double DefaultTimeoutSec = 2.0;

        public int UdpPort { get; }
        public double TrackerTimeoutSec { get; }
        public bool MirrorX { get; }

        public HandLinkSettings(int udpPort, double trackerTimeoutSec, bool mirrorX)
        {
            UdpPort = udpPort; TrackerTimeoutSec = trackerTimeoutSec; MirrorX = mirrorX;
        }

        public static HandLinkSettings Default => new HandLinkSettings(DefaultPort, DefaultTimeoutSec, true);
    }

    /// <summary>Hand tuning of the loaded content. Set once content is loaded; the hand input reads it when it starts.</summary>
    public sealed class HandSettingsHolder
    {
        public HandInterpreterSettings Settings { get; private set; } = HandInterpreterSettings.Default;
        public HandLinkSettings Link { get; private set; } = HandLinkSettings.Default;
        /// <summary>How an open hand and a fist are told apart (the <c>pose</c> block). The sources read it for every picture, the motion tracker reads <see cref="PoseSettings.ConfirmFrames"/>.</summary>
        public PoseSettings Pose { get; private set; } = PoseSettings.Default;

        public void Set(HandSettingsDto dto, PoseDto pose = null, CellsDto cells = null)
        {
            Pose = PoseSettings.From(pose);
            Settings = new HandInterpreterSettings(dto, CellSettings.From(cells));
            Link = dto == null ? HandLinkSettings.Default : new HandLinkSettings(dto.udpPort, dto.trackerTimeoutSec, dto.mirrorX);
        }
    }
}
