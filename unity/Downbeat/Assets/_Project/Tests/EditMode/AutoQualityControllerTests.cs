using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// The automatic quality controller on simulated frame times and tracker rates (docs/windows-runbook.md): the tier is chosen at the start and never climbs while running; it may go down to a cheaper
    /// one once, which waits for an idle meadow while the render scale goes down; within the tier the render scale moves both ways, up only on headroom seen while somebody played, after waits that double;
    /// a starved tracker is only blamed on the app while it answers. Several scenarios are those of a reviewer's port of the controller to Python.
    /// </summary>
    public class AutoQualityControllerTests
    {
        const double Fps = 60.0;
        const double Frame = 1.0 / Fps;

        static readonly MeadowQualityDto[] Tiers =
        {
            new MeadowQualityDto { name = "High", targetFps = 0 },
            new MeadowQualityDto { name = "Low", targetFps = 30 },
        };

        // what one frame costs at each step of the default ladder (ms, a machine that is overloaded at the top and fine at the bottom)
        static readonly float[] OverloadedCost = { 40f, 30f, 24f, 20f, 15f, 12f };
        static readonly float[] BorderlineCost = { 22f, 19f, 24f, 20f, 15f, 12f };
        static readonly float[] LightCost = { 6f, 5f, 4f, 3f, 3f, 2f };

        static AutoQualitySettings Settings() => AutoQualitySettings.From(new AutoQualityDto(), Tiers);

        sealed class Sim
        {
            public readonly AutoQualityController Controller;
            public readonly AutoQualitySettings Settings;
            public readonly List<(double t, QualityChange change)> Changes = new List<(double, QualityChange)>();
            public double Now;
            double _nextHiccup;
            readonly Random _noise;

            public Sim(int startLevel, int seed = 1, AutoQualityDto content = null)
            {
                Settings = content == null ? AutoQualityControllerTests.Settings() : AutoQualitySettings.From(content, Tiers);
                Controller = new AutoQualityController(Settings, startLevel);
                _noise = new Random(seed);
            }

            /// <summary>Runs for <paramref name="seconds"/>. cost: ms a frame costs per ladder step. A frame takes at least the budget of its step (the cap or the vertical sync).</summary>
            public void Run(double seconds, float[] cost, Func<double, double> trackerFps = null, Func<double, bool> idle = null, float noiseMs = 0f, Func<double, double> gpuShare = null,
                bool trackerRunning = false, double hiccupEverySec = 0.0, Func<LadderStep, float> stepCost = null, Func<double, bool> trackerRunningAt = null)
            {
                var end = Now + seconds;
                var trackerDebt = 0.0;
                while (Now < end)
                {
                    var level = LevelInForce();
                    var budget = Settings.BudgetMs(level);
                    var work = (stepCost != null ? stepCost(Controller.Applied) : cost[level]) + (noiseMs > 0 ? (float)((_noise.NextDouble() * 2 - 1) * noiseMs) : 0f);
                    if (hiccupEverySec > 0 && Now >= _nextHiccup)
                    {
                        work += (float)(budget * 2);
                        _nextHiccup = Now + hiccupEverySec;
                    }
                    var frameMs = Math.Max(work, (float)budget);
                    Now += frameMs / 1000.0;
                    double tracker = trackerFps != null ? trackerFps(Now) : 30.0;
                    trackerDebt += tracker * frameMs / 1000.0;
                    var arrived = trackerDebt >= 1.0;
                    if (arrived) trackerDebt -= 1.0;
                    var gpu = gpuShare != null ? gpuShare(Now) * budget : double.NaN;
                    var change = Controller.Update(new QualitySample(Now, frameMs, gpu, trackerRunningAt != null ? trackerRunningAt(Now) : trackerRunning, arrived, idle == null || idle(Now)));
                    if (change != null) Changes.Add((Now, change));
                }
            }

            /// <summary>The ladder step whose cost is in force: the applied tier and scale (the controller's wish may be ahead while a tier waits).</summary>
            int LevelInForce()
            {
                for (var i = 0; i < Settings.Steps; i++)
                {
                    var step = Settings.Step(i);
                    if (step.Tier == Controller.Applied.Tier && Math.Abs(step.RenderScale - Controller.Applied.RenderScale) < 1e-3f) return i;
                }
                // an interim scale of a pending tier change (High at a lower scale than any High step): the closest step of that tier
                var best = 0;
                for (var i = 0; i < Settings.Steps; i++) if (Settings.Step(i).Tier == Controller.Applied.Tier) best = i;
                return best;
            }
        }

        // ---- down ---------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void UnderGpuLoad_ItStepsDownUntilTheFramesFit_AndThenStops()
        {
            var sim = new Sim(0);

            sim.Run(80.0, OverloadedCost);

            Assert.AreEqual(2, sim.Controller.Level, "High@1.0 (40 ms) and High@0.85 (30 ms) are over the 16.7 ms budget, Low@1.0 (24 ms) fits its 33 ms cap");
            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
            Assert.AreEqual(2, sim.Changes.Count);
            StringAssert.Contains("step down", sim.Changes[0].change.Reason);
            StringAssert.Contains("High@1.00", sim.Changes[0].change.Reason);
            Assert.IsTrue(sim.Changes[1].change.TierChanged, "the grass is built again at the tier change");
        }

        [Test]
        public void TheFirstSeconds_ConfirmTheStart_WithAShorterDwell()
        {
            var sim = new Sim(0);

            sim.Run(12.0, OverloadedCost);

            Assert.AreEqual(2, sim.Controller.Level, "two steps down inside the first twelve seconds: the start is checked by the first frames");
            Assert.Less(sim.Changes[0].t, 6.0);
        }

        [Test]
        public void AfterTheConfirmation_AStepDownNeedsTheLongerDwell()
        {
            var sim = new Sim(2);
            sim.Run(30.0, LightCost);

            sim.Run(40.0, new float[] { 40, 40, 80, 80, 80, 80 }); // suddenly a heavy load (a background program)

            Assert.GreaterOrEqual(sim.Changes.Count, 1);
            Assert.GreaterOrEqual(sim.Changes[0].t - 30.0, 1.5, "half of a window of slow frames first, not the first slow frame");
            for (var i = 1; i < sim.Changes.Count; i++) Assert.GreaterOrEqual(sim.Changes[i].t - sim.Changes[i - 1].t, sim.Settings.DownDwellSec - 1e-6, "never two steps within the down dwell");
        }

        [Test]
        public void WhenTheTrackerIsStarved_WhileTheAppIsBusy_ItStepsDown_EvenIfTheFramesAreFine()
        {
            var sim = new Sim(2);

            sim.Run(60.0, LightCost, t => 6.0, trackerRunning: true); // frames fit, but the tracker gets 6 pictures a second

            Assert.Less(sim.Controller.Level, 5 + 1);
            Assert.GreaterOrEqual(sim.Controller.Level, 3, "it stepped down at least once");
            StringAssert.Contains("hand tracker gets", sim.Changes[0].change.Reason);
            Assert.GreaterOrEqual(sim.Changes[0].t, sim.Settings.TrackerStarveSec, "only after it had been starved for the starve time");
        }

        [Test]
        public void ATrackerThatIsFast_OrNotThere_IsNotStarved()
        {
            var fast = new Sim(2);
            var absent = new Sim(2);

            fast.Run(80.0, LightCost, t => 28.0, trackerRunning: true);
            absent.Run(80.0, LightCost, t => 3.0, trackerRunning: false);   // the mouse is the input: no tracker is expected

            Assert.AreEqual(2, fast.Controller.Level);
            Assert.AreEqual(2, absent.Controller.Level);
            Assert.IsEmpty(fast.Changes.Where(c => c.change.Reason.Contains("tracker")));
        }

        [Test]
        public void AStarvedTracker_IsNotBlamedOnTheApp_WhenTheGpuIsMostlyIdle()
        {
            var sim = new Sim(2);

            sim.Run(90.0, LightCost, t => 6.0, trackerRunning: true, gpuShare: t => 0.15); // the GPU needs 15% of the frame: something else holds the tracker back

            Assert.AreEqual(2, sim.Controller.Level);
        }

        // ---- up -----------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void ItStepsUpOnlyAfterALongStretchOfClearHeadroom()
        {
            var sim = new Sim(3);

            sim.Run(85.0, LightCost, idle: t => false);
            Assert.AreEqual(3, sim.Controller.Level, "light frames for 85 s: the up dwell is 90 s");
            sim.Run(15.0, LightCost, idle: t => false);

            Assert.AreEqual(2, sim.Controller.Level, "one step, not a jump");
            StringAssert.Contains("step up", sim.Changes.Last().change.Reason);
        }

        [Test]
        public void AFrameNowAndThenThatIsSlow_KeepsItFromGoingUp()
        {
            var sim = new Sim(3);

            sim.Run(400.0, LightCost, idle: t => false, hiccupEverySec: 3.0);

            Assert.AreEqual(3, sim.Controller.Level, "a slow frame every 3 s is in every 4 s window: there is no clear headroom");
        }

        [Test]
        public void WhereTheGpuTimeIsMeasured_HeadroomMeansTheGpuIsMostlyFree()
        {
            var busy = new Sim(3);
            var free = new Sim(3);

            busy.Run(200.0, LightCost, idle: t => false, gpuShare: t => 0.8);   // the frames are fine but the GPU is busy for 80% of each
            free.Run(200.0, LightCost, idle: t => false, gpuShare: t => 0.3);

            Assert.AreEqual(3, busy.Controller.Level);
            Assert.Less(free.Controller.Level, 3);
        }

        // ---- changing its mind ever more slowly ----------------------------------------------------------------------------------------------

        [Test]
        public void OnANoisyBorderlineMachine_ItChangesItsMindEverMoreSlowly()
        {
            var sim = new Sim(1);

            sim.Run(3600.0, BorderlineCost, idle: t => t % 120.0 > 60.0, noiseMs: 6f);   // an hour at a step that is half the time just over the budget, with a visit of a minute every two

            var changes = sim.Changes;
            for (var i = 1; i < changes.Count; i++) Assert.GreaterOrEqual(changes[i].t - changes[i - 1].t, 3.5, "never two changes in a hurry");
            var ups = changes.Count(c => c.change.Reason.StartsWith("step up"));
            var downs = changes.Count(c => c.change.Reason.StartsWith("step down"));
            Assert.LessOrEqual(ups, 6, $"{ups} steps up and {downs} down in an hour: the dwell doubles with every failed try");
            Assert.LessOrEqual(downs, 7);
            TestContext.Out.WriteLine($"AUTOQUALITY-BORDERLINE hour: {changes.Count} changes ({ups} up, {downs} down), ends at {sim.Controller}, failed probes {sim.Controller.FailedProbes}");
        }

        [Test]
        public void EveryFailedStepUp_DoublesTheWaitBeforeTheNext()
        {
            var up = new Sim(4);   // Low@0.7: fine; Low@0.85 is far too dear (60 ms against 33 ms), so every try up is taken back
            up.Run(7200.0, new float[] { 40f, 40f, 12f, 60f, 12f, 12f }, idle: t => false);

            var ups = up.Changes.Where(c => c.change.Reason.StartsWith("step up")).Select(c => c.t).ToList();
            Assert.GreaterOrEqual(ups.Count, 3, "it keeps trying");
            var gaps = new List<double>();
            for (var i = 1; i < ups.Count; i++) gaps.Add(ups[i] - ups[i - 1]);
            var text = string.Join(", ", gaps.Select(g => g.ToString("0")));
            for (var i = 1; i < gaps.Count; i++) Assert.GreaterOrEqual(gaps[i], Math.Min(gaps[i - 1] * 1.7, up.Settings.MaxUpDwellSec), $"the wait between tries doubles up to its cap: {text}");
        }

        // ---- bounds ---------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void ItStaysInsideTheLadder()
        {
            var bottom = new Sim(5);
            var top = new Sim(0);

            bottom.Run(300.0, new float[] { 90, 90, 90, 90, 90, 90 });   // hopeless: there is nothing cheaper
            top.Run(600.0, LightCost, gpuShare: t => 0.1);

            Assert.AreEqual(5, bottom.Controller.Level);
            Assert.IsEmpty(bottom.Changes);
            Assert.AreEqual(0, top.Controller.Level);
            Assert.IsEmpty(top.Changes);
        }

        [Test]
        public void AStartLevelOutsideTheLadder_IsPulledInside()
        {
            Assert.AreEqual(5, new AutoQualityController(Settings(), 99).Level);
            Assert.AreEqual(0, new AutoQualityController(Settings(), -3).Level);
        }

        // ---- the tier waits for idle ---------------------------------------------------------------------------------------------------------

        [Test]
        public void AChangeOfTier_WaitsForTheMeadowToBeIdle_AndOnlyTheScaleGoesDownInTheMeantime()
        {
            var sim = new Sim(1);
            var idleFrom = 90.0;

            sim.Run(60.0, OverloadedCost, idle: t => t >= idleFrom);   // somebody is playing: the tier must not change

            Assert.AreEqual("High", sim.Controller.Applied.Tier, "still High: nobody rebuilds the grass in the middle of a performance");
            Assert.IsTrue(sim.Controller.TierChangePending);
            Assert.AreEqual(2, sim.Controller.Level, "Low is wanted");
            Assert.IsEmpty(sim.Changes.Where(c => c.change.TierChanged));
            var scaleBefore = sim.Controller.Applied.RenderScale;
            sim.Run(40.0, OverloadedCost, idle: t => t >= idleFrom);   // idle from t=90

            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
            Assert.IsFalse(sim.Controller.TierChangePending);
            var tierChange = sim.Changes.Single(c => c.change.TierChanged);
            Assert.GreaterOrEqual(tierChange.t, idleFrom - 0.1);
            Assert.LessOrEqual(scaleBefore, 0.85f + 1e-4f, "the scale went down first");
            StringAssert.Contains("idle", tierChange.change.Reason);
        }

        [Test]
        public void ASceneThatIsAlreadyIdle_ChangesTheTierAtOnce()
        {
            var sim = new Sim(1);

            sim.Run(30.0, OverloadedCost, idle: t => true);

            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
        }

        // ---- a tier change that waits must not leave a performance on a machine that is too slow (review of A-C, HIGH-1) -------------------------------

        /// <summary>The cost of a frame as the render scale makes it: High 40 ms and Low 20 ms at full scale, in proportion to the pixels.</summary>
        static float ScaledCost(LadderStep step) => (step.Tier == "High" ? 40f : 20f) * step.RenderScale * step.RenderScale;

        [Test]
        public void WhileATierChangeWaits_TheScaleKeepsGoingDownWithinTheTier_ToItsFloor()
        {
            var sim = new Sim(1);   // High@0.85: 29 ms a frame, over its 16.7 ms budget; Low would be wanted, but somebody is playing

            sim.Run(100.0, null, idle: t => t >= 300.0, stepCost: ScaledCost);

            Assert.IsTrue(sim.Controller.TierChangePending);
            Assert.AreEqual("High", sim.Controller.Applied.Tier, "the tier waits for the visitor to leave");
            Assert.Less(sim.Controller.Applied.RenderScale, 0.8f, "but the picture did not stay at the scale that is too dear");
            Assert.GreaterOrEqual(sim.Controller.Applied.RenderScale, (float)sim.Settings.PendingScaleFloor - 1e-4f, "and it stops at the floor");
            Assert.AreEqual(2, sim.Controller.Level, "Low is wanted");
            Assert.AreEqual(1, sim.Controller.AppliedLevel, "but the step in force is still the old one: the health log says so until the tier changes");
            Assert.IsEmpty(sim.Changes.Where(c => c.change.TierChanged));
            var lowered = sim.Changes.Where(c => c.change.Reason.Contains("goes lower")).ToList();
            Assert.GreaterOrEqual(lowered.Count, 1);
            for (var i = 1; i < lowered.Count; i++) Assert.GreaterOrEqual(lowered[i].t - lowered[i - 1].t, sim.Settings.ConfirmDownDwellSec - 1e-6, "one step a dwell, not a plunge");

            sim.Run(250.0, null, idle: t => t >= 300.0, stepCost: ScaledCost);
            Assert.AreEqual("Low", sim.Controller.Applied.Tier, "the visitor left and the tier changes");
            Assert.AreEqual(2, sim.Controller.AppliedLevel, "and now the step in force is the new one");
            Assert.IsFalse(sim.Controller.TierChangePending);
        }

        [Test]
        public void TheScaleOfAPendingTierChange_StopsAtTheFloor_EvenWhenTheFramesStayTooSlow()
        {
            var sim = new Sim(1);

            sim.Run(400.0, new float[] { 90, 90, 90, 90, 90, 90 }, idle: t => false);   // hopeless, and nobody ever leaves

            Assert.AreEqual((float)sim.Settings.PendingScaleFloor, sim.Controller.Applied.RenderScale, 1e-3f);
            Assert.AreEqual("High", sim.Controller.Applied.Tier);
        }

        [Test]
        public void WhenTheRenderScaleIsFixedByHand_APendingTierChangeDoesNotMoveIt_AndTheLogDoesNotClaimOne()
        {
            var settings = AutoQualitySettings.From(new AutoQualityDto(), Tiers, new QualityPins(true, 0));
            var controller = new AutoQualityController(settings, 0);   // High, the scale is the user's
            var now = 0.0;
            var changes = new List<QualityChange>();

            for (var i = 0; i < 90 * 20; i++)   // 90 s of a performance with frames of 50 ms: far too slow
            {
                now += 0.05;
                var change = controller.Update(new QualitySample(now, 50f, double.NaN, false, false, false));
                if (change != null) changes.Add(change);
            }

            Assert.IsTrue(controller.TierChangePending, "it wants Low, and waits for the meadow to be idle");
            Assert.AreEqual(1f, controller.Applied.RenderScale, 1e-4f, "the scale was not touched");
            Assert.IsEmpty(changes.Where(c => c.Reason.Contains("render scale")), "and nothing says it was");
            Assert.AreEqual(0, controller.AppliedLevel);
        }

        // ---- the tier never climbs while running (the simpler Auto) --------------------------------------------------------------------------------

        /// <summary>The reviewer's scenario B: a UHD laptop that starts Low; High would be slow only while somebody plays (a visit of a minute, then a pause of 150 s).</summary>
        [Test]
        public void ALaptopThatStartsLow_NeverTriesHigh_HoweverCheapTheEmptyMeadowIs()
        {
            var sim = new Sim(2);   // Low@1.0
            Func<double, bool> idle = t => t % 210.0 >= 60.0;

            sim.Run(5400.0, null, idle: idle, stepCost: step => step.Tier == "High" ? (idle(sim.Now) ? 10f : 40f) : 10f);   // 90 minutes

            Assert.IsEmpty(sim.Changes.Where(c => c.change.TierChanged), "the grass is never built again");
            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
            Assert.AreEqual(0, sim.Controller.FailedProbes);
            Assert.GreaterOrEqual(sim.Controller.Level, 2, "it never got to High");
        }

        /// <summary>The reviewer's scenario D: a burst of OneDrive for a minute while idle at High@1.0, nobody for 25 minutes, one visitor for a minute, then idle.</summary>
        [Test]
        public void AfterAnOneDriveBurst_TheScaleStaysLowUntilAVisitorPlaysAWhile_AndTheTierNeverComesBack()
        {
            var sim = new Sim(0);
            Func<double, bool> idle = t => !(1500.0 <= t && t < 1560.0);

            sim.Run(2400.0, null, idle: idle, stepCost: step => sim.Now >= 30.0 && sim.Now < 90.0 ? 80f : 5f);

            var ups = sim.Changes.Where(c => c.change.Reason.StartsWith("step up")).Select(c => c.t).ToList();
            Assert.IsNotEmpty(sim.Changes.Where(c => c.change.TierChanged && c.change.Step.Tier == "Low"), "the burst took it down to Low (the idle meadow lets the tier change at once)");
            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
            Assert.IsEmpty(sim.Changes.Where(c => c.change.TierChanged && c.change.Step.Tier == "High"), "and High does not come back in this run");
            Assert.IsEmpty(ups.Where(t => t < 1500.0), "an empty meadow for 25 minutes is no evidence: the scale stays where the burst left it");
            Assert.IsNotEmpty(ups.Where(t => t >= 1500.0), "the visitor played for a minute with headroom: the scale goes up a step");
        }

        /// <summary>The reviewer's scenario E: High@0.85 too slow during a ten-minute visit, then the meadow is idle.</summary>
        [Test]
        public void ATenMinuteVisitOnATooSlowTier_EndsInLow_AndNeverBackInHigh()
        {
            var sim = new Sim(1);

            sim.Run(1800.0, null, idle: t => t > 600.0, stepCost: step => step.Tier == "High" ? 60f * step.RenderScale * step.RenderScale / 0.7225f : 10f);

            Assert.AreEqual("Low", sim.Controller.Applied.Tier);
            Assert.AreEqual(1, sim.Changes.Count(c => c.change.TierChanged), "one change of tier in the whole run");
            Assert.GreaterOrEqual(sim.Changes.Where(c => c.change.TierChanged).Min(c => c.t), 600.0, "it waited for the visitor to leave");
        }

        [Test]
        public void TheTierGoesDownAtMostOncePerRun()
        {
            var threeTiers = new AutoQualityDto { ladder = new[] { new AutoQualityStepDto { tier = "High", renderScale = 1f }, new AutoQualityStepDto { tier = "Low", renderScale = 1f }, new AutoQualityStepDto { tier = "Cheap", renderScale = 1f } } };
            var tiers = new[] { new MeadowQualityDto { name = "High", targetFps = 0 }, new MeadowQualityDto { name = "Low", targetFps = 30 }, new MeadowQualityDto { name = "Cheap", targetFps = 30 } };
            var settings = AutoQualitySettings.From(threeTiers, tiers);
            var controller = new AutoQualityController(settings, 0);
            var changes = new List<QualityChange>();
            var now = 0.0;

            for (var i = 0; i < 600 * 20; i++)   // ten minutes of frames that are far too slow for every tier, with nobody there
            {
                now += 0.05;
                var change = controller.Update(new QualitySample(now, 90f, double.NaN, false, false, true));
                if (change != null) changes.Add(change);
            }

            Assert.AreEqual(1, changes.Count(c => c.TierChanged), "to a cheaper tier once, then no more");
            Assert.AreEqual("Low", controller.Applied.Tier);
        }

        [Test]
        public void AStepUpWithinTheTier_NeedsFramesSeenWhilePerforming_NotOnlyWhileIdle()
        {
            var onlyIdle = new Sim(4);
            var performed = new Sim(4);

            onlyIdle.Run(1800.0, LightCost, idle: t => true);   // half an hour with nobody there: cheap frames prove nothing about a performance
            performed.Run(300.0, LightCost, idle: t => t > 60.0);   // a performance of a minute with headroom, then the meadow is idle

            Assert.AreEqual(4, onlyIdle.Controller.Level);
            Assert.Less(performed.Controller.Level, 4);
            Assert.AreEqual("Low", performed.Controller.Applied.Tier);
        }

        [Test]
        public void APerformanceWithManySlowFrames_IsNoEvidence()
        {
            var clean = new Sim(4);
            var rough = new Sim(4);
            // while somebody plays (the first minute) a slow frame of 80 ms comes every two seconds; from then on the meadow is idle and clean
            Func<Sim, Func<LadderStep, float>> cost = sim => step => sim.Now < 60.0 && Math.Floor(sim.Now) % 2 == 0 && sim.Now - Math.Floor(sim.Now) < 0.05 ? 80f : 5f;

            clean.Run(400.0, null, idle: t => t > 60.0, stepCost: step => 5f);
            rough.Run(400.0, null, idle: t => t > 60.0, stepCost: cost(rough));

            Assert.Less(clean.Controller.Level, 4, "the same minute without the slow frames is evidence");
            Assert.AreEqual(4, rough.Controller.Level, "a few per cent of slow frames in the performance are not");
        }

        [Test]
        public void AStepUpIsNotForgivenByTheClock_OnlyByPlayingAtIt()
        {
            var sim = new Sim(4);   // Low@0.7; Low@0.85 is too dear for the first 400 s (60 ms against 33), fine after
            Func<LadderStep, float> cost = step => step.RenderScale > 0.8f && sim.Now < 400.0 ? 60f : 8f;

            sim.Run(400.0, null, idle: t => false, stepCost: cost);
            Assert.GreaterOrEqual(sim.Controller.FailedProbes, 1, "a try up was taken back");

            var ups = sim.Changes.Count(c => c.change.Reason.StartsWith("step up"));
            while (sim.Changes.Count(c => c.change.Reason.StartsWith("step up")) == ups && sim.Now < 5000.0) sim.Run(1.0, null, idle: t => false, stepCost: cost);
            Assert.Less(sim.Controller.Level, 4, "a step up was made after the wait, and it is fine");
            var afterUp = sim.Controller.FailedProbes;

            sim.Run(7200.0, null, idle: t => true, stepCost: cost);   // two hours of an empty meadow
            Assert.AreEqual(afterUp, sim.Controller.FailedProbes, "no empty hours forgive anything");

            sim.Run(22.0, null, idle: t => false, stepCost: cost);    // but playing at the step for 20 s shows that it holds (and the next step up is made at once: it has its own 20 s to show)
            Assert.AreEqual(afterUp - 1, sim.Controller.FailedProbes);
        }

        [Test]
        public void ThePinnedLadder_StepsOnlyAlongTheTiers()
        {
            var settings = AutoQualitySettings.From(new AutoQualityDto(), Tiers, new QualityPins(true, 0));
            var controller = new AutoQualityController(settings, 0);
            var sim = 0.0;
            QualityChange change = null;

            for (var i = 0; i < 30 * 60 && change == null; i++)
            {
                sim += 0.05;
                change = controller.Update(new QualitySample(sim, 40f, double.NaN, false, false, true));
            }

            Assert.IsNotNull(change);
            Assert.AreEqual("Low", change.Step.Tier, "the first step down is the other tier: no step is wasted on a scale that cannot change");
            Assert.IsTrue(change.TierChanged);
        }

        // ---- a starved tracker is only blamed on the app while it answers (review of A-C, MEDIUM-2 and the verification review) ----------------------

        [Test]
        public void ATrackerThatAnswersTheStepsDown_IsServed_AndTheFramesAreNotGivenUpForItForever()
        {
            var fpsAtLevel = new[] { 30.0, 30.0, 6.0, 11.0, 16.0, 20.0 };   // each step down gives it a good share more
            var sim = new Sim(2);

            sim.Run(100.0, LightCost, t => fpsAtLevel[sim.Controller.Level], trackerRunning: true);

            Assert.AreEqual(4, sim.Controller.Level, "two steps down, and then the tracker is no longer starved");
            Assert.IsFalse(sim.Controller.StarveIgnored);
            Assert.IsTrue(sim.Changes.Any(c => c.change.Reason.Contains("answered the step down")));
        }

        [Test]
        public void ATrackerThatNeverAnswers_IsGivenUp_AndTheFrameTimesAloneBringTheScaleBack()
        {
            var sim = new Sim(2);

            sim.Run(70.0, LightCost, t => 6.0, trackerRunning: true, idle: t => false);   // a camera that delivers 6 pictures a second whatever the app does

            Assert.IsTrue(sim.Controller.StarveIgnored, "after two steps down that brought nothing, the slowness is not the app's");
            Assert.AreEqual(4, sim.Controller.Level, "it paid for the experiment with two steps");
            var note = sim.Changes.Last(c => c.change.Reason.Contains("ignored")).change;
            StringAssert.Contains("not caused by the app", note.Reason);
            StringAssert.Contains("600 s", note.Reason);

            sim.Run(400.0, LightCost, t => 6.0, trackerRunning: true, idle: t => false);   // inside the cooldown (to 70 + 330 s): the frames fit, so the scale climbs back
            Assert.AreEqual(2, sim.Controller.Level, "the frame-time level: nothing holds it down any more");
            Assert.IsTrue(sim.Controller.StarveIgnored);
            Assert.AreEqual(0, sim.Controller.FailedProbes);
        }

        [Test]
        public void AfterTheCooldown_OneMoreExperiment_AndIfItFailsAgainTheCooldownIsTwiceAsLong()
        {
            var sim = new Sim(2);

            sim.Run(70.0 + 600.0 + 300.0, LightCost, t => 6.0, trackerRunning: true, idle: t => false);

            var over = sim.Changes.First(c => c.change.Reason.Contains("cooldown is over")).t;
            var ignores = sim.Changes.Where(c => c.change.Reason.Contains("ignored for")).Select(c => c.change.Reason).ToList();
            Assert.GreaterOrEqual(ignores.Count, 2, "it gave up, tried once more and gave up again");
            StringAssert.Contains("ignored for 600 s", ignores[0]);
            StringAssert.Contains("ignored for 1200 s", ignores[1]);
            Assert.AreEqual(1, sim.Changes.Count(c => c.change.Reason.StartsWith("step down") && c.t > over && c.t < over + 60.0), "one more step down, not two: the count of failures is kept");
        }

        [Test]
        public void TheCooldownGrowsToItsCap_AndNoFurther()
        {
            var sim = new Sim(2);

            sim.Run(5.0 * 3600.0, LightCost, t => 6.0, trackerRunning: true, idle: t => false);

            var periods = sim.Changes.Where(c => c.change.Reason.Contains("ignored for")).Select(c => double.Parse(System.Text.RegularExpressions.Regex.Match(c.change.Reason, @"ignored for (\d+) s").Groups[1].Value)).ToList();
            Assert.GreaterOrEqual(periods.Count, 2);
            for (var i = 1; i < periods.Count; i++) Assert.GreaterOrEqual(periods[i], periods[i - 1], "never shorter");
            Assert.LessOrEqual(periods.Max(), sim.Settings.StarveIgnoreMaxSec, "and never longer than the cap");
        }

        /// <summary>The reviewer's scenario F: the camera gives 6 pictures a second whatever the app does, for 40 minutes, with visits of a minute every three.</summary>
        [Test]
        public void ACameraThatIsSlowByItself_CostsFewExperiments_AndNoRebuildOfTheGrass()
        {
            var sim = new Sim(2);

            sim.Run(2400.0, LightCost, t => 6.0, trackerRunning: true, idle: t => t % 180.0 > 60.0);

            Assert.IsEmpty(sim.Changes.Where(c => c.change.TierChanged), "the grass is not built again for a camera that is slow by itself");
            Assert.LessOrEqual(sim.Changes.Count(c => c.change.Reason.StartsWith("step down") && c.change.Reason.Contains("hand tracker")), 6, "a few experiments in 40 minutes, not one every eleven");
        }

        /// <summary>The reviewer's scenario C: the tracker is away for 6 s inside the 20 s window of an experiment although the step down did help it (8 to 16 pictures a second).</summary>
        [Test]
        public void AnExperimentWhoseWindowHadTheTrackerAway_IsCancelled_NeitherAGainNorAFailure()
        {
            var sim = new Sim(2);

            sim.Run(120.0, LightCost, t => 25.0 < t && t < 31.0 ? 0.0 : (sim.Controller.Level >= 3 ? 16.0 : 8.0), trackerRunning: true, idle: t => false);

            Assert.IsTrue(sim.Changes.Any(c => c.change.Reason.Contains("experiment is cancelled")), "the gap is seen");
            Assert.IsFalse(sim.Changes.Any(c => c.change.Reason.Contains("did not answer")), "and not counted as a failure");
            Assert.IsFalse(sim.Controller.StarveIgnored);
        }

        [Test]
        public void AnExperiment_WhileTheTrackerIsNotRunning_IsCancelled()
        {
            var sim = new Sim(2);

            sim.Run(14.0, LightCost, t => 6.0, trackerRunning: true, idle: t => false);   // the step down is made at about 11 s
            sim.Run(8.0, LightCost, t => 0.0, trackerRunning: false, idle: t => false);   // the tracker is restarted
            sim.Run(30.0, LightCost, t => 14.0, trackerRunning: true, idle: t => false);

            Assert.IsTrue(sim.Changes.Any(c => c.change.Reason.Contains("experiment is cancelled")));
            Assert.IsFalse(sim.Changes.Any(c => c.change.Reason.Contains("did not answer")));
        }

        [Test]
        public void ATransientBurstOfTrackerSlowness_ThatRecovers_CostsOneStepAndNothingMore()
        {
            var sim = new Sim(2);

            sim.Run(250.0, LightCost, t => t > 20.0 && t < 45.0 ? 5.0 : 28.0, trackerRunning: true, idle: t => false);   // OneDrive syncing for 25 s

            Assert.IsFalse(sim.Controller.StarveIgnored);
            Assert.AreEqual(2, sim.Controller.Level, "it stepped down once, saw the tracker recover and the scale climbed back");
            Assert.IsFalse(sim.Changes.Any(c => c.change.Reason.Contains("ignored")));
            Assert.AreEqual(1, sim.Changes.Count(c => c.change.Reason.StartsWith("step down")));
        }

        // ---- cancelled experiments count (review M-1) -------------------------------------

        /// <summary>The camera stalls for 3 s out of every 15 (a hiccup of the camera driver): no pictures, and the tracker reports the camera as lost.</summary>
        static bool CameraStalled(double t) => t % 15.0 >= 10.0 && t % 15.0 < 13.0;

        /// <summary>The hand is lost with the camera: the meadow settles to idle for the stall and the seconds around it.</summary>
        static bool HandLost(double t) => t % 15.0 >= 10.0 || t % 15.0 < 3.0;

        static List<(double t, QualityChange change)> StarvationStepsDown(Sim sim) =>
            sim.Changes.Where(c => !c.change.IsNote && c.change.Reason.Contains("hand tracker gets")).ToList();

        /// <summary>
        /// The reviewer's M-1: a laptop on Auto whose camera gives about 8 pictures a second and stalls for more than 2 s every 10 to 20 s, the GPU time not measured (counted as busy). Every step down for the
        /// starved tracker is an experiment and every stall cancels it, so nothing was ever learnt: it walked High@1.00 down to Low@0.60 within a minute and a half and the give-up never came.
        /// </summary>
        [Test]
        public void ACameraThatStallsEveryFewSeconds_IsGivenUp_NotWalkedDownTheLadderOneCancelledExperimentAfterTheOther()
        {
            var sim = new Sim(0);

            sim.Run(600.0, LightCost, t => CameraStalled(t) ? 0.0 : 8.0, idle: HandLost, trackerRunningAt: t => !CameraStalled(t));

            var gaveUp = sim.Changes.FirstOrDefault(c => c.change.Reason.Contains("ignored for"));
            Assert.IsNotNull(gaveUp.change, $"the starvation is given up after the cancelled experiments: {StarvationStepsDown(sim).Count} steps down for the tracker, ending at {sim.Controller}, and no give-up");
            StringAssert.Contains("ignored for 600 s", gaveUp.change.Reason);
            StringAssert.Contains("cancelled", gaveUp.change.Reason);
            var downs = StarvationStepsDown(sim);
            Assert.LessOrEqual(downs.Count, 6, "three cancelled experiments in a row are one that failed, two of them give up: six experiments at most");
            Assert.IsEmpty(downs.Where(c => c.t > gaveUp.t), "and no more steps down for the tracker while it is ignored");
            var best = sim.Changes.Where(c => !c.change.IsNote && c.t > gaveUp.t).Select(c => c.change.Step.RenderScale).DefaultIfEmpty(0f).Max();
            Assert.GreaterOrEqual(best, 0.85f, "while it is ignored the frame times alone decide, and they bring the scale back up");
        }

        [Test]
        public void ACameraThatKeepsStalling_IsIgnoredForTwiceAsLongTheSecondTime()
        {
            var sim = new Sim(0);

            sim.Run(1800.0, LightCost, t => CameraStalled(t) ? 0.0 : 8.0, idle: HandLost, trackerRunningAt: t => !CameraStalled(t));

            var ignores = sim.Changes.Where(c => c.change.Reason.Contains("ignored for")).ToList();
            Assert.GreaterOrEqual(ignores.Count, 2, "after the cooldown the next experiments are cancelled again, and that is given up again");
            StringAssert.Contains("ignored for 600 s", ignores[0].change.Reason);
            StringAssert.Contains("ignored for 1200 s", ignores[1].change.Reason);
            Assert.GreaterOrEqual(ignores[1].t, ignores[0].t + 600.0, "not before the first cooldown is over");
        }

        /// <summary>Two cancelled experiments, one that ran its course (no gain), then more cancelled ones: only the cancelled ones in a row count, so the give-up comes at the sixth experiment and not at the fourth.</summary>
        [Test]
        public void AnExperimentThatRunsItsCourse_StartsTheCountOfCancelledOnesAgain()
        {
            var oneTier = new AutoQualityDto { ladder = new[] { 1f, 0.95f, 0.9f, 0.85f, 0.8f, 0.75f, 0.7f, 0.65f }.Select(s => new AutoQualityStepDto { tier = "Low", renderScale = s }).ToArray() };
            var sim = new Sim(0, content: oneTier);
            var cancelled = new HashSet<int> { 1, 2, 4, 5, 6 };   // the experiments (the 3rd runs its whole window and brings nothing) whose window has a stall
            bool Stalled(double t)
            {
                var downs = StarvationStepsDown(sim);
                return downs.Count > 0 && cancelled.Contains(downs.Count) && t - downs.Last().t >= 4.0 && t - downs.Last().t < 7.0;
            }

            sim.Run(300.0, null, t => Stalled(t) ? 0.0 : 8.0, idle: t => true, stepCost: step => 3f, trackerRunningAt: t => !Stalled(t));

            var downsMade = StarvationStepsDown(sim);
            var gaveUp = sim.Changes.First(c => c.change.Reason.Contains("ignored for"));
            Assert.AreEqual(6, downsMade.Count(c => c.t < gaveUp.t), "four cancelled ones are not three in a row: the give-up waits for the sixth experiment");
            Assert.Greater(gaveUp.t, downsMade[5].t);
            Assert.IsTrue(sim.Changes.Any(c => c.change.Reason.Contains("did not answer the step down (8.0 -> ")), "the one that ran its course failed on its own");
        }

        // ---- a step up that is taken back soon is a failed try, held or not (review M-2) ------------------------------------------------------------

        /// <summary>
        /// The reviewer's M-2: a machine that holds a step up for longer than the 20 s of evidence and is then over budget (a heavier crowd) took the step back and, since the step had been marked as held,
        /// was never counted as a failed try: the wait stayed at 90 s and the scale changed every two minutes for ever.
        /// </summary>
        [Test]
        public void AStepUpThatHoldsForTheEvidenceTimeAndIsThenTakenBack_StillDoublesTheWait()
        {
            var sim = new Sim(3);   // Low@0.85 is fine; Low@1.00 is fine for the first 25 s after each step up and over the budget after (60 ms against 33)
            float Cost(LadderStep step)
            {
                if (step.Tier != "Low" || step.RenderScale < 0.99f) return 8f;
                var lastUp = sim.Changes.LastOrDefault(c => c.change.Reason.StartsWith("step up"));
                return lastUp.change != null && sim.Now - lastUp.t < 25.0 ? 8f : 60f;
            }

            sim.Run(2.0 * 3600.0, null, idle: t => false, stepCost: Cost);

            var ups = sim.Changes.Where(c => c.change.Reason.StartsWith("step up")).Select(c => c.t).ToList();
            var gaps = new List<double>();
            for (var i = 1; i < ups.Count; i++) gaps.Add(ups[i] - ups[i - 1]);
            var text = string.Join(", ", gaps.Select(g => g.ToString("0")));
            Assert.GreaterOrEqual(ups.Count, 3, "it keeps trying");
            for (var i = 1; i < gaps.Count; i++) Assert.GreaterOrEqual(gaps[i], Math.Min(gaps[i - 1] * 1.7, sim.Settings.MaxUpDwellSec), $"the wait between tries doubles up to its cap: {text}");
            Assert.LessOrEqual(sim.Changes.Count, 20, $"{sim.Changes.Count} changes in two hours (before: one every minute): the tries are {ups.Count}, the waits {text}");
        }

        // ---- the start ------------------------------------------------------------------------------------------------------------------

        [TestCase("Intel(R) UHD Graphics", 1024, 4)]
        [TestCase("Intel(R) Iris(R) Xe Graphics", 4096, 4)]
        [TestCase("Microsoft Basic Render Driver", 0, 4)]
        [TestCase("NVIDIA GeForce RTX 3060", 12288, 0)]
        [TestCase("Intel(R) Arc(TM) A770 Graphics", 16384, 0)]
        [TestCase("Apple M4 Pro", 0, 0)]
        [TestCase("Some Unknown Chip", 1024, 4)]
        [TestCase("Some Unknown Chip", 8192, 0)]
        [TestCase("Some Unknown Chip", 0, 0)]
        public void TheStartStep_ComesFromTheGraphicsDevice(string name, int memoryMb, int expectedStep)
        {
            var step = StartStepPicker.Pick(new GpuInfo(name, memoryMb), null, 6, out var reason);

            Assert.AreEqual(expectedStep, step, reason);
            StringAssert.Contains(name, reason, "the reason is logged with the device");
        }

        [Test]
        public void TheStartStep_NamesComeFromContent()
        {
            var start = new AutoQualityStartDto { weakNames = new[] { "Frobnicator" }, strongNames = new string[0], weakMemoryMb = 0, weakStartStep = 4, strongStartStep = 1 };

            Assert.AreEqual(4, StartStepPicker.Pick(new GpuInfo("Acme Frobnicator 9", 8000), start, 6, out _));
            Assert.AreEqual(1, StartStepPicker.Pick(new GpuInfo("Intel UHD", 8000), start, 6, out _), "the words are the content's, not the code's");
        }

        [Test]
        public void TheBudgetOfAStep_IsTheCapOfItsTier_OrTheVerticalSync()
        {
            var settings = Settings();

            Assert.AreEqual(1000.0 / 60.0, settings.BudgetMs(0), 1e-6, "High has no cap: 60 frames a second");
            Assert.AreEqual(1000.0 / 30.0, settings.BudgetMs(2), 1e-6, "Low is held to 30");
        }
    }
}
