using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>Checks content.json before anything is built from it. Fails with readable messages instead of throwing deep inside views.</summary>
    public static class ContentValidator
    {
        /// <summary>4: six parts (two rows of three cells) instead of three; a content file of an older schema is refused and the app starts from its fallbacks.</summary>
        public const int SupportedSchema = 4;
        const float MinBpm = 40f;
        const float MaxBpm = 240f;
        const int MaxVoicesLimit = 64;
        const double MinLeadLimit = 0.02;
        const double MaxLeadLimit = 1.0;

        public static IReadOnlyList<string> Validate(ContentDto content)
        {
            var errors = new List<string>();
            if (content == null) { errors.Add("content is null"); return errors; }
            if (content.schemaVersion != SupportedSchema) errors.Add($"schemaVersion {content.schemaVersion} is not {SupportedSchema}");
            CheckAudio(content.audio, errors);
            CheckSong(content.song, errors);
            CheckHand(content.hand, errors);
            ConductContentValidator.Check(content.conduct, content.song, errors);
            CellsContentValidator.Check(content.cells, errors);
            PoseContentValidator.Check(content.pose, errors);
            CutoffContentValidator.Check(content.cutoff, errors);
            AutoQualityContentValidator.Check(content.autoQuality, content.meadow, errors);
            MeadowContentValidator.Check(content.meadow, errors);
            CheckInstruments(content, errors);
            RecordContentValidator.Check(content.display, content.record, errors);
            TrailContentValidator.Check(content.trail, errors);
            TutorialContentValidator.Check(content.tutorial, errors);
            NatureContentValidator.Check(content.nature, content.experience, content.song, HandSampleIds(content), errors);
            return errors;
        }

        static void CheckAudio(AudioSettingsDto audio, List<string> errors)
        {
            if (audio == null) { errors.Add("audio is missing"); return; }
            if (audio.masterGainDb > 0f) errors.Add("audio.masterGainDb must be <= 0");
            if (audio.maxVoices < 1 || audio.maxVoices > MaxVoicesLimit) errors.Add($"audio.maxVoices must be 1..{MaxVoicesLimit}");
            if (audio.minLeadSec < MinLeadLimit || audio.minLeadSec > MaxLeadLimit) errors.Add($"audio.minLeadSec must be {MinLeadLimit}..{MaxLeadLimit}");
            if (audio.originDelaySec < 0.0 || audio.originDelaySec > 5.0) errors.Add("audio.originDelaySec must be 0..5");
        }

        static void CheckHand(HandSettingsDto hand, List<string> errors)
        {
            if (hand == null) return; // optional: the defaults apply
            if (!(hand.minScore >= 0f && hand.minScore <= 1f)) errors.Add("hand.minScore must be 0..1");
            if (!(hand.triggerSpeed > 0f && hand.triggerSpeed <= 50f)) errors.Add("hand.triggerSpeed must be > 0 and <= 50");
            if (!(hand.rearmSpeed >= 0f && hand.rearmSpeed < hand.triggerSpeed)) errors.Add("hand.rearmSpeed must be >= 0 and below triggerSpeed");
            if (!(hand.fullSpeed > hand.triggerSpeed && hand.fullSpeed <= 100f)) errors.Add("hand.fullSpeed must be above triggerSpeed (and <= 100)");
            if (!(hand.minVelocity >= 0f && hand.minVelocity <= 1f)) errors.Add("hand.minVelocity must be 0..1");
            if (!(hand.peakWaitMs >= 0f && hand.peakWaitMs <= 300f)) errors.Add("hand.peakWaitMs must be 0..300");
            if (!(hand.peakDropRatio > 0f && hand.peakDropRatio <= 1f)) errors.Add("hand.peakDropRatio must be > 0 and <= 1");
            if (!(hand.velocityWindowMs >= 30f && hand.velocityWindowMs <= 500f)) errors.Add("hand.velocityWindowMs must be 30..500");
            if (!(hand.minRetriggerMs >= 0f && hand.minRetriggerMs <= 2000f)) errors.Add("hand.minRetriggerMs must be 0..2000");
            if (!(hand.maxGapMs >= 50f && hand.maxGapMs <= 2000f)) errors.Add("hand.maxGapMs must be 50..2000");
            if (!(hand.heightHysteresis >= 0f && hand.heightHysteresis <= 0.1f)) errors.Add("hand.heightHysteresis must be 0..0.1");
            if (!(hand.partHysteresis >= 0f && hand.partHysteresis <= 0.1f)) errors.Add("hand.partHysteresis must be 0..0.1");
            if (!(hand.udpPort >= 1024 && hand.udpPort <= 65535)) errors.Add("hand.udpPort must be 1024..65535");
            if (!(hand.trackerTimeoutSec >= 0.5f && hand.trackerTimeoutSec <= 30f)) errors.Add("hand.trackerTimeoutSec must be 0.5..30");
        }

        static void CheckSong(SongDto song, List<string> errors)
        {
            if (song == null) { errors.Add("song is missing"); return; }
            if (song.bpm < MinBpm || song.bpm > MaxBpm) errors.Add($"song.bpm must be {MinBpm}..{MaxBpm}");
            if (song.slotsPerBeat < 1) errors.Add("song.slotsPerBeat must be >= 1");
            if (song.beatsPerBar < 1) errors.Add("song.beatsPerBar must be >= 1");
            if (song.maxPushSlots < 0) errors.Add("song.maxPushSlots must be >= 0");
            try { new ScaleSpec(song.scaleSteps, song.pitchSteps); }
            catch (ArgumentException e) { errors.Add("song.scale: " + e.Message); }

            var seen = new HashSet<Part>();
            if (song.partMinGapSlots == null) { errors.Add("song.partMinGapSlots is missing"); return; }
            foreach (var gap in song.partMinGapSlots)
            {
                if (!Parts.TryParse(gap.part, out var part)) errors.Add($"song.partMinGapSlots: unknown part '{gap.part}'");
                else if (!seen.Add(part)) errors.Add($"song.partMinGapSlots: duplicate part '{gap.part}'");
                if (gap.slots < 1) errors.Add($"song.partMinGapSlots.{gap.part} must be >= 1");
            }
            if (seen.Count != Parts.Count) errors.Add($"song.partMinGapSlots must list all {Parts.Count} parts");
        }

        static HashSet<string> HandSampleIds(ContentDto content)
        {
            var ids = new HashSet<string>();
            if (content.instruments == null) return ids;
            foreach (var instrument in content.instruments)
            {
                if (instrument?.samples == null) continue;
                foreach (var sample in instrument.samples) ids.Add(sample.id);
            }
            return ids;
        }

        static void CheckInstruments(ContentDto content, List<string> errors)
        {
            if (content.instruments == null || content.instruments.Length == 0) { errors.Add("instruments is missing"); return; }
            var ids = new HashSet<string>();
            var sampleIds = new HashSet<string>();
            foreach (var instrument in content.instruments)
            {
                if (string.IsNullOrEmpty(instrument.id) || !ids.Add(instrument.id)) errors.Add($"instrument id '{instrument.id}' is empty or duplicated");
                if (!Parts.TryParse(instrument.part, out _)) errors.Add($"instrument {instrument.id}: unknown part '{instrument.part}'");
                if (instrument.samples == null || instrument.samples.Length == 0) { errors.Add($"instrument {instrument.id}: no samples"); continue; }
                foreach (var sample in instrument.samples)
                {
                    if (string.IsNullOrEmpty(sample.id) || !sampleIds.Add(sample.id)) errors.Add($"sample id '{sample.id}' is empty or duplicated");
                    Require(sample.file, $"sample {sample.id}.file", errors);
                    if (sample.soundingMidi < 0 || sample.soundingMidi > 127) errors.Add($"sample {sample.id}: soundingMidi out of range");
                    if (sample.perceivedOnsetOffsetMs < 0f || sample.perceivedOnsetOffsetMs > 200f) errors.Add($"sample {sample.id}: perceivedOnsetOffsetMs must be 0..200");
                }
            }
            if (errors.Count > 0 || content.song == null) return;

            // Every pitch step of every part must resolve within the allowed pitch shift.
            try
            {
                var mapper = new PitchMapper(content.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps));
                for (var p = 0; p < Parts.Count; p++)
                {
                    for (var step = 0; step < mapper.PitchSteps; step++)
                    {
                        if (!mapper.TryResolve((Part)p, step, out _)) errors.Add($"{(Part)p} step {step}: no sample within maxPitchShift");
                    }
                    var rootClass = ((mapper.InstrumentFor((Part)p).rootMidi - content.song.rootMidi) % 12 + 12) % 12;
                    if (rootClass != 0) errors.Add($"{(Part)p}: instrument rootMidi is not the song root pitch class");
                }
            }
            catch (ArgumentException e)
            {
                errors.Add("instruments: " + e.Message);
            }
        }

        static void Require(string value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{name} is empty");
        }
    }
}
