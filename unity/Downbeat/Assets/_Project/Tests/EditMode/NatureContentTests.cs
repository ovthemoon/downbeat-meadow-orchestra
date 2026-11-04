using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NatureContentTests
    {
        static bool Mentions(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        [Test]
        public void ShippedContent_BuildsTheCoreSettings_WithThePlannedNumbers()
        {
            var content = TestContent.LoadShipped();

            var energy = NatureSettingsFactory.Energy(content.nature.energy);
            Assert.AreEqual(0.06f, energy.RiseBase);
            Assert.AreEqual(6f, energy.HalfLifeSec);
            Assert.AreEqual(0.2f, energy.Level1);
            Assert.AreEqual(0.45f, energy.Level2);
            Assert.AreEqual(0.7f, energy.Level3);
            Assert.AreEqual(0.05f, energy.Hysteresis);

            var rules = NatureSettingsFactory.Rules(content.nature.rules, content.song.slotsPerBeat);
            Assert.AreEqual(2, rules.ChordCount);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, rules.ChordSteps(0));
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, rules.ChordSteps(1));
            Assert.AreEqual(0.35f, rules.FireflyChance);

            var experience = NatureSettingsFactory.Experience(content.experience);
            Assert.AreEqual(3.0, experience.NoHandsSec);
            Assert.AreEqual(6.0, experience.SettlingMinSec);
            Assert.AreEqual(1, content.nature.leadBars);
        }

        [Test]
        public void ShippedContent_PlansANatureScoreThatTheMapperCanPlay()
        {
            var content = TestContent.LoadShipped();
            var rules = NatureSettingsFactory.Rules(content.nature.rules, content.song.slotsPerBeat);
            var conductor = new NatureLayerConductor(rules, content.song.beatsPerBar * content.song.slotsPerBeat, content.song.pitchSteps, 3);
            var mapper = new NatureMapper(content.nature.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps), 12);
            var notes = new System.Collections.Generic.List<NatureNote>();

            conductor.Plan(0, 16 * 200, 3, notes);

            Assert.IsNotEmpty(notes);
            foreach (var note in notes) Assert.IsTrue(mapper.TryResolve(note.Layer, note.PitchStep, out _), note.ToString());
        }

        [Test]
        public void Missing_nature_and_experience_are_reported()
        {
            var content = TestContent.LoadShipped();
            content.nature = null;
            content.experience = null;

            Assert.IsTrue(Mentions(content, "nature is missing"));
            Assert.IsTrue(Mentions(content, "experience is missing"));
        }

        [Test]
        public void A_nonsense_energy_block_is_reported_with_its_reason()
        {
            var content = TestContent.LoadShipped();
            content.nature.energy.level2 = 0.1f;
            Assert.IsTrue(Mentions(content, "nature.energy"), "levels that do not ascend");

            content = TestContent.LoadShipped();
            content.nature.energy.hysteresis = 0.3f;
            Assert.IsTrue(Mentions(content, "hysteresis"));
        }

        [Test]
        public void A_chord_note_outside_the_scale_is_reported()
        {
            var content = TestContent.LoadShipped();
            content.nature.rules.chords[0].steps = new[] { 0, 1, 10 };

            Assert.IsTrue(Mentions(content, "outside the scale"));
        }

        [Test]
        public void Lead_bars_and_the_pulse_band_are_range_checked()
        {
            var content = TestContent.LoadShipped();
            content.nature.leadBars = 0;
            content.nature.hillPulseHalfHeight = 0.9f;
            content.nature.hillPulse = 2f;
            content.nature.hillPulseFollow = 1.5f;

            Assert.IsTrue(Mentions(content, "nature.hillPulseFollow"));
            Assert.IsTrue(Mentions(content, "nature.leadBars"));
            Assert.IsTrue(Mentions(content, "nature.hillPulseHalfHeight"));
            Assert.IsTrue(Mentions(content, "nature.hillPulse must"));
        }

        [Test]
        public void The_nature_instruments_must_be_three_with_their_own_layers_and_sample_ids()
        {
            var content = TestContent.LoadShipped();
            content.nature.instruments = content.nature.instruments.Take(2).ToArray();
            Assert.IsTrue(Mentions(content, "exactly 3"));

            content = TestContent.LoadShipped();
            content.nature.instruments[1].layer = "fireflies";
            Assert.IsTrue(Mentions(content, "two instruments"));

            content = TestContent.LoadShipped();
            content.nature.instruments[0].samples[0].id = content.instruments[0].samples[0].id;
            Assert.IsTrue(Mentions(content, "duplicated"), "a nature sample may not reuse the id of a hand sample");
        }

        [Test]
        public void A_layer_step_without_a_sample_in_reach_is_reported()
        {
            var content = TestContent.LoadShipped();
            content.nature.instruments[2].rootMidi = 50; // the drum would have to play at D3, five semitones from its only sample, and may not shift

            Assert.IsTrue(Mentions(content, "hills step 0"));
        }

        [Test]
        public void Experience_times_are_range_checked()
        {
            var content = TestContent.LoadShipped();
            content.experience.noHandsSec = 0f;
            content.experience.inputPresenceSec = 100f;

            Assert.IsTrue(Mentions(content, "experience:"));
            Assert.IsTrue(Mentions(content, "experience.inputPresenceSec"));
        }

        [Test]
        public void TheShippedSkyRule_MatchesTheEnergiesOfConductingPlay()
        {
            // the energies after 30 s of one conducted hand, from the simulation that the conducting tests write (measurements/day5/conduct-energy-sim.json)
            var json = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "..", "..", "measurements", "day5", "conduct-energy-sim.json")));
            var sim = UnityEngine.JsonUtility.FromJson<EnergySim>(json);
            var nature = TestContent.LoadShipped().nature;
            var model = new NatureVisualModel(nature.visualSmoothSec, nature.hillPulse, nature.hillPulseSec, nature.skyEnergyLow, nature.skyEnergyHigh);
            float strongest = 0f, stillest = 0f;
            foreach (var run in sim.runs)
            {
                var sky = model.SkyFor(run.energy_at_30s);
                if (run.scenario.StartsWith("still")) stillest = UnityEngine.Mathf.Max(stillest, sky);
                else if (run.scenario.StartsWith("fast") || run.scenario.StartsWith("medium")) strongest = UnityEngine.Mathf.Max(strongest, sky);
            }

            Assert.GreaterOrEqual(strongest, 0.99f, "a hand that conducts brings the sky to the sunset (sky 1.0)");
            Assert.Less(stillest, 0.4f, "a held still hand does not");
            Assert.Greater(sim.runs.Length, 4, "the simulation was read");
        }

        [System.Serializable]
        public sealed class EnergySim
        {
            public EnergyRun[] runs;
        }

        [System.Serializable]
        public sealed class EnergyRun
        {
            public string scenario;
            public float energy_at_30s;
        }
    }
}
