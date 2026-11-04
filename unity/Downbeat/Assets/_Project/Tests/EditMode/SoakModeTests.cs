using System.Collections.Generic;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SoakModeTests
    {
        [Test]
        public void Soak_ImpliesTheLoopingDemoReplay_Mute_AndGapsThatLetTheMeadowGoIdle()
        {
            var args = LaunchArgs.Parse(new[] { "--soak" });

            Assert.IsTrue(args.Has("soak"));
            Assert.AreEqual("replay", args.Get("input"));
            Assert.AreEqual(ReplayPath.DefaultFile, args.Get("file"));
            Assert.IsTrue(args.Has("mute"), "a night run in somebody's home makes no sound");
            Assert.AreEqual(SoakMode.DefaultGaps, args.Get("replay-gaps"));
            Assert.AreNotEqual("0", args.Get("loop"), "the recording loops (a loop is the default)");
        }

        [Test]
        public void Soak_NeverOverridesWhatWasAskedForExplicitly()
        {
            var args = LaunchArgs.Parse(new[] { "--soak", "--input", "mouse", "--file", "replay/other.json", "--replay-gaps", "2,9", "--loop", "0" });

            Assert.AreEqual("mouse", args.Get("input"));
            Assert.AreEqual("replay/other.json", args.Get("file"));
            Assert.AreEqual("2,9", args.Get("replay-gaps"));
            Assert.AreEqual("0", args.Get("loop"));
        }

        [Test]
        public void WithoutSoak_NothingIsAdded()
        {
            var args = LaunchArgs.Parse(new[] { "--quality", "High" });

            Assert.IsFalse(args.Has("soak"));
            Assert.IsNull(args.Get("input"));
            Assert.IsFalse(args.Has("mute"));
            Assert.IsNull(args.Get("replay-gaps"));
        }

        [Test]
        public void Gaps_ParseAList_AndRepeatInOrder()
        {
            var gaps = ReplayGapSchedule.Parse("1, 5,35");

            Assert.AreEqual(1.0, gaps.GapAfterPlay(1));
            Assert.AreEqual(5.0, gaps.GapAfterPlay(2));
            Assert.AreEqual(35.0, gaps.GapAfterPlay(3));
            Assert.AreEqual(1.0, gaps.GapAfterPlay(4), "and round again");
            Assert.AreEqual(35.0, gaps.GapAfterPlay(6));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("-3")]
        [TestCase("1,,x")]
        [TestCase("NaN")]
        public void Gaps_FallBackToTheOneSecondPause_OnNonsense(string text)
        {
            var gaps = ReplayGapSchedule.Parse(text);

            Assert.AreEqual(ReplayGapSchedule.DefaultPauseSec, gaps.GapAfterPlay(1));
            Assert.AreEqual(ReplayGapSchedule.DefaultPauseSec, gaps.GapAfterPlay(2));
        }

        [Test]
        public void Gaps_KeepTheGoodValuesOfAListWithBadOnes()
        {
            var gaps = ReplayGapSchedule.Parse("4,x,-1,8");

            Assert.AreEqual(4.0, gaps.GapAfterPlay(1));
            Assert.AreEqual(8.0, gaps.GapAfterPlay(2));
            Assert.AreEqual(4.0, gaps.GapAfterPlay(3));
        }

        [Test]
        public void ALoopingReplay_WaitsTheGapOfEachPlay_BeforeStartingAgain()
        {
            const double gridOrigin = 100.0;
            const int slotsPerBar = 16;
            const double firstStartDelay = 0.5;   // ReplayHandSource.FirstStartDelaySec
            const double tick = 1.0 / 72.0;
            var dto = HandTestData.ReplayOfStrikes((0.5f, 0.4f, 7f), (0.3f, 0.5f, 6f));
            var durationSec = dto.frames[dto.frames.Length - 1].t / 1000.0;
            var grid = new BeatGrid(gridOrigin, 96, 4);
            var audio = new FakeAudioEngine { Now = gridOrigin + 0.2 };
            var source = new ReplayHandSource(audio, new ReplayPlayer(dto), "x.json", loop: true, maxPlays: 0, gaps: ReplayGapSchedule.Parse("1,20"));
            source.OnGridReady(grid, slotsPerBar);

            var start = grid.BarTimeAtOrAfter(audio.Now + firstStartDelay, slotsPerBar);
            var restartedAt = new List<double>();
            var generation = source.Generation;
            var frames = new List<HandFrame>();
            while (restartedAt.Count < 3 && audio.Now < gridOrigin + 400.0)
            {
                frames.Clear();
                source.Poll(frames);
                if (source.Generation != generation)
                {
                    generation = source.Generation;
                    restartedAt.Add(audio.Now);
                }
                audio.Now += tick;
            }

            Assert.AreEqual(3, restartedAt.Count);
            var end1 = start + durationSec;
            Assert.AreEqual(end1 + 1.0, restartedAt[0], 0.05, "after the first play: the 1 s gap");
            var start2 = grid.BarTimeAtOrAfter(restartedAt[0] + firstStartDelay, slotsPerBar);
            var end2 = start2 + durationSec;
            Assert.AreEqual(end2 + 20.0, restartedAt[1], 0.05, "after the second play: the 20 s gap");
            var start3 = grid.BarTimeAtOrAfter(restartedAt[1] + firstStartDelay, slotsPerBar);
            var end3 = start3 + durationSec;
            Assert.AreEqual(end3 + 1.0, restartedAt[2], 0.05, "and the list starts again");
        }
    }
}
