using System;
using System.IO;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The whole six-cell piece from the pictures of a tracker that delivers six a second (a 30 a second recording with the pictures dropped, see <see cref="HandTestData.ThinnedTo"/>) to the tutorial that comes back:
    /// an open hand plays in a top cell and another in a bottom cell, both turn into fists and spread apart, the piece ends, the card is written at once, and ten quiet seconds later the tutorial shows.
    /// Wired as the app wires it (<see cref="PerformanceRig"/>); the real scene is in the PlayMode tests.
    /// </summary>
    public class SixCellsEndToEndTests
    {
        const double SlowFps = 6.0;
        const double FastFps = 30.0;
        const float PalmSize = 0.15f;

        // the script, in seconds
        const double PlayFrom = 0.4;
        const double FistsFrom = 8.0;
        const double SpreadFrom = 9.0;
        const double SpreadDone = 9.4;
        const double HandsLeave = 11.0;
        const double ScriptEnd = 24.0;

        static PosedHand Hand(int id, double x, double y, HandPose pose) => PoseHand(id, (float)x, (float)y, PalmSize, pose);

        /// <summary>An open hand going round a circle of a twentieth of the picture (about three palms a second) in the middle of a cell.</summary>
        static PosedHand Circling(int id, Part cell, double t)
        {
            var (x, y) = ConductRig.CentreOf(cell);
            const double Radius = 0.05;
            var omega = 3.0 * PalmSize / Radius;
            return Hand(id, x + Radius * Math.Cos(omega * t), y + Radius * Math.Sin(omega * t), HandPose.Open);
        }

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>The bell (top row, right) and the low string (bottom row, left) are played by an open hand each; then two fists, a palm and a third apart, are pulled apart to six palms; then nobody.</summary>
        static PosedHand[] Script(double t)
        {
            if (t < PlayFrom) return new PosedHand[0];
            if (t < FistsFrom) return new[] { Circling(0, Part.Bell, t), Circling(1, Part.LowString, t) };
            if (t < HandsLeave)
            {
                var apart = Ramp(t, 1.3, 6.0, SpreadFrom, SpreadDone);
                return new[] { Hand(0, 0.5 - apart * PalmSize / 2.0, 0.6, HandPose.Fist), Hand(1, 0.5 + apart * PalmSize / 2.0, 0.6, HandPose.Fist) };
            }
            return new PosedHand[0];
        }

        [Test]
        public void SixCells_EndToEnd_At6Fps()
        {
            var thinned = ReplayOf(Script, ScriptEnd, FastFps).ThinnedTo(SlowFps);
            Assert.That(thinned.frames.Length, Is.InRange((int)(ScriptEnd * SlowFps) - 3, (int)(ScriptEnd * SlowFps) + 3), "six pictures a second");
            var rig = new PerformanceRig();
            var player = new ReplayPlayer(thinned, rig.Hand);

            rig.RunReplayUntil(player, HandsLeave);

            // notes from both parts, one of the top row and one of the bottom row
            var parts = rig.Played.Select(n => n.Part).Distinct().ToList();
            CollectionAssert.IsSupersetOf(parts, new[] { Part.Bell, Part.LowString }, "the open hand of each cell plays its own instrument");
            Assert.IsTrue(rig.Played.Any(n => Parts.IsSky(n.Part)) && rig.Played.Any(n => !Parts.IsSky(n.Part)), "one from the sky row, one from the ground row");
            // the fists play nothing: the only other notes are those of the pictures in which the closing hands, now in the middle of the picture, were not yet known to be fists
            var elsewhere = rig.Played.Select((n, i) => (n, time: rig.NoteTimes[i])).Where(p => p.n.Part != Part.Bell && p.n.Part != Part.LowString).ToList();
            Assert.That(elsewhere.Select(p => p.time), Has.All.LessThanOrEqualTo(FistsFrom + (PoseSettings.DefaultConfirmFrames + 1) / SlowFps), "fists make no notes");
            Assert.AreEqual(1, rig.Started.Count, "one visit");
            Assert.That(rig.Started[0], Is.LessThan(PlayFrom + 1.0), "begun with the first hands");

            // the end
            Assert.AreEqual(1, rig.Cuts.Count, "the piece was ended, once");
            var cutAt = rig.Cuts[0];
            Assert.That(cutAt, Is.InRange(SpreadFrom, SpreadDone + rig.Settings.HoldSec + 3.0 / SlowFps + 0.1));
            Assert.AreEqual(1, rig.Ended.Count, "the visit ended");
            Assert.AreEqual(cutAt, rig.Ended[0], 1e-9, "the card is written at once: SessionEnded comes in the very frame of the cut");
            Assert.AreEqual(new[] { rig.Settings.FadeSec }, rig.Audio.FadeOutAllCalls, "the sound fades out once");
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.IsEmpty(rig.NoteTimes.Where(t => t > cutAt - 1e-9), "not a note after the end");
            Assert.IsFalse(rig.Tutorial.IsVisible, "the tutorial does not show over what has just ended");

            // the quiet that follows: the tutorial comes back after ten seconds
            var quietSec = rig.TutorialSettings.IdleDelaySec;
            rig.RunReplayUntil(player, cutAt + quietSec - 1.0);
            Assert.IsFalse(rig.Tutorial.IsVisible, "not yet");
            rig.RunReplayUntil(player, cutAt + quietSec + rig.TutorialSettings.FadeInSec + 0.5);
            Assert.IsTrue(rig.Tutorial.IsVisible, "ten quiet seconds after the end");
            Assert.AreEqual(0, rig.Tutorial.Step);
            Assert.AreEqual(1, rig.Cuts.Count, "and no second end");
            Assert.AreEqual(1, rig.Started.Count, "no second visit");
        }

        [Test]
        public void TheSameScript_At30Fps_GivesTheSameStory()
        {
            var fast = ReplayOf(Script, ScriptEnd, FastFps);
            var rig = new PerformanceRig();

            rig.RunReplayUntil(new ReplayPlayer(fast, rig.Hand), HandsLeave);

            var parts = rig.Played.Select(n => n.Part).Distinct().ToList();
            CollectionAssert.IsSupersetOf(parts, new[] { Part.Bell, Part.LowString });
            Assert.AreEqual(1, rig.Cuts.Count);
            Assert.AreEqual(rig.Cuts[0], rig.Ended[0], 1e-9);
        }

        [Test]
        public void TheShippedDemoRecording_ThinnedTo6Fps_StillMakesNotes()
        {
            var demo = JsonUtility.FromJson<ReplayDto>(File.ReadAllText(TestContent.RepoReplayPath));
            var thinned = demo.ThinnedTo(SlowFps);
            Assert.That(thinned.frames.Length, Is.InRange(demo.frames.Length / 6 - 3, demo.frames.Length / 4), "about a fifth of the pictures");

            var fast = new PerformanceRig();
            fast.RunReplayUntil(new ReplayPlayer(demo, fast.Hand), 18.0);
            var slow = new PerformanceRig();
            slow.RunReplayUntil(new ReplayPlayer(thinned, slow.Hand), 18.0);

            TestContext.Out.WriteLine($"DEMO-6FPS notes at 30: {fast.Played.Count}, at 6: {slow.Played.Count}; parts at 6: {string.Join("/", slow.Played.Select(n => n.Part).Distinct())}; visits {slow.Started.Count}");
            Assert.GreaterOrEqual(fast.Played.Count, 20, "the recording conducts a piece at 30 a second");
            Assert.GreaterOrEqual(slow.Played.Count, 20, "and at six a second");
            Assert.GreaterOrEqual(slow.Played.Count, fast.Played.Count / 2, "about as much");
            Assert.AreEqual(1, slow.Started.Count, "one visit");
            Assert.IsEmpty(slow.Cuts, "and nothing in it is mistaken for the end");
        }
    }
}
