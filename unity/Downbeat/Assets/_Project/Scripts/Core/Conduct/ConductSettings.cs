using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>How a hand plays: by where it is (Conduct) or by striking down (Strike, the first interpreter).</summary>
    public enum PlayStyle
    {
        Conduct,
        Strike,
    }

    /// <summary>The numbers of conducting play, as an immutable snapshot of <see cref="ConductDto"/> (the content file can be replaced while the engine goes on).</summary>
    public sealed class ConductSettings
    {
        public const string ConductName = "conduct";
        public const string StrikeName = "strike";

        /// <summary>The phrases a content file without any gets, in the order of <see cref="Part"/>: the upper row is light and moves, the lower row is low and holds its root.</summary>
        static readonly int[][] DefaultPhrases =
        {
            new[] { 0, 2, 4, 7, 4, 2 }, // harp
            new[] { 7, 5, 4, 2 },       // recorder
            new[] { 9, 7, 9, 5 },       // bell
            new[] { 0, 0, 2, 0 },       // low string
            new[] { 0, 0, 4, 0 },       // marimba
            new[] { 0, 0, 0, 0 },       // hand drum
        };

        readonly int[][] _phrases;
        /// <summary>The tone numbers of the lower row's phrases in a song, by part; null for a part that has none and plays its ordinary phrase.</summary>
        readonly int[][] _songPhrases;

        ConductSettings(PlayStyle style, double motionWindowMs, float stillPalms, float halfBeatPalms, float quarterBeatPalms, float minVelocity, float loudPalms, float downbeatAccent, double releaseSec, double crossfadeSec, double holdMs, int[][] phrases, int[][] songPhrases)
        {
            Style = style;
            MotionWindowMs = motionWindowMs;
            StillPalms = stillPalms;
            HalfBeatPalms = halfBeatPalms;
            QuarterBeatPalms = quarterBeatPalms;
            MinVelocity = minVelocity;
            LoudPalms = loudPalms;
            DownbeatAccent = downbeatAccent;
            ReleaseSec = releaseSec;
            CrossfadeSec = crossfadeSec;
            HoldMs = holdMs;
            _phrases = phrases;
            _songPhrases = songPhrases;
        }

        public PlayStyle Style { get; }
        public double MotionWindowMs { get; }
        public float StillPalms { get; }
        public float HalfBeatPalms { get; }
        public float QuarterBeatPalms { get; }
        public float MinVelocity { get; }
        /// <summary>The movement (palm lengths per second) at which a hand plays at full strength.</summary>
        public float LoudPalms { get; }
        public float DownbeatAccent { get; }
        public double ReleaseSec { get; }
        /// <summary>How long the part a hand leaves by crossing into the next band fades out (a hand that goes takes <see cref="ReleaseSec"/>).</summary>
        public double CrossfadeSec { get; }
        public double HoldMs { get; }

        /// <summary>The scale steps of one part's phrase (at least one).</summary>
        public IReadOnlyList<int> Phrase(Part part) => _phrases[(int)part];

        /// <summary>
        /// The tone numbers of one part's phrase while a song plays (at least one): 0 is the bass of the bar, 1..3 its chord, 4..6 the chord an octave up (<see cref="SongBar.ChordTone"/>).
        /// Null when the content gives the part none, which then plays its ordinary <see cref="Phrase"/> in a song as well.
        /// </summary>
        public IReadOnlyList<int> SongPhrase(Part part) => _songPhrases[(int)part];

        /// <summary>
        /// The strength of the notes of a hand that moves <paramref name="palms"/> palm lengths a second: <see cref="MinVelocity"/> for a hand that is still (less than <see cref="StillPalms"/>, which is only the jitter
        /// of the picture), rising straight to 1 at <see cref="LoudPalms"/>. How loud a hand plays is how much it moves, wherever in the picture it is.
        /// </summary>
        public float VelocityForMotion(float palms)
        {
            var span = LoudPalms - StillPalms;
            var t = span > 0f ? (palms - StillPalms) / span : (palms > StillPalms ? 1f : 0f);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return MinVelocity + (1f - MinVelocity) * t;
        }

        public static ConductSettings Default => From(null, null);

        /// <summary>
        /// From the content block (null: the defaults) and the <c>--play-mode</c> override (null or empty: the content decides). A mode name that is not known is reported in
        /// <paramref name="problem"/> and ignored. Values that the content validator would have refused are not checked again here.
        /// </summary>
        public static ConductSettings From(ConductDto dto, string modeOverride) => From(dto, modeOverride, out _);

        public static ConductSettings From(ConductDto dto, string modeOverride, out string problem)
        {
            problem = null;
            var d = dto ?? new ConductDto();
            var style = ParseStyle(d.mode, PlayStyle.Conduct, out _);
            if (!string.IsNullOrEmpty(modeOverride))
            {
                style = ParseStyle(modeOverride, style, out var known);
                if (!known) problem = $"--play-mode '{modeOverride}' is not conduct or strike; the content's mode is used";
            }
            return new ConductSettings(style, d.motionWindowMs, d.stillPalms, d.halfBeatPalms, d.quarterBeatPalms, d.minVelocity, d.loudPalms, d.downbeatAccent, d.releaseSec, d.crossfadeSec, d.holdMs, PhrasesOf(d.phrases), SongPhrasesOf(d.songPhrases));
        }

        /// <summary>
        /// The mode the command line asks for: <c>--play-mode conduct|strike</c>, else <c>strike</c> when <c>--latency-mode</c> is there (the latency measurement is the time from a strike
        /// to its sound), else null (the content decides).
        /// </summary>
        public static string ModeOverride(LaunchArgs launch)
        {
            if (launch == null) return null;
            return launch.Get("play-mode") ?? (launch.Has("latency-mode") ? StrikeName : null);
        }

        public static PlayStyle ParseStyle(string name, PlayStyle fallback, out bool known)
        {
            known = true;
            if (string.Equals(name, ConductName, StringComparison.OrdinalIgnoreCase)) return PlayStyle.Conduct;
            if (string.Equals(name, StrikeName, StringComparison.OrdinalIgnoreCase)) return PlayStyle.Strike;
            known = false;
            return fallback;
        }

        static int[][] PhrasesOf(ConductPhraseDto[] given)
        {
            var phrases = new int[Parts.Count][];
            for (var i = 0; i < phrases.Length; i++) phrases[i] = DefaultPhrases[i];
            return WithGiven(phrases, given);
        }

        static int[][] SongPhrasesOf(ConductPhraseDto[] given) => WithGiven(new int[Parts.Count][], given);

        /// <summary>The phrases of the content (entries without steps or for a part that is not known are skipped) put over <paramref name="phrases"/>, which is changed and returned.</summary>
        static int[][] WithGiven(int[][] phrases, ConductPhraseDto[] given)
        {
            if (given == null) return phrases;
            foreach (var entry in given)
            {
                if (entry?.steps == null || entry.steps.Length == 0 || !Parts.TryParse(entry.part, out var part)) continue;
                phrases[(int)part] = (int[])entry.steps.Clone();
            }
            return phrases;
        }
    }

    /// <summary>
    /// How much of a normal note's energy a conducted note adds, so that a hand that is only held up keeps the meadow awake (around the first nature level) and a hand that conducts
    /// fills it. The two shares come from the simulation in <c>ConductEnergyTests</c> (the numbers are in docs/conducting-play.md); the energy constants and the level thresholds of the
    /// content are not changed.
    /// </summary>
    public static class ConductEnergy
    {
        /// <summary>A hand held still (less movement than <c>stillPalms</c>).</summary>
        public const float StillShare = 0.25f;
        /// <summary>A hand that moves.</summary>
        public const float ActiveShare = 0.40f;

        public static float Share(float motionPalms, float stillPalms) => motionPalms > stillPalms ? ActiveShare : StillShare;
    }
}
