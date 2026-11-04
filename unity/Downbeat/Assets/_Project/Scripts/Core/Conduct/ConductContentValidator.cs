using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>conduct</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional.</summary>
    public static class ConductContentValidator
    {
        public const int MaxPhraseSteps = 32;

        public static void Check(ConductDto conduct, SongDto song, List<string> errors)
        {
            if (conduct == null) return;
            ConductSettings.ParseStyle(conduct.mode, PlayStyle.Conduct, out var known);
            if (!known) errors.Add($"conduct.mode must be '{ConductSettings.ConductName}' or '{ConductSettings.StrikeName}', not '{conduct.mode}'");
            if (!(conduct.motionWindowMs >= 150f && conduct.motionWindowMs <= 1500f)) errors.Add("conduct.motionWindowMs must be 150..1500");
            if (!(conduct.stillPalms >= 0f && conduct.stillPalms <= 2f)) errors.Add("conduct.stillPalms must be 0..2");
            if (!(conduct.halfBeatPalms > conduct.stillPalms)) errors.Add("conduct.halfBeatPalms must be more than conduct.stillPalms");
            if (!(conduct.quarterBeatPalms > conduct.halfBeatPalms)) errors.Add("conduct.quarterBeatPalms must be more than conduct.halfBeatPalms");
            if (!(conduct.minVelocity >= 0f && conduct.minVelocity <= 1f)) errors.Add("conduct.minVelocity must be 0..1");
            if (!(conduct.loudPalms > conduct.stillPalms && conduct.loudPalms <= 20f)) errors.Add("conduct.loudPalms must be more than conduct.stillPalms and at most 20");
            if (!(conduct.downbeatAccent >= 0f && conduct.downbeatAccent <= 0.5f)) errors.Add("conduct.downbeatAccent must be 0..0.5");
            if (!(conduct.releaseSec >= 0.1f && conduct.releaseSec <= 10f)) errors.Add("conduct.releaseSec must be 0.1..10");
            if (!(conduct.crossfadeSec >= 0.05f && conduct.crossfadeSec <= conduct.releaseSec)) errors.Add("conduct.crossfadeSec must be 0.05..releaseSec");
            if (!(conduct.holdMs >= 0f && conduct.holdMs <= 5000f)) errors.Add("conduct.holdMs must be 0..5000");
            CheckPhrases(conduct.phrases, song, errors);
            CheckSongPhrases(conduct.songPhrases, errors);
        }

        static void CheckPhrases(ConductPhraseDto[] phrases, SongDto song, List<string> errors)
        {
            if (phrases == null || phrases.Length == 0) return; // the defaults apply
            var seen = new HashSet<Part>();
            foreach (var phrase in phrases)
            {
                if (phrase == null || !Parts.TryParse(phrase.part, out var part)) { errors.Add($"conduct.phrases: unknown part '{phrase?.part}'"); continue; }
                if (!seen.Add(part)) errors.Add($"conduct.phrases: duplicate part '{phrase.part}'");
                if (phrase.steps == null || phrase.steps.Length < 1 || phrase.steps.Length > MaxPhraseSteps)
                {
                    errors.Add($"conduct.phrases.{phrase.part}.steps must hold 1..{MaxPhraseSteps} steps");
                    continue;
                }
                if (song == null) continue;
                foreach (var step in phrase.steps)
                {
                    if (step < 0 || step >= song.pitchSteps) errors.Add($"conduct.phrases.{phrase.part}: step {step} is outside 0..{song.pitchSteps - 1} (song.pitchSteps is {song.pitchSteps})");
                }
            }
            if (seen.Count != Parts.Count) errors.Add($"conduct.phrases must list all {Parts.Count} parts when it is given");
        }

        /// <summary>Structure only: the tones of a phrase are numbers of chord tones, so they depend on the bars of a song, which <see cref="SongBook"/> checks (a song it cannot play is left out, it does not fail the content).</summary>
        static void CheckSongPhrases(ConductPhraseDto[] phrases, List<string> errors)
        {
            if (phrases == null) return; // a part without one plays its phrase of conduct.phrases
            var seen = new HashSet<Part>();
            foreach (var phrase in phrases)
            {
                if (phrase == null || !Parts.TryParse(phrase.part, out var part)) { errors.Add($"conduct.songPhrases: unknown part '{phrase?.part}'"); continue; }
                if (!seen.Add(part)) errors.Add($"conduct.songPhrases: duplicate part '{phrase.part}'");
                if (phrase.steps == null || phrase.steps.Length < 1 || phrase.steps.Length > MaxPhraseSteps)
                {
                    errors.Add($"conduct.songPhrases.{phrase.part}.steps must hold 1..{MaxPhraseSteps} steps");
                    continue;
                }
                foreach (var index in phrase.steps)
                {
                    if (index < 0 || index > SongBar.LastToneIndex) errors.Add($"conduct.songPhrases.{phrase.part}: index {index} is outside 0..{SongBar.LastToneIndex} (0 is the bass, 1..3 the chord, 4..6 the chord an octave up)");
                }
            }
        }
    }
}
