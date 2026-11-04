using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>Which quality mode the command line asks for, and the content blocks that go with automatic quality.</summary>
    public class QualityModeTests
    {
        static QualityMode Mode(bool contentEnables, params string[] args) => QualityMode.Resolve(LaunchArgs.Parse(Join(args)), contentEnables);

        static string[] Join(string[] args)
        {
            var all = new List<string> { "Downbeat" };
            all.AddRange(args);
            return all.ToArray();
        }

        [Test]
        public void WithoutQuality_ItIsAutomatic()
        {
            var mode = Mode(true);

            Assert.IsTrue(mode.IsAuto);
            Assert.IsNull(mode.LockedTier);
        }

        [TestCase("Auto")]
        [TestCase("auto")]
        public void QualityAuto_TurnsItOn(string value) => Assert.IsTrue(Mode(true, "--quality", value).IsAuto);

        [TestCase("High")]
        [TestCase("Low")]
        public void AnExplicitTier_LocksIt_AndTurnsAutomaticOff(string tier)
        {
            var mode = Mode(true, "--quality", tier);

            Assert.IsFalse(mode.IsAuto);
            Assert.AreEqual(tier, mode.LockedTier);
            StringAssert.Contains("locks", mode.Reason);
        }

        [Test]
        public void ANumberFixedByHand_PinsOnlyThatLever_AndTheTierIsStillPicked()
        {
            var scale = Mode(true, "--render-scale", "0.5");
            var fps = Mode(true, "--max-fps", "30");
            var both = Mode(true, "--render-scale", "0.5", "--max-fps", "30");

            Assert.IsTrue(scale.IsAuto, "-RenderScale alone must not lock the tier to the first one (High)");
            Assert.IsTrue(scale.Pins.RenderScale);
            Assert.AreEqual(0, scale.Pins.MaxFps);
            Assert.IsTrue(fps.IsAuto, "-MaxFps alone must not lock the tier to the first one (High)");
            Assert.IsFalse(fps.Pins.RenderScale);
            Assert.AreEqual(30, fps.Pins.MaxFps);
            Assert.IsTrue(both.IsAuto);
            Assert.IsTrue(both.Pins.RenderScale && both.Pins.MaxFps == 30);
            Assert.IsFalse(Mode(true).Pins.Any);
            StringAssert.Contains("--max-fps", fps.Reason);
        }

        [Test]
        public void ANumberFixedByHand_WithAnExplicitTier_LeavesNothingAutomatic()
        {
            Assert.IsFalse(Mode(true, "--quality", "Low", "--max-fps", "30").IsAuto);
            Assert.IsFalse(Mode(true, "--quality", "High", "--render-scale", "0.7").IsAuto);
        }

        [Test]
        public void APinnedScale_LeavesOneStepPerTier_AndAPinnedFrameCap_IsTheBudgetOfEveryStep()
        {
            var content = Shipped();

            var scalePinned = AutoQualitySettings.From(content.autoQuality, content.meadow.quality, new QualityPins(true, 0));
            var fpsPinned = AutoQualitySettings.From(content.autoQuality, content.meadow.quality, new QualityPins(false, 30));

            Assert.AreEqual(2, scalePinned.Steps, "High and Low: the scale is no lever");
            Assert.AreEqual("High", scalePinned.Step(0).Tier);
            Assert.AreEqual("Low", scalePinned.Step(1).Tier);
            Assert.AreEqual(1, scalePinned.LevelOfTier("Low"));
            Assert.AreEqual(-1, scalePinned.LevelOfTier("Ultra"));
            Assert.AreEqual(6, fpsPinned.Steps, "the scale is still a lever");
            for (var i = 0; i < fpsPinned.Steps; i++) Assert.AreEqual(1000.0 / 30.0, fpsPinned.BudgetMs(i), 1e-6, "30 frames a second is what the screen is held to");
        }

        [Test]
        public void TheFrameInWhichAHandFirstAppears_IsNotIdle_SoNoRebuildHitchesThen()
        {
            Assert.IsTrue(QualityIdle.Is(ExperienceState.Idle, 0, false));
            Assert.IsFalse(QualityIdle.Is(ExperienceState.Idle, 1, false), "the state of the visit has not caught up with the hand yet");
            Assert.IsFalse(QualityIdle.Is(ExperienceState.Idle, 0, true), "a note from the mouse or the keyboard");
            Assert.IsFalse(QualityIdle.Is(ExperienceState.Waking, 0, false));
            Assert.IsFalse(QualityIdle.Is(ExperienceState.Playing, 2, true));
            Assert.IsFalse(QualityIdle.Is(ExperienceState.Settling, 0, false));
        }

        [Test]
        public void TheContent_CanSwitchItOff()
        {
            Assert.IsFalse(Mode(false).IsAuto);
            StringAssert.Contains("autoQuality.enabled", Mode(false).Reason);
        }

        // ---- the content: tier levers and the autoQuality block ---------------------------------------------------------------------------------

        static ContentDto Shipped() => UnityEngine.JsonUtility.FromJson<ContentDto>(System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "..", "..", "content", "content.json"))));

        [Test]
        public void TheShippedContent_IsValid_WithTheLeversAndTheAutoBlock()
        {
            var content = Shipped();

            Assert.IsEmpty(ContentValidator.Validate(content));
            Assert.IsNotNull(content.autoQuality);
            var low = System.Linq.Enumerable.First(content.meadow.quality, t => t.name == "Low");
            Assert.AreEqual(0.7f, low.renderScale, 1e-4f);
            Assert.AreEqual(30, low.targetFps, "Low is held to 30 frames a second");
            Assert.AreEqual("Quarter", low.bloom.downscale);
        }

        [Test]
        public void ATierLever_OutsideItsRange_IsRefused()
        {
            void Refuses(System.Action<MeadowQualityDto> change, string name)
            {
                var content = Shipped();
                change(content.meadow.quality[1]);
                Assert.That(ContentValidator.Validate(content), Has.Some.Contains(name), name);
            }

            Refuses(t => t.renderScale = 0.2f, "renderScale");
            Refuses(t => t.renderScale = 1.5f, "renderScale");
            Refuses(t => t.upscaler = "Lanczos", "upscaler");
            Refuses(t => t.msaa = 3, "msaa");
            Refuses(t => t.targetFps = 5, "targetFps");
            Refuses(t => t.targetFps = 500, "targetFps");
            Refuses(t => t.skyDetail = 2f, "skyDetail");
            Refuses(t => t.bloom.filter = "Box", "bloom.filter");
            Refuses(t => t.bloom.downscale = "Eighth", "bloom.downscale");
            Refuses(t => t.bloom.maxIterations = 40, "bloom.maxIterations");
        }

        [Test]
        public void TheAutoQualityBlock_IsChecked()
        {
            void Refuses(System.Action<AutoQualityDto> change, string name)
            {
                var content = Shipped();
                change(content.autoQuality);
                Assert.That(ContentValidator.Validate(content), Has.Some.Contains(name), name);
            }

            Refuses(a => a.windowSec = 0f, "windowSec");
            Refuses(a => a.headroomShare = 1.2f, "headroomShare");
            Refuses(a => a.trackerMinFps = 0f, "trackerMinFps");
            Refuses(a => a.maxUpDwellSec = 5f, "maxUpDwellSec");
            Refuses(a => a.upHoldSec = -1f, "upHoldSec");
            Refuses(a => a.upHoldSec = 4000f, "upHoldSec");
            Refuses(a => a.confirmDownDwellSec = a.downDwellSec + 1f, "confirmDownDwellSec");
            Refuses(a => a.ladder[0].tier = "Ultra", "'Ultra' is not a tier");
            Refuses(a => a.ladder[1].renderScale = 0.1f, "renderScale");
            Refuses(a => a.ladder = new[] { a.ladder[0] }, "2..12 steps");
            Refuses(a => a.start.weakStartStep = 99, "weakStartStep");
            Refuses(a => a.pendingScaleFloor = 0.1f, "pendingScaleFloor");
            Refuses(a => a.pendingScaleStep = 0f, "pendingScaleStep");
            Refuses(a => a.performingEvidenceSec = -1f, "performingEvidenceSec");
            Refuses(a => a.starveGainShare = 0f, "starveGainShare");
            Refuses(a => a.starveGainWindowSec = 0f, "starveGainWindowSec");
            Refuses(a => a.starveGiveUpAfter = 0, "starveGiveUpAfter");
            Refuses(a => a.starveCancelsAsFailure = 0, "starveCancelsAsFailure");
            Refuses(a => a.starveCancelsAsFailure = 11, "starveCancelsAsFailure");
            Refuses(a => a.starveIgnoreSec = 1f, "starveIgnoreSec");
        }

        [Test]
        public void AWeakLaptop_StartsAtLowAtTheScaleTheMemoMeasured()
        {
            var content = Shipped();
            var settings = AutoQualitySettings.From(content.autoQuality, content.meadow.quality);

            var step = StartStepPicker.Pick(new GpuInfo("Intel(R) UHD Graphics", 1024), content.autoQuality.start, settings.Steps, out _);

            Assert.AreEqual("Low", settings.Step(step).Tier);
            Assert.AreEqual(0.7f, settings.Step(step).RenderScale, 1e-4f, "the weak laptop of the memo ran Low at 0.7");
        }

        [Test]
        public void ContentWithoutTheAutoBlock_UsesTheBuiltInLadder()
        {
            var content = Shipped();
            content.autoQuality = null;

            Assert.IsEmpty(ContentValidator.Validate(content));
            var settings = AutoQualitySettings.From(content.autoQuality, content.meadow.quality);
            Assert.AreEqual(6, settings.Steps);
            Assert.AreEqual("Low", settings.Step(2).Tier);
        }
    }
}
