using System;
using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class PitchMapperTests
    {
        /// <summary>The instruments given, and a one-sample bell, low string and hand drum after them: the mapper wants an instrument for every one of the six parts.</summary>
        static InstrumentDto[] WithTheOtherParts(params InstrumentDto[] given)
        {
            var all = new List<InstrumentDto>(given);
            foreach (var name in new[] { "bell", "lowstring", "handdrum" }) all.Add(TestContent.Instrument(name, name, 69, TestContent.Sample(name + "_x", 69)));
            return all.ToArray();
        }

        static PitchMapper SmallMapper(float tuneCents = 0f)
        {
            // harp-like: A4=69, C5=72, E5=76 samples; marimba and recorder with one sample each
            return new PitchMapper(WithTheOtherParts(
                TestContent.Instrument("harp", "harp", 69, TestContent.Sample("h_a4", 69, tuneCents), TestContent.Sample("h_c5", 72), TestContent.Sample("h_e5", 76)),
                TestContent.Instrument("marimba", "marimba", 69, TestContent.Sample("m_c5", 72)),
                TestContent.Instrument("recorder", "recorder", 81, TestContent.Sample("r_as5", 82, 0f, 17f))), TestContent.AMinorPentatonic());
        }

        [Test]
        public void Scale_steps_follow_A_minor_pentatonic_over_two_octaves()
        {
            var scale = TestContent.AMinorPentatonic();
            var expected = new[] { 69, 72, 74, 76, 79, 81, 84, 86, 88, 91 }; // A4 C5 D5 E5 G5 A5 C6 D6 E6 G6
            for (var step = 0; step < expected.Length; step++) Assert.AreEqual(expected[step], scale.MidiForStep(69, step), $"step {step}");
        }

        [Test]
        public void An_exact_sample_is_played_at_rate_one()
        {
            Assert.IsTrue(SmallMapper().TryResolve(Part.Harp, 1, out var r)); // C5
            Assert.AreEqual("h_c5", r.SampleId);
            Assert.AreEqual(0, r.Semitones);
            Assert.AreEqual(1.0, r.PlaybackRate, 1e-12);
        }

        [Test]
        public void The_closest_sample_is_chosen_and_the_rate_matches_the_semitone_shift()
        {
            Assert.IsTrue(SmallMapper().TryResolve(Part.Harp, 2, out var r)); // D5 (74): C5 is 2 away, E5 is 2 away -> tie -> the higher one, pitched down
            Assert.AreEqual("h_e5", r.SampleId);
            Assert.AreEqual(-2, r.Semitones);
            Assert.AreEqual(Math.Pow(2, -2 / 12.0), r.PlaybackRate, 1e-12);
            Assert.AreEqual(74, r.TargetMidi);
        }

        [Test]
        public void The_measured_pitch_is_used_not_the_file_name_so_the_recorder_A5_comes_from_A_sharp_5_minus_one_semitone()
        {
            Assert.IsTrue(SmallMapper().TryResolve(Part.Recorder, 0, out var r)); // A5 = 81 from the sample sounding at 82
            Assert.AreEqual(81, r.TargetMidi);
            Assert.AreEqual(-1, r.Semitones);
            Assert.AreEqual(Math.Pow(2, -1 / 12.0), r.PlaybackRate, 1e-12);
        }

        [Test]
        public void Measured_tuning_error_is_corrected_by_the_playback_rate()
        {
            // the sample is 10 cents flat (-10): playing it at its own pitch must raise it by 10 cents
            Assert.IsTrue(SmallMapper(-10f).TryResolve(Part.Harp, 0, out var r));
            Assert.AreEqual(Math.Pow(2, 10 / 1200.0), r.PlaybackRate, 1e-12);
        }

        [Test]
        public void Onset_offset_shrinks_when_the_sample_is_played_faster()
        {
            Assert.IsTrue(SmallMapper().TryResolve(Part.Recorder, 0, out var slow)); // rate < 1
            Assert.AreEqual(0.017 / slow.PlaybackRate, slow.OnsetOffsetSec, 1e-12);
            Assert.Greater(slow.OnsetOffsetSec, 0.017);
        }

        [Test]
        public void Steps_beyond_the_allowed_shift_are_rejected()
        {
            var tight = new PitchMapper(WithTheOtherParts(
                TestContent.Instrument("harp", "harp", 69, TestContent.Sample("h", 69)),
                TestContent.Instrument("marimba", "marimba", 69, TestContent.Sample("m", 69)),
                TestContent.Instrument("recorder", "recorder", 69, TestContent.Sample("r", 69))), TestContent.AMinorPentatonic());
            tight.InstrumentFor(Part.Harp).maxPitchShift = 3;
            Assert.IsTrue(tight.TryResolve(Part.Harp, 1, out _)); // +3
            Assert.IsFalse(tight.TryResolve(Part.Harp, 2, out _)); // +5
            Assert.IsFalse(tight.TryResolve(Part.Harp, 99, out _), "unknown step");
        }

        [Test]
        public void Sample_indexes_follow_load_order_across_instruments()
        {
            var mapper = SmallMapper();
            Assert.AreEqual(8, mapper.AllSamples.Count);
            Assert.IsTrue(mapper.TryResolve(Part.Marimba, 1, out var m)); // C5 from the only marimba sample (index 3)
            Assert.AreEqual(3, m.SampleIndex);
            Assert.AreEqual("m_c5", mapper.AllSamples[m.SampleIndex].id);
        }

        [Test]
        public void Constructor_rejects_missing_or_duplicate_parts()
        {
            Assert.Throws<ArgumentException>(() => new PitchMapper(new[] { TestContent.Instrument("a", "harp", 69, TestContent.Sample("x", 69)) }, TestContent.AMinorPentatonic()), "five parts are missing");
            Assert.Throws<ArgumentException>(() => new PitchMapper(WithTheOtherParts(
                TestContent.Instrument("a", "harp", 69, TestContent.Sample("x", 69)),
                TestContent.Instrument("b", "harp", 69, TestContent.Sample("y", 69))), TestContent.AMinorPentatonic()), "a part with two instruments");
            Assert.Throws<ArgumentException>(() => new PitchMapper(WithTheOtherParts(
                TestContent.Instrument("a", "strings", 69, TestContent.Sample("x", 69)),
                TestContent.Instrument("b", "harp", 69, TestContent.Sample("y", 69))), TestContent.AMinorPentatonic()), "the old name of the harp is the harp");
        }

        // ---- a pitch given as a MIDI note (the notes of a song) -----------------------------------------------------------------------------

        static PitchMapper ShippedMapper(out ContentDto content)
        {
            content = TestContent.LoadShipped();
            return new PitchMapper(content.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps));
        }

        [Test]
        public void TryResolveMidi_PicksTheNearestSample()
        {
            var mapper = ShippedMapper(out _);   // the bell has samples sounding 70 74 80 84 88 92

            Assert.IsTrue(mapper.TryResolveMidi(Part.Bell, 79, out var r));

            Assert.AreEqual("bell_Gs5", r.SampleId);
            Assert.AreEqual(80, mapper.AllSamples[r.SampleIndex].soundingMidi);
            Assert.AreEqual(79, r.TargetMidi);
            Assert.AreEqual(-1, r.Semitones);
            Assert.AreEqual(Math.Pow(2, (-100.0 - mapper.AllSamples[r.SampleIndex].tuneCents) / 1200.0), r.PlaybackRate, 1e-12);
        }

        [Test]
        public void TryResolveMidi_RefusesBeyondTheShift()
        {
            var mapper = ShippedMapper(out _);   // 77 is 3 semitones from the samples at 74 and 80, and the bell may be moved 2

            Assert.IsFalse(mapper.TryResolveMidi(Part.Bell, 77, out _));
            Assert.IsTrue(mapper.TryResolveMidi(Part.Bell, 78, out _), "2 from 80");
        }

        [Test]
        public void TryResolveMidi_OnATie_PrefersTheHigherSample()
        {
            Assert.IsTrue(SmallMapper().TryResolveMidi(Part.Harp, 74, out var r));   // between h_c5 (72) and h_e5 (76)

            Assert.AreEqual("h_e5", r.SampleId);
            Assert.AreEqual(-2, r.Semitones);
        }

        [Test]
        public void TryResolveMidi_TakesAnyMidi_NotOnlyTheNotesOfTheScale()
        {
            var mapper = ShippedMapper(out _);

            Assert.IsTrue(mapper.TryResolveMidi(Part.Harp, 61, out var low), "below the root of the harp, which is 69");
            Assert.AreEqual(61, low.TargetMidi);
            Assert.IsTrue(mapper.TryResolveMidi(Part.Harp, 95, out _));
            Assert.IsFalse(mapper.TryResolveMidi(Part.Harp, 20, out _));
            Assert.IsFalse(mapper.TryResolveMidi(Part.Harp, 127, out _));
        }

        [Test]
        public void TryResolveMidi_AnyInt_IsRefusedWithoutThrowing()
        {
            var mapper = ShippedMapper(out _);

            Assert.IsFalse(mapper.TryResolveMidi(Part.Harp, int.MinValue, out _));
            Assert.IsFalse(mapper.TryResolveMidi(Part.Harp, int.MaxValue, out _));
        }

        [Test]
        public void TryResolveMidi_GivesTheStepPathsResult_ForTheMidiOfTheStep()
        {
            var mapper = ShippedMapper(out var content);
            var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);

            for (var p = 0; p < Parts.Count; p++)
            {
                var part = (Part)p;
                for (var step = 0; step < mapper.PitchSteps; step++)
                {
                    Assert.IsTrue(mapper.TryResolve(part, step, out var byStep));
                    Assert.IsTrue(mapper.TryResolveMidi(part, scale.MidiForStep(mapper.InstrumentFor(part).rootMidi, step), out var byMidi));
                    Assert.AreEqual(byStep, byMidi, $"{part} step {step}");
                }
            }
        }

        // The result of every part and step of the shipped content as the code before the MIDI path gave it (sample id and the shift in semitones), worked out by hand from the measured pitches.
        static readonly Dictionary<Part, string> StepResultsBeforeTheMidiPath = new Dictionary<Part, string>
        {
            { Part.Harp, "harp_A4:0 harp_C5:0 harp_E5:-2 harp_E5:0 harp_G5:0 harp_G5:2 harp_D6:-2 harp_D6:0 harp_D6:2 harp_D6:5" },
            { Part.Recorder, "recorder_As5:-1 recorder_C6:0 recorder_D6:0 recorder_E6:0 recorder_G6:0 recorder_G6:2 recorder_G6:5 recorder_G6:7 recorder_G6:9 recorder_G6:12" },
            { Part.Bell, "bell_As4:-1 bell_D5:-2 bell_D5:0 bell_D5:2 bell_Gs5:-1 bell_Gs5:1 bell_C6:0 bell_E6:-2 bell_E6:0 bell_Gs6:-1" },
            { Part.LowString, "lowstring_A3:0 lowstring_D4:-2 lowstring_D4:0 lowstring_D4:2 lowstring_G4:0 lowstring_A4:0 lowstring_D5:-2 lowstring_D5:0 lowstring_D5:2 lowstring_G5:0" },
            { Part.Marimba, "marimba_C5_med:-3 marimba_C5_med:0 marimba_C5_med:2 marimba_G5_med:-3 marimba_G5_med:0 marimba_G5_med:2 marimba_G5_med:5 marimba_G5_med:7 marimba_G5_med:9 marimba_G5_med:12" },
            { Part.HandDrum, "handdrum_A2_tumba:0 handdrum_A2_tumba:3 handdrum_A2_tumba:5 handdrum_A3_conga:-5 handdrum_A3_conga:-2 handdrum_A3_conga:0 handdrum_A3_conga:3 handdrum_A3_conga:5 handdrum_A4_quinto:-5 handdrum_A4_quinto:-2" },
        };

        [Test]
        public void TryResolve_StillGivesTheSameResult()
        {
            var mapper = ShippedMapper(out var content);
            var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);

            for (var p = 0; p < Parts.Count; p++)
            {
                var part = (Part)p;
                var results = new List<string>();
                for (var step = 0; step < mapper.PitchSteps; step++)
                {
                    Assert.IsTrue(mapper.TryResolve(part, step, out var r), $"{part} step {step}");
                    results.Add($"{r.SampleId}:{r.Semitones}");
                    Assert.AreEqual(scale.MidiForStep(mapper.InstrumentFor(part).rootMidi, step), r.TargetMidi, $"{part} step {step}");
                    var sample = mapper.AllSamples[r.SampleIndex];
                    Assert.AreEqual(Math.Pow(2, (r.Semitones * 100.0 - sample.tuneCents) / 1200.0), r.PlaybackRate, 1e-12, $"{part} step {step}");
                }
                Assert.AreEqual(StepResultsBeforeTheMidiPath[part], string.Join(" ", results), part.ToString());
            }
            Assert.IsFalse(mapper.TryResolve(Part.Harp, -1, out _));
            Assert.IsFalse(mapper.TryResolve(Part.Harp, mapper.PitchSteps, out _));
        }

        [Test]
        public void The_shipped_content_resolves_every_part_and_step_within_the_allowed_shift()
        {
            var content = TestContent.LoadShipped();
            var mapper = new PitchMapper(content.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps));
            for (var p = 0; p < Parts.Count; p++)
            {
                for (var step = 0; step < mapper.PitchSteps; step++)
                {
                    Assert.IsTrue(mapper.TryResolve((Part)p, step, out var r), $"{(Part)p} step {step}");
                    Assert.LessOrEqual(Math.Abs(r.Semitones), mapper.InstrumentFor((Part)p).maxPitchShift);
                    Assert.Greater(r.PlaybackRate, 0.4);
                    Assert.Less(r.PlaybackRate, 2.6);
                }
            }
        }
    }
}
