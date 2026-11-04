using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downbeat.App;
using Downbeat.Content;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// The tutorial as the app runs it, without a scene: the coordinator on the overlay layer of a camera, fed the hands and the flow of a visit by the test and a clock of its own, so that ten seconds take no time.
    /// What the scene adds (the real state machine, the real hands, the real notes) is in <see cref="TutorialAppPlayModeTests"/>.
    /// </summary>
    public class TutorialCoordinatorPlayModeTests
    {
        const double Dt = 1.0 / 30.0;
        const float Strip = 0.28f;
        const float Left = 0.17f, Middle = 0.5f, Top = 0.25f;
        const double PictureMs = 100.0;

        sealed class Hands : IHandMotionView
        {
            readonly List<HandMotion> _hands = new List<HandMotion>();
            public long FrameSeq { get; private set; }
            public int Count => _hands.Count;
            public HandMotion Hand(int index) => _hands[index];
            public bool TryGetById(int id, out HandMotion motion)
            {
                foreach (var hand in _hands)
                {
                    if (hand.Id != id) continue;
                    motion = hand;
                    return true;
                }
                motion = default;
                return false;
            }

            /// <summary>A new picture of the hands: each one is stamped with the time of the picture, as the tracker's are (a picture with the time of the one before is the same picture again).</summary>
            public void Show(params HandMotion[] hands)
            {
                FrameSeq++;
                _hands.Clear();
                foreach (var hand in hands)
                {
                    _hands.Add(new HandMotion(hand.Id, hand.IsTracked, hand.Score, hand.X, hand.Y, hand.Size, hand.HasVelocity, hand.VelocityX, hand.VelocityY, hand.SpeedPalms, hand.DownSpeedPalms,
                        FrameSeq * PictureMs, hand.Epoch, hand.Pose, hand.CanPlay));
                }
            }
        }

        static HandMotion Hand(int id, float x, float y, HandPose pose) =>
            new HandMotion(id, true, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, pose);

        sealed class Rig : IDisposable
        {
            readonly GameObject _cameraObject;
            public readonly MeadowOverlayLayer Overlay;
            public readonly TutorialCoordinator Coordinator;
            public readonly Hands Hands = new Hands();
            public double Now;
            public ExperienceState State = ExperienceState.Idle;
            public double IdleSince;
            public bool InputNote;

            public Rig(ContentDto content = null, ContentBundle bundle = null)
            {
                _cameraObject = new GameObject("tutorial coordinator camera");
                var camera = _cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                Overlay = MeadowOverlayLayer.Create(camera);
                Overlay.StripFraction = Strip;
                Coordinator = new TutorialCoordinator(content ?? Shipped(), Overlay, bundle);
            }

            public TutorialView View => Coordinator.View;
            public TutorialFrame Frame => Coordinator.Frame;

            public void RunTo(double until)
            {
                while (Now + Dt < until + 1e-9)
                {
                    Now += Dt;
                    Coordinator.Tick(Now, State, IdleSince, Hands, InputNote, Now);
                }
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_cameraObject);
            }
        }

        static ContentDto Shipped() => JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        [Test]
        public void Appears_TenSecondsAfterStart_OnTheClockOfTheApp()
        {
            using (var rig = new Rig())
            {
                rig.Now = 1234.5;   // the audio clock does not start at 0
                rig.RunTo(1234.5 + 9.5);
                Assert.IsFalse(rig.View.IsShown, "not yet");

                rig.RunTo(1234.5 + 11.0);

                Assert.IsTrue(rig.View.IsShown);
                Assert.AreEqual(0, rig.Frame.Step);
                Assert.AreEqual("손을 펴서 들어 보세요", rig.View.CaptionText);
                Assert.AreEqual(1, rig.View.OpenHandCount, "the open hand breathes in the middle");
            }
        }

        [Test]
        public void Appears_TenSecondsAfterTheVisitEnds_AndGoesRoundTheThreeSteps()
        {
            using (var rig = new Rig())
            {
                rig.State = ExperienceState.Playing;
                rig.RunTo(60.0);
                Assert.IsFalse(rig.View.IsShown, "somebody is playing");
                rig.State = ExperienceState.Idle;
                rig.IdleSince = 60.0;

                rig.RunTo(69.5);
                Assert.IsFalse(rig.View.IsShown, "the visit ended ten seconds ago, not yet");
                rig.RunTo(72.5);
                Assert.AreEqual(0, rig.Frame.Step);
                rig.RunTo(77.5);
                Assert.AreEqual(1, rig.Frame.Step);
                Assert.AreEqual(6, rig.View.VisibleLabelCount, "the cells with their names");
                rig.RunTo(82.5);
                Assert.AreEqual(2, rig.Frame.Step);
                Assert.AreEqual(2, rig.View.FistCount);
                rig.RunTo(87.5);
                Assert.AreEqual(0, rig.Frame.Step, "round again");
            }
        }

        [Test]
        public void NeverShows_WhileSomebodyIsPlaying()
        {
            using (var rig = new Rig())
            {
                foreach (var state in new[] { ExperienceState.Waking, ExperienceState.Playing, ExperienceState.Settling })
                {
                    rig.State = state;
                    rig.RunTo(rig.Now + 40.0);
                    Assert.IsFalse(rig.View.IsShown, state.ToString());
                }
            }
        }

        [Test]
        public void OpenHand_FadesItOut_InUnderASecond()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(13.0);
                Assert.IsTrue(rig.View.IsShown);

                rig.Hands.Show(Hand(0, Middle, Top, HandPose.Open));
                rig.RunTo(13.4);
                Assert.IsTrue(rig.View.IsShown, "it is fading");
                Assert.Less(rig.View.DimAlpha, 0.3f, "the veil is going");
                rig.RunTo(14.0);

                Assert.IsFalse(rig.View.IsShown, "gone before a second is out");
            }
        }

        [Test]
        public void AMouseNote_FadesItOutToo()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(13.0);
                rig.InputNote = true;
                rig.RunTo(13.05);
                rig.InputNote = false;
                rig.RunTo(14.0);

                Assert.IsFalse(rig.View.IsShown);
            }
        }

        [Test]
        public void TwoFists_DoNotFadeIt_AndTickTheThirdStep()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(21.0);   // the third step: 20 to 25 s
                Assert.AreEqual(2, rig.Frame.Step);
                Assert.AreEqual(0, rig.View.TickedDotCount);

                rig.Hands.Show(Hand(0, 0.35f, Top, HandPose.Fist), Hand(1, 0.65f, Top, HandPose.Fist));
                rig.RunTo(23.0);

                Assert.IsTrue(rig.View.IsShown, "fists do not wake the meadow and do not fade the tutorial");
                Assert.IsTrue(rig.Frame.IsChecked(2));
                Assert.AreEqual(1, rig.View.TickedDotCount, "the dot of the third step is a tick");
                Assert.AreEqual(1, rig.View.VisibleBadgeCount, "and the large tick is there");
                Assert.AreEqual(2, rig.Frame.Step, "the check does not hurry the next step");
            }
        }

        [Test]
        public void OneFist_DoesNotFadeIt_AndTicksNothing()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(21.0);
                rig.Hands.Show(Hand(0, 0.35f, Top, HandPose.Fist));
                rig.RunTo(24.0);

                Assert.IsTrue(rig.View.IsShown);
                Assert.IsFalse(rig.Frame.IsChecked(2));
            }
        }

        [Test]
        public void AHandMovingToAnotherCell_TicksTheSecondStep()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(16.0);   // the second step: 15 to 20 s
                Assert.AreEqual(1, rig.Frame.Step);
                rig.Hands.Show(Hand(0, Left, Top, HandPose.Unknown));
                rig.RunTo(16.5);
                Assert.IsFalse(rig.Frame.IsChecked(1), "arriving is not moving");

                rig.Hands.Show(Hand(0, Middle, Top, HandPose.Unknown));   // a hand that is half open plays but does not fade the tutorial
                rig.RunTo(17.0);

                Assert.IsTrue(rig.Frame.IsChecked(1));
                Assert.AreEqual(1, rig.Frame.Step);
                Assert.IsTrue(rig.View.IsShown);
            }
        }

        [Test]
        public void TheView_HangsUnderTheOverlay_WithSortingOrdersAboveTheRecordStrip()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(21.0);
                rig.Hands.Show(Hand(0, 0.35f, Top, HandPose.Fist), Hand(1, 0.65f, Top, HandPose.Fist));
                rig.RunTo(23.0);

                Assert.AreSame(rig.Overlay.Root, rig.View.transform.parent, "it is a child of the overlay layer");
                var renderers = rig.View.GetComponentsInChildren<Renderer>(true);
                Assert.Greater(renderers.Length, 20);
                var lowest = renderers.Min(r => r.sortingOrder);
                var highest = renderers.Max(r => r.sortingOrder);
                Assert.GreaterOrEqual(lowest, rig.Overlay.SortingOrderBase, "over the record strip");
                Assert.LessOrEqual(highest, rig.Overlay.SortingOrderBase + 9, "and below the cell guide (+10), the trail (+20) and the word of the end (+30)");
            }
        }

        [Test]
        public void TheView_KeepsClearOfTheStrip_AlsoWhenTheStripIsHiddenWithTab()
        {
            using (var rig = new Rig())
            {
                rig.RunTo(13.0);
                Assert.AreEqual(Strip, rig.View.OverlayFraction, 1e-6f);

                rig.Overlay.StripFraction = 0f;   // what DownbeatApp does when Tab hides the strip
                rig.RunTo(13.1);

                Assert.AreEqual(0f, rig.View.OverlayFraction, 1e-6f);
            }
        }

        static Texture2D Picture(string file)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "tutorial", file))), false), file);
            return texture;
        }

        [Test]
        public void TheHands_ArePicturesOfTheBundle_WhenTheContentLoadedThem()
        {
            var content = Shipped();
            var pictures = new Dictionary<string, Texture2D> { { content.tutorial.openHandImage, Picture("hand-open.png") }, { content.tutorial.fistImage, Picture("hand-fist.png") } };
            var bundle = new ContentBundle(content, "file:///content/", pictures);
            try
            {
                using (var rig = new Rig(content, bundle))
                {
                    Assert.IsTrue(rig.View.OpenHandIsPicture);
                    Assert.IsTrue(rig.View.FistIsPicture);
                }
            }
            finally
            {
                bundle.Release();
            }
        }

        [Test]
        public void ThePictureThatTheContentCouldNotLoad_IsDrawnInCode_TheOtherOneIsAPicture()
        {
            var content = Shipped();
            var pictures = new Dictionary<string, Texture2D> { { content.tutorial.openHandImage, Picture("hand-open.png") } };   // the fist is not in the bundle
            var bundle = new ContentBundle(content, "file:///content/", pictures);
            try
            {
                using (var rig = new Rig(content, bundle))
                {
                    Assert.IsTrue(rig.View.OpenHandIsPicture);
                    Assert.IsFalse(rig.View.FistIsPicture);
                    rig.RunTo(21.0);
                    Assert.AreEqual(2, rig.View.FistCount, "the third step still shows two fists");
                }
            }
            finally
            {
                bundle.Release();
            }
        }

        [Test]
        public void WithoutABundle_BothHandsAreDrawnInCode()
        {
            using (var rig = new Rig())
            {
                Assert.IsFalse(rig.View.OpenHandIsPicture);
                Assert.IsFalse(rig.View.FistIsPicture);
            }
        }

        [Test]
        public void ABrokenTutorialBlock_IsNotTheCoordinatorsBusiness_TheDefaultsApplyWhenItIsMissing()
        {
            var content = Shipped();
            content.tutorial = null;

            using (var rig = new Rig(content))
            {
                rig.RunTo(10.5);
                Assert.IsTrue(rig.View.IsShown, "a content file without the block gets the ten seconds");
            }
        }
    }
}
