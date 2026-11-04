using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using Pose = Downbeat.Tests.HandTestData.Pose;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>Conducting play in its place: the hand input (conduct or strike), the recorded demo hands, and what it does to the energy of the meadow.</summary>
    public class ConductPlayTests
    {
        // ---- HandPlayInput: conduct or strike ----------------------------------------------------------------------------------------------

        sealed class AudioClock
        {
            public double Now;
        }

        sealed class Setup
        {
            public readonly FakeHandSource Source = new FakeHandSource { State = TrackerState.Running };
            public readonly AudioClock Clock = new AudioClock { Now = ConductRig.GridOrigin - 1.0 };
            public readonly ConductPlayer Conduct;
            public readonly HandPlayInput Input;
            public readonly BeatGrid Grid = new BeatGrid(ConductRig.GridOrigin, ConductRig.Bpm, ConductRig.SlotsPerBeat);

            public Setup(string playMode)
            {
                var content = JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));
                var range = new PlayableRange();
                range.Set(content.song.pitchSteps);
                var settings = new HandSettingsHolder();
                settings.Set(content.hand);
                Conduct = new ConductPlayer(() => Clock.Now);
                Conduct.Configure(content, playMode);
                Input = new HandPlayInput(Source, range, settings, new HandMotionTracker(settings), new HandInterpreter(), Conduct);
                Input.OnGridReady(Grid, ConductRig.SlotsPerBar);
            }

            /// <summary>One hand that makes a hard stroke downward (a strike) in the middle of the picture, at 30 pictures a second, for <paramref name="seconds"/>.</summary>
            public List<NoteIntent> PlayStrokes(double seconds)
            {
                var notes = new List<NoteIntent>();
                long seq = 1;
                var captureMs = 0.0;
                var y = 0.3f;
                for (var i = 0; i < (int)(seconds * 30); i++)
                {
                    Clock.Now += 1.0 / 30.0;
                    captureMs += 1000.0 / 30.0;
                    y += (i % 15 < 4) ? 0.025f : (i % 15 < 8 ? 0f : -0.0125f); // fast down for 4 pictures (3.75 palms a second ... see StrikeFrames), then rest and a slow return
                    Source.Pending.Add(Frame(seq++, captureMs, new Pose(0, 0.5f, y)));
                    Input.Tick(Clock.Now);
                    while (Input.TryDequeue(out var intent)) notes.Add(intent);
                }
                return notes;
            }
        }

        [Test]
        public void InConductMode_TheHandPlaysByWhereItIs_AndNoStrikeIsLookedFor()
        {
            var setup = new Setup(null);

            var notes = setup.PlayStrokes(8.0);

            Assert.IsTrue(setup.Conduct.IsConducting, "the shipped content conducts");
            Assert.AreEqual(0, setup.Input.StrikeCount, "no strike is looked for");
            Assert.GreaterOrEqual(notes.Count, 8, "but the raised hand plays");
            Assert.That(notes.Select(n => n.Timing.IsValid), Has.All.True);
            Assert.That(notes.Select(n => double.IsNaN(n.RequestedAt)), Has.All.False, "each note names its slot");
        }

        [Test]
        public void InStrikeMode_TheHandPlaysOnlyWhenItStrikes_AsBefore()
        {
            var setup = new Setup("strike");

            var notes = setup.PlayStrokes(8.0);

            Assert.IsFalse(setup.Conduct.IsConducting);
            Assert.Greater(setup.Input.StrikeCount, 0, "the old interpreter is intact");
            Assert.AreEqual(setup.Input.StrikeCount, notes.Count, "a note for every strike and nothing else");
        }

        [Test]
        public void ReplaysAndSoakGetNotesInConductMode_FromTheDemoHands()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "replay", "demo-hands.json"));
            var player = new ReplayPlayer(JsonUtility.FromJson<ReplayDto>(File.ReadAllText(path)));
            var rig = new ConductRig();

            rig.RunReplay(player, 18.0);

            Assert.IsTrue(player.IsFinished, "the whole recording was played");
            Assert.GreaterOrEqual(rig.Notes.Count, 20, "the demo hands (one hand, 16 s) conduct a piece, so the night run has something to play");
            Assert.GreaterOrEqual(rig.Notes.Select(n => n.Part).Distinct().Count(), 1);
            TestContext.Out.WriteLine($"CONDUCT-REPLAY notes={rig.Notes.Count} parts={string.Join("/", rig.Notes.Select(n => n.Part).Distinct())} over 16.1 s of demo-hands.json");
        }

        [Test]
        public void TheDemoHandsPlayTheSameNotesEveryTime()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "replay", "demo-hands.json"));
            var dto = JsonUtility.FromJson<ReplayDto>(File.ReadAllText(path));
            List<(long, int, float)> Play()
            {
                var rig = new ConductRig();
                rig.RunReplay(new ReplayPlayer(dto), 18.0);
                return rig.Notes.Select(n => (rig.SlotOf(n), n.PitchStep, n.Velocity)).ToList();
            }

            CollectionAssert.AreEqual(Play(), Play());
        }

        // ---- energy ---------------------------------------------------------------------------------------------------------------------

        /// <summary>Plays 30 s of a hand into the real <see cref="PerformanceEnergy"/> with the constants of the shipped content and reports the level and energy it ends at.</summary>
        static (float energy, int level, float peak, int notes) Energy(Func<double, Pose[]> hand, double pictureEverySec, double seconds = 30.0)
        {
            var content = JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));
            var rig = new ConductRig();
            var energy = new PerformanceEnergy(NatureSettingsFactory.Energy(content.nature.energy));
            var peak = 0f;
            var end = rig.AudioNow + seconds;
            var delivered = 0;
            // a 60 Hz loop like the app's: the rig is run one frame at a time and every note it makes adds to the energy
            while (rig.AudioNow < end)
            {
                rig.Run(ConductRig.AudioFrameSec, hand, pictureEverySec);
                for (; delivered < rig.Notes.Count; delivered++) energy.AddNote(rig.Notes[delivered].Velocity, rig.Notes[delivered].EnergyWeight);
                energy.Advance(ConductRig.AudioFrameSec);
                peak = Math.Max(peak, energy.Value);
            }
            return (energy.Value, energy.Level, peak, rig.Notes.Count);
        }

        [TestCase("still hand, recorder (sky)", Part.Recorder, 0f, 1, 0.2f, 0.45f)]
        [TestCase("still hand, marimba (ground)", Part.Marimba, 0f, 1, 0.2f, 0.45f)]
        [TestCase("slow movement (1.4 palms a second), recorder", Part.Recorder, 1.4f, 2, 0.45f, 0.7f)]
        [TestCase("slow movement (1.4 palms a second), marimba", Part.Marimba, 1.4f, 2, 0.45f, 0.7f)]
        [TestCase("medium movement (2.5 palms a second), recorder", Part.Recorder, 2.5f, 3, 0.7f, 1.01f)]
        [TestCase("fast movement (6 palms a second), recorder", Part.Recorder, 6.0f, 3, 0.7f, 1.01f)]
        [TestCase("fast movement (6 palms a second), marimba", Part.Marimba, 6.0f, 3, 0.7f, 1.01f)]
        public void TheEnergyAfter30Seconds_OfAHandHeldUp_OrConducting(string name, Part part, float palmsPerSecond, int expectedLevel, float from, float to)
        {
            foreach (var pictureEverySec in new[] { 1.0 / 30.0, 1.0 / 6.0 })
            {
                var hand = palmsPerSecond <= 0f ? ConductRig.StillIn(part) : ConductRig.CircleIn(part, palmsPerSecond);

                var (energy, level, peak, notes) = Energy(hand, pictureEverySec);

                TestContext.Out.WriteLine($"CONDUCT-ENERGY scenario=\"{name}\" tracker={1.0 / pictureEverySec:0}fps notes={notes} energy30s={energy:0.000} peak={peak:0.000} level={level}");
                Assert.AreEqual(expectedLevel, level, $"{name} at {1.0 / pictureEverySec:0} pictures a second ended at energy {energy:0.000}");
                Assert.GreaterOrEqual(energy, from);
                Assert.Less(energy, to);
            }
        }

        [Test]
        public void AStillHandThatLeaves_LetsTheMeadowSettle()
        {
            var content = JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));
            var rig = new ConductRig();
            var energy = new PerformanceEnergy(NatureSettingsFactory.Energy(content.nature.energy));
            var hand = ConductRig.Gone(ConductRig.StillIn(Part.Recorder), 20.0);
            var delivered = 0;
            var end = rig.AudioNow + 60.0;
            var levelAt40 = -1;
            var start = rig.AudioNow;
            while (rig.AudioNow < end)
            {
                rig.Run(ConductRig.AudioFrameSec, hand, 1.0 / 30.0);
                for (; delivered < rig.Notes.Count; delivered++) energy.AddNote(rig.Notes[delivered].Velocity, rig.Notes[delivered].EnergyWeight);
                energy.Advance(ConductRig.AudioFrameSec);
                if (levelAt40 < 0 && rig.AudioNow - start >= 40.0) levelAt40 = energy.Level;
            }

            Assert.Less(energy.Value, 0.05f, "20 s after the hand left the energy has gone back to the idle value of the content");
            TestContext.Out.WriteLine($"CONDUCT-ENERGY scenario=\"still hand that leaves at 20 s\" energy60s={energy.Value:0.000} level40s={levelAt40}");
        }
    }
}
