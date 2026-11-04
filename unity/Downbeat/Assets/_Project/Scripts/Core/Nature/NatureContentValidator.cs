using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>nature</c> and <c>experience</c> blocks of content.json (called by <see cref="ContentValidator"/>).</summary>
    public static class NatureContentValidator
    {
        const int MaxLeadBars = 4;
        const float MaxSmoothSec = 10f;
        const float MaxOnsetOffsetMs = 500f;
        const float MaxInputPresenceSec = 30f;

        /// <param name="song">The song block (null when it is itself broken: the checks that need it are skipped).</param>
        /// <param name="usedSampleIds">Sample ids of the hand instruments: the nature samples must not reuse them.</param>
        public static void Check(NatureDto nature, ExperienceDto experience, SongDto song, ISet<string> usedSampleIds, List<string> errors)
        {
            CheckExperience(experience, errors);
            if (nature == null) { errors.Add("nature is missing"); return; }
            if (nature.leadBars < 1 || nature.leadBars > MaxLeadBars) errors.Add($"nature.leadBars must be 1..{MaxLeadBars}");
            Try(() => NatureSettingsFactory.Energy(nature.energy), "nature.energy", errors);
            if (!(nature.visualSmoothSec >= 0f && nature.visualSmoothSec <= MaxSmoothSec)) errors.Add($"nature.visualSmoothSec must be 0..{MaxSmoothSec}");
            if (!(nature.hillPulse >= 0f && nature.hillPulse <= 1f)) errors.Add("nature.hillPulse must be 0..1");
            if (!(nature.hillPulseSec >= 0.05f && nature.hillPulseSec <= 5f)) errors.Add("nature.hillPulseSec must be 0.05..5");
            if (!(nature.hillPulseRow >= 0f && nature.hillPulseRow <= 1f)) errors.Add("nature.hillPulseRow must be 0..1");
            if (!(nature.hillPulseHalfHeight >= 0.02f && nature.hillPulseHalfHeight <= 0.5f)) errors.Add("nature.hillPulseHalfHeight must be 0.02..0.5");
            if (!(nature.hillPulseFollow >= 0f && nature.hillPulseFollow <= 1f)) errors.Add("nature.hillPulseFollow must be 0..1");
            if (!(nature.skyEnergyLow >= 0f && nature.skyEnergyHigh <= 1f && nature.skyEnergyLow < nature.skyEnergyHigh)) errors.Add("nature.skyEnergyLow/High must be 0 <= low < high <= 1");

            if (song == null) return;
            var rules = Try(() => NatureSettingsFactory.Rules(nature.rules, song.slotsPerBeat), "nature.rules", errors);
            if (rules != null)
            {
                Try(() => new NatureLayerConductor(rules, song.beatsPerBar * song.slotsPerBeat, song.pitchSteps, 0), "nature.rules", errors);
            }
            CheckInstruments(nature.instruments, song, rules, usedSampleIds, errors);
        }

        static void CheckExperience(ExperienceDto x, List<string> errors)
        {
            if (x == null) { errors.Add("experience is missing"); return; }
            Try(() => NatureSettingsFactory.Experience(x), "experience", errors);
            if (!(x.inputPresenceSec >= 0f && x.inputPresenceSec <= MaxInputPresenceSec)) errors.Add($"experience.inputPresenceSec must be 0..{MaxInputPresenceSec}");
        }

        static void CheckInstruments(NatureInstrumentDto[] instruments, SongDto song, NatureRules rules, ISet<string> usedSampleIds, List<string> errors)
        {
            if (instruments == null || instruments.Length != NatureLayers.Count) { errors.Add($"nature.instruments must list exactly {NatureLayers.Count} (fireflies, sky, hills)"); return; }
            var layers = new HashSet<string>();
            var ownSampleIds = new HashSet<string>();
            var before = errors.Count;
            foreach (var instrument in instruments)
            {
                if (instrument == null) { errors.Add("nature.instruments has an empty entry"); continue; }
                if (!NatureLayers.TryParse(instrument.layer, out _)) errors.Add($"nature instrument {instrument.id}: unknown layer '{instrument.layer}'");
                else if (!layers.Add(instrument.layer)) errors.Add($"nature instrument {instrument.id}: layer '{instrument.layer}' has two instruments");
                if (!(instrument.gainDb >= -60f && instrument.gainDb <= 6f)) errors.Add($"nature instrument {instrument.id}: gainDb must be -60..6");
                if (instrument.rootMidi < 0 || instrument.rootMidi > 127) errors.Add($"nature instrument {instrument.id}: rootMidi out of range");
                if (instrument.maxPitchShift < 0 || instrument.maxPitchShift > 24) errors.Add($"nature instrument {instrument.id}: maxPitchShift must be 0..24");
                CheckSamples(instrument, usedSampleIds, ownSampleIds, errors);
            }
            if (errors.Count > before || rules == null) return;

            try
            {
                var mapper = new NatureMapper(instruments, new ScaleSpec(song.scaleSteps, song.pitchSteps), 0);
                RequireSteps(mapper, NatureLayer.Fireflies, song.pitchSteps - rules.FireflyTopSteps, song.pitchSteps - 1, errors);
                RequireSteps(mapper, NatureLayer.Hills, 0, 0, errors);
                for (var c = 0; c < rules.ChordCount; c++)
                    foreach (var step in rules.ChordSteps(c)) RequireSteps(mapper, NatureLayer.Sky, step, step, errors);
            }
            catch (ArgumentException e)
            {
                errors.Add("nature.instruments: " + e.Message);
            }
        }

        static void CheckSamples(NatureInstrumentDto instrument, ISet<string> usedSampleIds, ISet<string> ownSampleIds, List<string> errors)
        {
            if (instrument.samples == null || instrument.samples.Length == 0) { errors.Add($"nature instrument {instrument.id}: no samples"); return; }
            foreach (var sample in instrument.samples)
            {
                if (string.IsNullOrEmpty(sample.id) || !ownSampleIds.Add(sample.id) || (usedSampleIds != null && usedSampleIds.Contains(sample.id)))
                    errors.Add($"sample id '{sample.id}' is empty or duplicated");
                if (string.IsNullOrWhiteSpace(sample.file)) errors.Add($"sample {sample.id}.file is empty");
                if (sample.soundingMidi < 0 || sample.soundingMidi > 127) errors.Add($"sample {sample.id}: soundingMidi out of range");
                if (sample.perceivedOnsetOffsetMs < 0f || sample.perceivedOnsetOffsetMs > MaxOnsetOffsetMs) errors.Add($"sample {sample.id}: perceivedOnsetOffsetMs must be 0..{MaxOnsetOffsetMs}");
            }
        }

        static void RequireSteps(NatureMapper mapper, NatureLayer layer, int fromStep, int toStep, List<string> errors)
        {
            for (var step = fromStep; step <= toStep; step++)
            {
                if (!mapper.TryResolve(layer, step, out _)) errors.Add($"nature layer {NatureLayers.Name(layer)} step {step}: no sample within maxPitchShift");
            }
        }

        static T Try<T>(Func<T> build, string what, List<string> errors) where T : class
        {
            try { return build(); }
            catch (ArgumentException e) { errors.Add($"{what}: {e.Message}"); return null; }
        }
    }
}
