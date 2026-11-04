using System.Collections.Generic;
using System.Linq;
using Downbeat.App;
using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    public class ReplayTests
    {
        const double GridOrigin = 10.0;
        const int SlotsPerBar = 16; // 96 bpm, 4 slots per beat: 2.5 s per bar

        static readonly (float x, float y, float speed)[] Strikes =
        {
            (0.15f, 0.30f, 7f), (0.50f, 0.45f, 5f), (0.85f, 0.60f, 12f), (0.50f, 0.25f, 9f),
        };

        [Test]
        public void A_valid_replay_passes_and_a_json_round_trip_keeps_it_valid()
        {
            var dto = ReplayOfStrikes(Strikes);
            var again = JsonUtility.FromJson<ReplayDto>(JsonUtility.ToJson(dto));

            Assert.IsEmpty(ReplayValidator.Validate(dto));
            Assert.IsEmpty(ReplayValidator.Validate(again));
            Assert.AreEqual(dto.frames.Length, again.frames.Length);
        }

        [Test]
        public void Broken_replays_are_rejected_with_a_reason()
        {
            var wrongFormat = ReplayOfStrikes(Strikes);
            wrongFormat.format = "something-else";
            var backwards = ReplayOfStrikes(Strikes);
            backwards.frames[10].t = 0;
            var shortHand = ReplayOfStrikes(Strikes);
            shortHand.frames[20].h[0].p = new float[10];
            var duplicateIds = ReplayOfStrikes(Strikes);
            duplicateIds.frames[20].h = new[] { ReplayHand(0, 0.2f, 0.2f), ReplayHand(0, 0.8f, 0.2f) };
            var nan = ReplayOfStrikes(Strikes);
            nan.frames[20].h[0].p[3] = float.NaN;
            var empty = new ReplayDto { format = ReplayFormat.Name, version = 1, aspect = 1.3f, frames = new ReplayFrameDto[0] };

            foreach (var broken in new[] { wrongFormat, backwards, shortHand, duplicateIds, nan, empty })
            {
                Assert.IsNotEmpty(ReplayValidator.Validate(broken));
                Assert.Throws<System.ArgumentException>(() => new ReplayPlayer(broken));
            }
            Assert.IsNotEmpty(ReplayValidator.Validate(null));
        }

        [TestCase("replay/demo-hands.json", true)]
        [TestCase("demo-hands.json", true)]
        [TestCase("replay/my_run-2.json", true)]
        [TestCase("../content.json", false)]
        [TestCase("/etc/passwd.json", false)]
        [TestCase("https://example.com/x.json", false)]
        [TestCase("replay\\x.json", false)]
        [TestCase("replay/x.txt", false)]
        [TestCase("replay/x.json?y=1", false)]
        [TestCase("replay//x.json", false)]
        [TestCase("", false)]
        public void Only_relative_json_paths_inside_the_site_may_be_requested(string path, bool allowed)
        {
            Assert.AreEqual(allowed, ReplayPath.IsAllowed(path));
        }

        [Test]
        public void The_player_hands_out_frames_in_order_when_their_time_comes()
        {
            var player = new ReplayPlayer(ReplayOfStrikes(Strikes));
            var frames = new List<HandFrame>();

            player.Advance(0, frames);
            Assert.AreEqual(1, frames.Count);
            player.Advance(FrameMs * 3, frames);
            Assert.AreEqual(4, frames.Count);
            player.Advance(1e9, frames);

            Assert.IsTrue(player.IsFinished);
            Assert.AreEqual(player.FrameCount, frames.Count);
            for (var i = 1; i < frames.Count; i++) Assert.Greater(frames[i].Seq, frames[i - 1].Seq);
        }

        [Test]
        public void The_same_replay_run_twice_gives_the_identical_note_list()
        {
            var dto = ReplayOfStrikes(Strikes);

            var first = PlayThrough(dto, 1.0 / 72.0);
            var second = PlayThrough(dto, 1.0 / 72.0);

            Assert.AreEqual(Strikes.Length, first.Count);
            AssertSameNotes(first, second);
        }

        [Test]
        public void The_notes_do_not_depend_on_how_often_the_game_asks_for_frames()
        {
            var dto = ReplayOfStrikes(Strikes);

            var at72 = PlayThrough(dto, 1.0 / 72.0);
            var at60 = PlayThrough(dto, 1.0 / 60.0);
            var at20 = PlayThrough(dto, 1.0 / 20.0);
            var uneven = PlayThrough(dto, 1.0 / 72.0, jitter: true);

            AssertSameNotes(at72, at60);
            AssertSameNotes(at72, at20);
            AssertSameNotes(at72, uneven);
        }

        [Test]
        public void A_replay_starts_on_a_bar_line_and_asks_for_each_note_at_the_time_it_had_in_the_recording()
        {
            var dto = ReplayOfStrikes(Strikes);

            var notes = PlayThrough(dto, 1.0 / 72.0);

            var barSec = SlotsPerBar * 60.0 / 96.0 / 4.0;
            var start = notes[0].RequestedAt - notes[0].Timing.CaptureMs / 1000.0;
            Assert.AreEqual(0.0, (start - GridOrigin) / barSec % 1.0, 1e-9, "the recording starts exactly on a bar line");
            foreach (var note in notes) Assert.AreEqual(start + note.Timing.CaptureMs / 1000.0, note.RequestedAt, 1e-9);
        }

        [Test]
        public void A_looping_replay_plays_the_same_notes_again_on_the_next_bar_line()
        {
            var dto = ReplayOfStrikes(Strikes);
            var audio = new FakeAudioEngine { Now = GridOrigin + 0.2 };
            var source = new ReplayHandSource(audio, new ReplayPlayer(dto), "x.json", loop: true);
            var input = NewInput(source);
            var grid = new BeatGrid(GridOrigin, 96, 4);
            input.OnGridReady(grid, SlotsPerBar);

            var intents = new List<NoteIntent>();
            var hardLimit = audio.Now + 3 * (dto.frames[dto.frames.Length - 1].t / 1000.0 + 6.0);
            for (; source.Generation < 2 && audio.Now < hardLimit; audio.Now += 1.0 / 72.0)
            {
                input.Tick(audio.Now);
                while (input.TryDequeue(out var intent)) intents.Add(intent);
            }

            Assert.AreEqual(2 * Strikes.Length, intents.Count, "two passes");
            Assert.GreaterOrEqual(source.Generation, 1);
            for (var i = 0; i < Strikes.Length; i++)
            {
                Assert.AreEqual(intents[i].Part, intents[i + Strikes.Length].Part);
                Assert.AreEqual(intents[i].PitchStep, intents[i + Strikes.Length].PitchStep);
                Assert.AreEqual(intents[i].Velocity, intents[i + Strikes.Length].Velocity);
            }
            var secondStart = intents[Strikes.Length].RequestedAt - intents[Strikes.Length].Timing.CaptureMs / 1000.0;
            var barSec = SlotsPerBar * 60.0 / 96.0 / 4.0;
            Assert.AreEqual(0.0, (secondStart - GridOrigin) / barSec % 1.0, 1e-9);
        }

        [Test]
        public void The_first_play_waits_for_the_configured_delay_and_still_starts_on_a_bar_line()
        {
            const double delaySec = 12.0;
            var dto = ReplayOfStrikes(Strikes);
            var audio = new FakeAudioEngine { Now = GridOrigin + 0.2 };
            var source = new ReplayHandSource(audio, new ReplayPlayer(dto), "x.json", loop: false, firstStartDelaySec: delaySec);
            var input = NewInput(source);
            input.OnGridReady(new BeatGrid(GridOrigin, 96, 4), SlotsPerBar);

            var intents = new List<NoteIntent>();
            var hardLimit = audio.Now + delaySec + dto.frames[dto.frames.Length - 1].t / 1000.0 + 6.0;
            var begun = audio.Now;
            for (; audio.Now < hardLimit; audio.Now += 1.0 / 72.0)
            {
                input.Tick(audio.Now);
                while (input.TryDequeue(out var intent)) intents.Add(intent);
            }

            Assert.AreEqual(Strikes.Length, intents.Count);
            var start = intents[0].RequestedAt - intents[0].Timing.CaptureMs / 1000.0;
            Assert.GreaterOrEqual(start, begun + delaySec, "nothing happens before the delay is over (an idle meadow first)");
            var barSec = SlotsPerBar * 60.0 / 96.0 / 4.0;
            Assert.AreEqual(0.0, (start - GridOrigin) / barSec % 1.0, 1e-9, "and the recording still starts exactly on a bar line");
        }

        [Test]
        public void The_replay_notes_land_on_the_same_grid_slots_every_run()
        {
            var dto = ReplayOfStrikes(Strikes);
            var settings = new SchedulerSettings(0.1, 2, ConductRig.DefaultGaps);
            var grid = new BeatGrid(GridOrigin, 96, 4);

            var runA = SlotsOf(PlayThrough(dto, 1.0 / 72.0), new QuantizedNoteScheduler(grid, settings));
            var runB = SlotsOf(PlayThrough(dto, 1.0 / 60.0, jitter: true), new QuantizedNoteScheduler(grid, settings));

            CollectionAssert.AreEqual(runA, runB);
            Assert.AreEqual(Strikes.Length, runA.Count);
        }

        [Test]
        public void The_shipped_demo_replay_is_valid_and_gives_the_same_strikes_on_every_run()
        {
            var dto = JsonUtility.FromJson<ReplayDto>(System.IO.File.ReadAllText(TestContent.RepoReplayPath));
            Assert.IsEmpty(ReplayValidator.Validate(dto));

            var first = PlayThrough(dto, 1.0 / 72.0);
            var second = PlayThrough(dto, 1.0 / 60.0, jitter: true);

            Assert.GreaterOrEqual(first.Count, 10, "a loop of the demo has about a dozen strikes");
            Assert.GreaterOrEqual(first.Select(n => n.Part).Distinct().Count(), 3, "the demo hand strikes in at least three of the six cells");
            AssertSameNotes(first, second);
        }

        static HandPlayInput NewInput(IHandSource source)
        {
            var range = new PlayableRange();
            range.Set(10);
            var settings = new HandSettingsHolder();
            return new HandPlayInput(source, range, settings, new HandMotionTracker(settings), new HandInterpreter());
        }

        static List<NoteIntent> PlayThrough(ReplayDto dto, double tickSec, bool jitter = false)
        {
            var audio = new FakeAudioEngine { Now = GridOrigin + 0.2 };
            var source = new ReplayHandSource(audio, new ReplayPlayer(dto), "x.json", loop: false);
            var input = NewInput(source);
            input.OnGridReady(new BeatGrid(GridOrigin, 96, 4), SlotsPerBar);

            var intents = new List<NoteIntent>();
            var end = audio.Now + dto.frames[dto.frames.Length - 1].t / 1000.0 + 6.0;
            var n = 0;
            while (audio.Now < end)
            {
                input.Tick(audio.Now);
                while (input.TryDequeue(out var intent)) intents.Add(intent);
                audio.Now += jitter ? tickSec * (0.4 + 1.2 * ((n++ * 7919) % 100) / 100.0) : tickSec;
            }
            return intents;
        }

        static List<long> SlotsOf(List<NoteIntent> intents, QuantizedNoteScheduler scheduler)
        {
            var slots = new List<long>();
            foreach (var intent in intents)
            {
                if (scheduler.TrySchedule(intent.RequestedAt, intent, out var note)) slots.Add(note.Slot);
            }
            return slots;
        }

        static void AssertSameNotes(List<NoteIntent> a, List<NoteIntent> b)
        {
            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Part, b[i].Part, $"note {i} part");
                Assert.AreEqual(a[i].PitchStep, b[i].PitchStep, $"note {i} pitch");
                Assert.AreEqual(a[i].Velocity, b[i].Velocity, $"note {i} velocity");
                Assert.AreEqual(a[i].RequestedAt, b[i].RequestedAt, 1e-12, $"note {i} request time");
            }
        }
    }
}
