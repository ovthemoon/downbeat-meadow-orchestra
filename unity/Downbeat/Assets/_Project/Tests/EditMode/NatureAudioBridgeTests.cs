using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NatureAudioBridgeTests
    {
        const double Origin = 0.5;
        const double Bpm = 96.0;
        const int SlotsPerBeat = 4;
        const int SlotsPerBar = 16;
        const double BarSec = 60.0 / Bpm * 4.0; // 2.5 s
        const double SlotSec = 60.0 / Bpm / SlotsPerBeat;
        const double MinLeadSec = 0.1;
        const int FirstSample = 12;

        sealed class RecordingAudio : IAudioEngine
        {
            public readonly List<(int Sample, double When, double Rate, double Gain, double CalledAt)> Calls = new List<(int, double, double, double, double)>();
            public bool IsRunning => true;
            public bool SamplesReady => true;
            public int SamplesFailed => 0;
            public double Now { get; set; }
            public double HeardNow => Now;
            public double VisualNow => Now;
            public AudioStats Stats => new AudioStats(0, 0, 0, 0);
            public void Configure(float masterGainLinear, int maxVoices) { }
            public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples) { }
            public void Tick() { }
            public double Schedule(int sampleIndex, double when, double playbackRate, double gain) { Calls.Add((sampleIndex, when, playbackRate, gain, Now)); return when; }
            public void FadeOutAll(double seconds) { }
            public void LogFlash(double when, double heardNow) { }
        }

        static NatureAudioBridge Bridge(RecordingAudio audio, int seed = 5, int leadBars = 1)
        {
            var content = TestContent.LoadShipped();
            var rules = NatureSettingsFactory.Rules(content.nature.rules, content.song.slotsPerBeat);
            var conductor = new NatureLayerConductor(rules, SlotsPerBar, content.song.pitchSteps, seed);
            var mapper = new NatureMapper(content.nature.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps), FirstSample);
            return new NatureAudioBridge(audio, new BeatGrid(Origin, Bpm, SlotsPerBeat), conductor, mapper, leadBars * BarSec, MinLeadSec);
        }

        static List<NatureEvent> Run(NatureAudioBridge bridge, RecordingAudio audio, double from, double to, double step, Func<double, int> levelAt)
        {
            var played = new List<NatureEvent>();
            for (var t = from; t < to; t += step)
            {
                audio.Now = t;
                bridge.Tick(t, levelAt(t), played);
            }
            return played;
        }

        [Test]
        public void Bridge_SchedulesEveryNoteAtLeastOneBarMinusASlotAhead()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);

            var played = Run(bridge, audio, 10.0, 40.0, 0.016, _ => 3);

            Assert.Greater(played.Count, 20, "a level-3 meadow plays a lot");
            Assert.AreEqual(played.Count, audio.Calls.Count);
            var shortest = audio.Calls.Select((c, i) => played[i].AudioTime - c.CalledAt).Min();
            Assert.GreaterOrEqual(shortest, BarSec - SlotSec - 0.016 - 1e-6, "no nature note is put on the clock later than a bar (less one slot and a frame) before it sounds");
        }

        [Test]
        public void Bridge_PlansNothingAtLevel0_AndStartsOneBarAfterTheLevelRises()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);

            var played = Run(bridge, audio, 10.0, 30.0, 0.016, t => t < 20.0 ? 0 : 2);

            Assert.IsNotEmpty(played);
            Assert.GreaterOrEqual(played.Min(n => n.AudioTime), 20.0 + BarSec - SlotSec - 0.05, "the rise of the level shows after the bar that was already planned");
            Assert.IsTrue(played.All(n => n.Note.Layer != NatureLayer.Hills), "level 2 has no drum");
        }

        [Test]
        public void Bridge_NeverPlansTheSameSlotTwice_WhateverTheFrameTimes()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);
            var played = new List<NatureEvent>();
            var random = new Random(3);
            var t = 10.0;
            for (var i = 0; i < 3000; i++)
            {
                t += random.NextDouble() * 0.05; // 0 to 50 ms frames
                audio.Now = t;
                bridge.Tick(t, 3, played);
            }

            var keys = played.Select(n => (n.Note.Layer, n.Note.Slot, n.Note.PitchStep)).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
            Assert.IsTrue(Enumerable.Range(1, played.Count - 1).All(i => played[i].Note.Slot >= played[i - 1].Note.Slot), "in slot order");
        }

        [Test]
        public void Bridge_PlaysTheSameScoreWhatEverTheFrameRate()
        {
            var slow = new RecordingAudio();
            var fast = new RecordingAudio();

            var a = Run(Bridge(slow), slow, 10.0, 40.0, 0.050, _ => 3).Select(n => n.Note).Where(n => n.Slot < 700).ToList();
            var b = Run(Bridge(fast), fast, 10.0, 40.0, 0.004, _ => 3).Select(n => n.Note).Where(n => n.Slot < 700).ToList();

            CollectionAssert.AreEqual(b, a);
        }

        [Test]
        public void Bridge_DropsNotesThatAStallOfMoreThanTheLeadLeftInThePast()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);
            var played = Run(bridge, audio, 10.0, 12.0, 0.016, _ => 3);
            var before = played.Count;

            audio.Now = 30.0; // the main thread froze for 18 s
            bridge.Tick(30.0, 3, played);

            Assert.Greater(bridge.Dropped, 0, "the slots between the old and the new horizon that are already gone are not played late");
            Assert.IsTrue(audio.Calls.Skip(before).All(c => c.When >= 30.0 + MinLeadSec - 0.3), "only notes that still have time are scheduled (a pad starts a little before its slot)");
            Assert.IsTrue(played.Skip(before).All(n => n.AudioTime >= 30.0 + MinLeadSec));
        }

        [Test]
        public void Bridge_UsesTheNatureSamples_TheirGain_AndStartsSlowSoundsEarlierByTheirAttack()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);

            var played = Run(bridge, audio, 10.0, 30.0, 0.016, _ => 3);

            Assert.IsTrue(audio.Calls.All(c => c.Sample >= FirstSample && c.Sample < FirstSample + 10), "nature samples are loaded after the hand samples");
            var minus6Db = Math.Pow(10.0, -6.0 / 20.0);
            for (var i = 0; i < played.Count; i++)
            {
                Assert.AreEqual(played[i].Note.Gain * minus6Db, audio.Calls[i].Gain, 1e-6, played[i].Note.ToString());
                var early = played[i].AudioTime - audio.Calls[i].When;
                if (played[i].Note.Layer == NatureLayer.Sky) Assert.Greater(early, 0.1, "the pad is heard late, so it starts early");
                else Assert.Less(early, 0.01, "a bell or a drum is heard at once");
            }
        }

        [Test]
        public void Bridge_ReportsWhenEachNoteIsHeard_ForThePicture()
        {
            var audio = new RecordingAudio();
            var bridge = Bridge(audio);

            var played = Run(bridge, audio, 10.0, 20.0, 0.016, _ => 3);

            Assert.IsTrue(played.All(n => Math.Abs(n.AudioTime - new BeatGrid(Origin, Bpm, SlotsPerBeat).SlotTime(n.Note.Slot)) < 1e-9));
        }

        [Test]
        public void Constructor_RejectsNonsense()
        {
            var audio = new RecordingAudio();
            var content = TestContent.LoadShipped();
            var rules = NatureSettingsFactory.Rules(content.nature.rules, content.song.slotsPerBeat);
            var conductor = new NatureLayerConductor(rules, SlotsPerBar, content.song.pitchSteps, 1);
            var mapper = new NatureMapper(content.nature.instruments, new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps), 0);
            var grid = new BeatGrid(Origin, Bpm, SlotsPerBeat);

            Assert.Throws<ArgumentNullException>(() => new NatureAudioBridge(null, grid, conductor, mapper, BarSec, MinLeadSec));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureAudioBridge(audio, grid, conductor, mapper, 0.0, MinLeadSec));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureAudioBridge(audio, grid, conductor, mapper, BarSec, -1.0));
        }
    }
}
