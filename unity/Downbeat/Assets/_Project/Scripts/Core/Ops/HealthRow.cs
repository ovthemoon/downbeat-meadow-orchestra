using System;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>One line of the health log: what happened in the last interval (a minute) and the state of the run at its end. <see cref="ToCsv"/> always uses the invariant culture.</summary>
    public readonly struct HealthRow
    {
        const double BytesPerMegabyte = 1024.0 * 1024.0;

        /// <summary>The columns, in the order of <see cref="ToCsv"/>.</summary>
        public static string CsvHeader =>
            "timestamp_utc,uptime_s,fps_avg,fps_min_1s,frame_ms_p95,frame_ms_max,frames_over_100ms,max_dsp_stall_s,mono_mb,alloc_mb,working_set_mb," +
            "tracker_fps,tracker_age_ms_p95,hands_present_ratio,notes_scheduled,notes_late,voices_stolen,nature_dropped,errors,experience_state,energy_level,sessions,audio_state,render_scale,quality_level,starve_ignored,quality_changes";

        readonly string _experienceState;
        readonly string _audioState;
        readonly float _renderScale;
        readonly int _qualityLevel;
        readonly bool _starveIgnored;
        readonly int _qualityChanges;

        internal HealthRow(DateTime timestampUtc, double uptimeSec, double fpsAverage, double fpsMin1s, double frameMsP95, double frameMsMax, int framesOver100Ms, double maxDspStallSec,
            double monoMb, double allocMb, double trackerFps, double trackerAgeMsP95, double handsPresentRatio, int notesScheduled, int notesLate, int voicesStolen, int errors)
        {
            TimestampUtc = timestampUtc;
            UptimeSec = uptimeSec;
            FpsAverage = fpsAverage;
            FpsMin1s = fpsMin1s;
            FrameMsP95 = frameMsP95;
            FrameMsMax = frameMsMax;
            FramesOver100Ms = framesOver100Ms;
            MaxDspStallSec = maxDspStallSec;
            MonoMb = monoMb;
            AllocMb = allocMb;
            TrackerFps = trackerFps;
            TrackerAgeMsP95 = trackerAgeMsP95;
            HandsPresentRatio = handsPresentRatio;
            NotesScheduled = notesScheduled;
            NotesLate = notesLate;
            VoicesStolen = voicesStolen;
            Errors = errors;
            WorkingSetMb = 0.0;
            NatureDropped = 0;
            _experienceState = string.Empty;
            _audioState = string.Empty;
            _renderScale = 1f;
            _qualityLevel = -1;
            _starveIgnored = false;
            _qualityChanges = 0;
            EnergyLevel = 0;
            Sessions = 0;
        }

        HealthRow(in HealthRow from, string state, int level, int sessions, int natureDropped, double workingSetMb, string audioState, float renderScale, int qualityLevel, bool starveIgnored, int qualityChanges)
        {
            this = from;
            _renderScale = renderScale;
            _qualityLevel = qualityLevel;
            _starveIgnored = starveIgnored;
            _qualityChanges = qualityChanges;
            _experienceState = state;
            _audioState = audioState;
            EnergyLevel = level;
            Sessions = sessions;
            NatureDropped = natureDropped;
            WorkingSetMb = workingSetMb;
        }

        public DateTime TimestampUtc { get; }
        public double UptimeSec { get; }
        public double FpsAverage { get; }
        /// <summary>The slowest complete second of the interval (the average when no second was complete).</summary>
        public double FpsMin1s { get; }
        public double FrameMsP95 { get; }
        public double FrameMsMax { get; }
        public int FramesOver100Ms { get; }
        /// <summary>The largest step the audio clock made between two frames in the interval: a main-thread stall shows here as it does in the clock measurements.</summary>
        public double MaxDspStallSec { get; }
        public double MonoMb { get; }
        public double AllocMb { get; }
        public double WorkingSetMb { get; }
        public double TrackerFps { get; }
        /// <summary>NaN when the interval had no tracker packets with an age.</summary>
        public double TrackerAgeMsP95 { get; }
        public double HandsPresentRatio { get; }
        public int NotesScheduled { get; }
        public int NotesLate { get; }
        public int VoicesStolen { get; }
        public int NatureDropped { get; }
        public int Errors { get; }
        public string ExperienceState => _experienceState ?? string.Empty;
        /// <summary>starting, ok, stalled or failed (<see cref="AudioPhase"/>) at the end of the interval; empty when the caller did not say.</summary>
        public string AudioState => _audioState ?? string.Empty;
        /// <summary>The render scale at the end of the interval (1 = every pixel of the window).</summary>
        public float RenderScale => _renderScale;
        /// <summary>The step of the automatic quality ladder at the end of the interval (0 = lightest); -1 when the tier is locked.</summary>
        public int QualityLevel => _qualityLevel;
        /// <summary>1 while a starved hand tracker is being ignored because stepping down did not help it (the frame times alone decide); else 0.</summary>
        public bool StarveIgnored => _starveIgnored;
        /// <summary>How many times the automatic quality changed the picture in the interval (a tier change is a rebuild of the grass: a frame that long is not a stall of the app).</summary>
        public int QualityChanges => _qualityChanges;
        public int EnergyLevel { get; }
        public int Sessions { get; }

        /// <summary>The same row with the state of the run at its end: the flow of the visit, the energy level, visits so far, nature notes that were late, the process's memory and the state of the sound.</summary>
        public HealthRow WithRun(string experienceState, int energyLevel, int sessions, int natureDropped, long workingSetBytes, string audioState = "", float renderScale = 1f, int qualityLevel = -1,
            bool starveIgnored = false, int qualityChanges = 0) =>
            new HealthRow(this, Clean(experienceState), energyLevel, sessions, natureDropped, workingSetBytes / BytesPerMegabyte, Clean(audioState), renderScale, qualityLevel, starveIgnored, qualityChanges);

        public string ToCsv()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(",",
                TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", c),
                UptimeSec.ToString("0.0", c), FpsAverage.ToString("0.0", c), FpsMin1s.ToString("0.0", c), FrameMsP95.ToString("0.0", c), FrameMsMax.ToString("0.0", c),
                FramesOver100Ms.ToString(c), MaxDspStallSec.ToString("0.000", c), MonoMb.ToString("0.0", c), AllocMb.ToString("0.0", c), WorkingSetMb.ToString("0.0", c),
                TrackerFps.ToString("0.0", c), double.IsNaN(TrackerAgeMsP95) ? string.Empty : TrackerAgeMsP95.ToString("0.0", c), HandsPresentRatio.ToString("0.000", c),
                NotesScheduled.ToString(c), NotesLate.ToString(c), VoicesStolen.ToString(c), NatureDropped.ToString(c), Errors.ToString(c),
                ExperienceState, EnergyLevel.ToString(c), Sessions.ToString(c), AudioState, RenderScale.ToString("0.00", c), QualityLevel.ToString(c),
                StarveIgnored ? "1" : "0", QualityChanges.ToString(c));
        }

        /// <summary>A text cell must not break the line or the column count.</summary>
        static string Clean(string text) => (text ?? string.Empty).Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
