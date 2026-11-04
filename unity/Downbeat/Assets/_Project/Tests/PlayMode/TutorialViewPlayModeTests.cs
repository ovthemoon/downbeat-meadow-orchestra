using System;
using System.Collections;
using System.IO;
using System.Linq;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// The tutorial view built under a dummy camera, as the meadow's overlay layer will carry it: every step is built and drawn without an error, the right glyphs show in each, nothing leaves the screen above the record strip,
    /// and what the view made goes with it. Pictures are drawn into a render texture and the first run of each kind is written to captures/visual.
    /// </summary>
    public class TutorialViewPlayModeTests
    {
        const int Width = 960;
        const int Height = 540;
        const float OverlayFraction = 0.28f;
        const double Clock = 3000.0;
        const int GoldPixelsOfAHand = 1000;   // the hands are thin glowing outlines: a still of 960 x 540 shows about two thousand gold pixels of them, the texts none

        static readonly string[] Labels = { "하프", "리코더", "종", "낮은 현", "마림바", "손북" };
        static readonly Color[] Colours =
        {
            new Color32(0xF2, 0xC4, 0x6B, 255), new Color32(0xF4, 0xF1, 0xE6, 255), new Color32(0xBF, 0xE3, 0xFF, 255),
            new Color32(0xC9, 0xA2, 0x7A, 255), new Color32(0xE2, 0x45, 0x3A, 255), new Color32(0xE8, 0xA0, 0x4A, 255),
        };
        static readonly string[] Fonts = { "Apple SD Gothic Neo", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR" };

        sealed class Rig : IDisposable
        {
            readonly GameObject _cameraObject;
            readonly RenderTexture _target;
            public readonly Camera Camera;
            public readonly TutorialView View;
            public readonly TutorialSettings Settings = TutorialSettings.From(new TutorialDto());

            public Rig(float overlayFraction = OverlayFraction, Vector3? shift = null, Texture2D openHandPicture = null, Texture2D fistPicture = null)
            {
                _cameraObject = new GameObject("tutorial test camera");
                Camera = _cameraObject.AddComponent<Camera>();
                Camera.fieldOfView = 50f;
                Camera.nearClipPlane = 0.1f;
                Camera.farClipPlane = 50f;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color(0.25f, 0.45f, 0.2f, 1f);
                _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Camera.targetTexture = _target;
                Camera.enabled = false;
                var offset = shift ?? Vector3.zero;
                View = TutorialView.Create(_cameraObject.transform, (vx, vy) => LocalPoint(vx, vy) + offset, overlayFraction, Settings, Labels, Colours, Fonts, 100, openHandPicture, fistPicture);
            }

            /// <summary>A point of the picture on the plane one unit in front of the camera, as the overlay layer gives it.</summary>
            public Vector3 LocalPoint(float vx, float vy)
            {
                var halfHeight = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var halfWidth = halfHeight * Width / Height;
                return new Vector3((vx - 0.5f) * 2f * halfWidth, (vy - 0.5f) * 2f * halfHeight, 1f);
            }

            public Texture2D Picture()
            {
                var previous = RenderTexture.active;
                try
                {
                    Camera.Render();
                    RenderTexture.active = _target;
                    var picture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                    picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    picture.Apply();
                    return picture;
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>The picture rectangle (viewport, y up) that a renderer covers.</summary>
            public Rect ViewportRectOf(Renderer renderer)
            {
                var b = renderer.bounds;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    // the four corners on the plane the quads are on (the box of a quad has a little depth, which would make it look larger than it is)
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, 0));
                    var v = Camera.WorldToViewportPoint(corner);
                    minX = Mathf.Min(minX, v.x);
                    maxX = Mathf.Max(maxX, v.x);
                    minY = Mathf.Min(minY, v.y);
                    maxY = Mathf.Max(maxY, v.y);
                }
                return Rect.MinMaxRect(minX, minY, maxX, maxY);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_cameraObject);
                UnityEngine.Object.DestroyImmediate(_target);
            }
        }

        static TutorialFrame Frame(float alpha, int step, float progress, bool check0 = false, bool check1 = false, bool check2 = false) =>
            new TutorialFrame(alpha, step, progress, check0, check1, check2);

        [Test]
        public void EveryStep_IsBuiltAndDrawn_WithoutAnError()
        {
            using (var rig = new Rig())
            {
                foreach (var step in new[] { 0, 1, 2 })
                {
                    foreach (var progress in new[] { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1f })
                    {
                        rig.View.Show(Frame(1f, step, progress, step == 0, step == 1, step == 2), Clock + step * 5 + progress * 5);
                        var picture = rig.Picture();
                        UnityEngine.Object.DestroyImmediate(picture);
                    }
                }
                Assert.IsTrue(rig.View.IsShown);
            }
        }

        [Test]
        public void StepOne_ShowsAnOpenHand_ThatBreathes_WithItsLine_AndNoNames()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 0, 0.5f), Clock);

                Assert.AreEqual(1, rig.View.OpenHandCount);
                Assert.AreEqual(0, rig.View.FistCount);
                Assert.AreEqual(rig.Settings.Texts[0], rig.View.CaptionText);
                Assert.AreEqual(0, rig.View.VisibleLabelCount, "the names belong to the second step");
                Assert.IsFalse(rig.View.EndWordShown);

                var hand = rig.View.transform.Find("OpenHand");
                rig.View.Show(Frame(1f, 0, 0.5f), Clock);
                var small = hand.localScale.y;
                rig.View.Show(Frame(1f, 0, 0.5f), Clock + TutorialChoreography.BreathPeriodSec * 0.5);
                Assert.Greater(hand.localScale.y, small * 1.05f, "half a breath later the hand is bigger");
            }
        }

        [Test]
        public void StepTwo_ShowsTheSixNamesInTheirCells_AndTheHandAmongThem()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 1, 0.5f), Clock);

                Assert.AreEqual(1, rig.View.OpenHandCount);
                Assert.AreEqual(6, rig.View.VisibleLabelCount);
                for (var cell = 0; cell < 6; cell++) Assert.AreEqual(Labels[cell], rig.View.LabelText(cell));
                Assert.AreEqual(rig.Settings.Texts[1], rig.View.CaptionText);

                // the top-left name is in the top-left cell, the bottom-right one in the bottom-right
                var topLeft = rig.View.transform.Find("Label0").position;
                var bottomRight = rig.View.transform.Find("Label5").position;
                Assert.Less(topLeft.x, bottomRight.x);
                Assert.Greater(topLeft.y, bottomRight.y);
            }
        }

        [Test]
        public void StepTwo_TheRowsOfTheGridMeetAtTheHorizon_AndTheHandWalksInTheMiddleOfEachCell()
        {
            const float Horizon = 0.5f;   // not the middle of the free picture (0.64): the grid follows the horizon it is given

            using (var rig = new Rig())
            {
                rig.View.HorizonViewport = Horizon;
                var hand = rig.View.transform.Find("OpenHand");
                var viewportOf = new System.Func<Transform, Vector3>(t => rig.Camera.WorldToViewportPoint(t.position));

                rig.View.Show(Frame(1f, 1, 0.05f), Clock);
                var topRowName = viewportOf(rig.View.transform.Find("Label0")).y;
                var bottomRowName = viewportOf(rig.View.transform.Find("Label3")).y;
                var firstCell = CellGuideLayout.Cell(0, OverlayFraction, Horizon);
                var inTheFirstCell = viewportOf(hand);
                rig.View.Show(Frame(1f, 1, 0.55f), Clock);
                var fourthStop = CellGuideLayout.Cell(5, OverlayFraction, Horizon);   // the walk goes 0 1 2 5 4 3: the fourth stop is the hand drum's cell
                var inTheFourthStop = viewportOf(hand);

                Assert.Greater(topRowName, Horizon, "the names of the top row are in the sky above the horizon");
                Assert.Less(bottomRowName, Horizon, "and the names of the bottom row on the ground under it");
                Assert.That(inTheFirstCell.y, Is.InRange(firstCell.YMin, firstCell.YMax), "the hand starts inside the first cell, in the sky row");
                Assert.That(inTheFourthStop.y, Is.InRange(fourthStop.YMin, fourthStop.YMax), "and walks down into the fourth stop, in the ground row");
                Assert.AreEqual(fourthStop.CenterX, inTheFourthStop.x, 0.01f);
                Assert.Less(inTheFourthStop.y, fourthStop.CenterY, "a little under the middle of the cell, clear of its name at the top");
                Assert.Less(inTheFourthStop.y + 0.25f * (fourthStop.YMax - fourthStop.YMin), viewportOf(rig.View.transform.Find("Label5")).y, "and the hand (half the height of its cell) stays under the name of the low cell");
            }
        }

        [Test]
        public void StepTwo_TheHandVisitsTheCells_InTheOrderOfTheChoreography()
        {
            using (var rig = new Rig())
            {
                var hand = rig.View.transform.Find("OpenHand");
                rig.View.Show(Frame(1f, 1, 0.05f), Clock);
                var first = hand.localPosition;
                rig.View.Show(Frame(1f, 1, 0.3f), Clock);
                var second = hand.localPosition;
                rig.View.Show(Frame(1f, 1, 0.55f), Clock);
                var fourth = hand.localPosition;

                Assert.Greater(second.x, first.x, "from the first cell to the second, to the right");
                Assert.AreEqual(first.y, second.y, 1e-3f, "along the top row");
                Assert.Less(fourth.y, first.y, "and then down into the bottom row");
            }
        }

        [Test]
        public void StepThree_ShowsTwoFists_ThatMoveApart_AndTheEndWordAtTheEnd()
        {
            using (var rig = new Rig())
            {
                var left = rig.View.transform.Find("FistLeft");
                var right = rig.View.transform.Find("FistRight");

                rig.View.Show(Frame(1f, 2, 0.1f), Clock);
                var gapBefore = right.localPosition.x - left.localPosition.x;
                Assert.AreEqual(2, rig.View.FistCount);
                Assert.AreEqual(0, rig.View.OpenHandCount);
                Assert.IsFalse(rig.View.EndWordShown, "the word comes after the fists have moved");
                Assert.AreEqual(rig.Settings.Texts[2], rig.View.CaptionText);

                rig.View.Show(Frame(1f, 2, 0.9f), Clock);
                var gapAfter = right.localPosition.x - left.localPosition.x;
                Assert.Greater(gapAfter, gapBefore * 2f, "they are well apart");
                Assert.IsTrue(rig.View.EndWordShown);
                Assert.AreEqual(rig.Settings.EndWord, rig.View.EndWordText);
                Assert.AreEqual(Mathf.Abs(left.localScale.x), Mathf.Abs(right.localScale.x), 1e-5f, "the two fists are the same size");
                Assert.AreEqual(-Mathf.Sign(left.localScale.x), Mathf.Sign(right.localScale.x), "and one of them is the mirror image of the other");
            }
        }

        [Test]
        public void ANextStep_ReplacesTheGlyphsOfTheLastOne()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 2, 0.5f), Clock);
                Assert.AreEqual(2, rig.View.FistCount);

                rig.View.Show(Frame(1f, 0, 0.5f), Clock);

                Assert.AreEqual(0, rig.View.FistCount);
                Assert.AreEqual(1, rig.View.OpenHandCount);
                Assert.AreEqual(rig.Settings.Texts[0], rig.View.CaptionText);
            }
        }

        [Test]
        public void ThreeDotsShowTheStep_AndATickReplacesTheDotOfADoneStep()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 1, 0.5f), Clock);
                Assert.AreEqual(3, rig.View.VisibleDotCount);
                Assert.AreEqual(0, rig.View.VisibleBadgeCount);

                rig.View.Show(Frame(1f, 1, 0.5f, check0: true), Clock + 1.0);
                Assert.AreEqual(1, rig.View.TickedDotCount, "step one is done");
                Assert.AreEqual(2, rig.View.VisibleDotCount - rig.View.TickedDotCount, "two are still to do");

                rig.View.Show(Frame(1f, 1, 0.5f, check0: true, check1: true), Clock + 2.0);
                Assert.AreEqual(2, rig.View.TickedDotCount);
                Assert.GreaterOrEqual(rig.View.VisibleBadgeCount, 1, "and the step on the screen shows its tick large");
            }
        }

        [Test]
        public void WhenNotVisible_NothingIsDrawn()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 0, 0.5f), Clock);
                Assert.IsTrue(rig.View.IsShown);

                rig.View.Show(default, Clock + 1.0);

                Assert.IsFalse(rig.View.IsShown);
                Assert.IsFalse(rig.View.gameObject.activeInHierarchy || rig.View.transform.Cast<Transform>().Any(t => t.gameObject.activeInHierarchy && t.GetComponent<Renderer>() != null && t.GetComponent<Renderer>().enabled),
                    "no renderer is left on");

                rig.View.Show(Frame(1f, 1, 0.5f), Clock + 2.0);
                Assert.IsTrue(rig.View.IsShown, "and it comes back");
            }
        }

        [Test]
        public void TheDim_IsThePartOfTheMeadowThatTheFrameLetsThrough()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 0, 0.5f), Clock);
                Assert.AreEqual(0.45f, rig.View.DimAlpha, 1e-4f, "the dim of the content at full strength");

                rig.View.Show(Frame(0.5f, 0, 0.5f), Clock);
                Assert.AreEqual(0.225f, rig.View.DimAlpha, 1e-4f, "half of it while fading");
            }
        }

        [Test]
        public void TheMeadow_IsDarker_WhenTheTutorialIsThere()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(default, Clock);
                var bare = rig.Picture();
                rig.View.Show(Frame(1f, 2, 0.05f), Clock);
                var dimmed = rig.Picture();
                try
                {
                    // a point of the meadow no glyph covers: the top left corner of the stage
                    var x = Width / 20;
                    var y = Mathf.RoundToInt(Height * 0.9f);
                    var a = Luminance(bare.GetPixel(x, y));
                    var b = Luminance(dimmed.GetPixel(x, y));
                    Assert.Less(b, a * 0.8f, "dimmed");
                    Assert.Greater(b, a * 0.35f, "but still seen through");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bare);
                    UnityEngine.Object.DestroyImmediate(dimmed);
                }
            }
        }

        [Test]
        public void TheRecordStrip_IsLeftAlone_DimmingAndAllElsewhere()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(default, Clock);
                var bare = rig.Picture();
                rig.View.Show(Frame(1f, 1, 0.5f, true, true, true), Clock);
                var shown = rig.Picture();
                try
                {
                    var stripTop = Mathf.FloorToInt(Height * OverlayFraction) - 3;
                    var changed = 0;
                    for (var y = 0; y < stripTop; y++)
                    {
                        for (var x = 0; x < Width; x++)
                        {
                            if (Mathf.Abs(Luminance(bare.GetPixel(x, y)) - Luminance(shown.GetPixel(x, y))) > 0.01f) changed++;
                        }
                    }
                    Assert.AreEqual(0, changed, "below the top of the strip the picture is exactly the same");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bare);
                    UnityEngine.Object.DestroyImmediate(shown);
                }
            }
        }

        [Test]
        public void EveryElement_StaysOnTheScreen_AboveTheRecordStrip()
        {
            using (var rig = new Rig()) AssertEveryElementIsAboveTheStrip(rig);
        }

        static void AssertEveryElementIsAboveTheStrip(Rig rig)
        {
            foreach (var step in new[] { 0, 1, 2 })
            {
                foreach (var progress in new[] { 0.05f, 0.3f, 0.55f, 0.8f, 1f })
                {
                    rig.View.Show(Frame(1f, step, progress, true, true, true), Clock + step * 5 + progress * 5);
                    var picture = rig.Picture();   // the texts are laid out when drawn
                    UnityEngine.Object.DestroyImmediate(picture);
                    foreach (var renderer in rig.View.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        var rect = rig.ViewportRectOf(renderer);
                        var name = $"{renderer.name} in step {step + 1} at {progress}";
                        Assert.GreaterOrEqual(rect.yMin, OverlayFraction - 0.004f, name + ": not over the strip");
                        Assert.LessOrEqual(rect.yMax, 1f + 0.004f, name + ": not off the top");
                        Assert.GreaterOrEqual(rect.xMin, -0.004f, name + ": not off the left");
                        Assert.LessOrEqual(rect.xMax, 1f + 0.004f, name + ": not off the right");
                    }
                }
            }
        }

        [Test]
        public void WithNoStrip_TheStageIsTheWholePicture()
        {
            using (var rig = new Rig(overlayFraction: 0f))
            {
                rig.View.Show(Frame(1f, 1, 0.5f), Clock);
                var picture = rig.Picture();
                UnityEngine.Object.DestroyImmediate(picture);
                var lowest = rig.View.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).Min(r => rig.ViewportRectOf(r).yMin);

                Assert.Less(lowest, 0.1f, "the bottom row of cells reaches the bottom of the picture");
            }
        }

        [Test]
        public void TheView_FollowsTheMappingItIsGiven()
        {
            using (var plain = new Rig())
            using (var shifted = new Rig(shift: new Vector3(0.3f, -0.2f, 0f)))
            {
                plain.View.Show(Frame(1f, 0, 0.5f), Clock);
                shifted.View.Show(Frame(1f, 0, 0.5f), Clock);

                var a = plain.View.transform.Find("OpenHand").localPosition;
                var b = shifted.View.transform.Find("OpenHand").localPosition;

                Assert.AreEqual(0.3f, b.x - a.x, 1e-4f);
                Assert.AreEqual(-0.2f, b.y - a.y, 1e-4f);
            }
        }

        [Test]
        public void ThePicture_OfAnotherShapeThanTheLastOne_GetsTheTutorialLaidOutAgain()
        {
            // a capture or a recording is drawn at its own size, not the window's: the view is shown for the shape the camera has between the pictures and must follow the shape of the one it draws now
            var cameraObject = new GameObject("tutorial reshape camera");
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 50f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                camera.enabled = false;
                var overlay = MeadowOverlayLayer.Create(camera);
                var view = TutorialView.Create(overlay.Root, overlay.LocalPoint, 0f, TutorialSettings.From(null), Labels, Colours, Fonts, overlay.SortingOrderBase);
                var shapeOfTheWindow = (float)camera.pixelWidth / camera.pixelHeight;
                Assume.That(shapeOfTheWindow, Is.Not.EqualTo(1600f / 900f).Within(0.05f), "the test needs a window of another shape than the picture");

                view.Show(Frame(1f, 2, 0.05f), Clock);   // laid out for the window
                var picture = Draw(camera, target);
                camera.targetTexture = null;
                view.Show(default, Clock);
                var bare = Draw(camera, target);
                try
                {
                    var y = Mathf.RoundToInt(900 * 0.9f);
                    foreach (var x in new[] { 4, 1600 - 5 })
                    {
                        Assert.Less(Luminance(picture.GetPixel(x, y)), Luminance(bare.GetPixel(x, y)) * 0.85f, $"the veil reaches the edge of the picture at x = {x}");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(picture);
                    UnityEngine.Object.DestroyImmediate(bare);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static Texture2D Draw(Camera camera, RenderTexture target)
        {
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                picture.Apply();
                return picture;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
            }
        }

        [Test]
        public void EachStep_PaintsPixels_AndNoneOfThemTheMagentaOfAMissingShader()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(default, Clock);
                var bare = rig.Picture();
                try
                {
                    foreach (var step in new[] { 0, 1, 2 })
                    {
                        var progress = step == 2 ? 0.9f : 0.5f;
                        rig.View.Show(Frame(1f, step, progress, true, step > 0, false), Clock);
                        rig.View.Show(Frame(1f, step, progress, true, step > 0, false), Clock + 1.0);   // the ticks have popped up by now
                        var picture = rig.Picture();
                        try
                        {
                            CaptureOutput.SavePicture(picture, $"tutorial-step{step + 1}.png");
                            var different = 0;
                            var magenta = 0;
                            var bright = 0;
                            var pixels = picture.GetPixels32();
                            var reference = bare.GetPixels32();
                            for (var i = 0; i < pixels.Length; i++)
                            {
                                var p = pixels[i];
                                if (p.r > 240 && p.g < 40 && p.b > 240) magenta++;
                                if (Mathf.Abs(Luminance(p) - Luminance(reference[i])) > 0.05f) different++;
                                if (p.r > 215 && p.g > 190 && p.b > 150) bright++;
                            }
                            Assert.AreEqual(0, magenta, $"step {step + 1}: no missing shader");
                            Assert.Greater(different, Width * Height / 20, $"step {step + 1} changes a good part of the picture (the dim and the glyphs)");
                            Assert.Greater(bright, 800, $"step {step + 1} has cream glyphs or white text on it");
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(picture);
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bare);
                }
            }
        }

        [Test]
        public void AFadingTutorial_ShowsLessThanAFullOne()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 0, 0.5f), Clock);
                var full = rig.Picture();
                rig.View.Show(Frame(0.3f, 0, 0.5f), Clock);
                var faded = rig.Picture();
                rig.View.Show(default, Clock);
                var bare = rig.Picture();
                try
                {
                    Assert.Less(Difference(faded, bare), Difference(full, bare) * 0.6, "a tutorial at 0.3 is nearer to the bare meadow than one at 1");
                    Assert.Greater(Difference(faded, bare), 0.0);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(full);
                    UnityEngine.Object.DestroyImmediate(faded);
                    UnityEngine.Object.DestroyImmediate(bare);
                }
            }
        }

        // ---- the pictures of the hands that come with the content ----

        static string TutorialFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "tutorial"));

        /// <summary>One of the pictures of the content, as the content loader would hand it over (the caller destroys it).</summary>
        static Texture2D Picture(string file)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "test " + file };
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(Path.Combine(TutorialFolder, file)), false), file);
            return texture;
        }

        static string TextureNameOf(Rig rig, string quad) => rig.View.transform.Find(quad).GetComponent<MeshRenderer>().sharedMaterial.mainTexture.name;

        [Test]
        public void WithoutPictures_BothHandsAreDrawnInCode()
        {
            using (var rig = new Rig())
            {
                Assert.IsFalse(rig.View.OpenHandIsPicture);
                Assert.IsFalse(rig.View.FistIsPicture);
                Assert.AreEqual("TutorialOpenHand", TextureNameOf(rig, "OpenHand"));
                Assert.AreEqual("TutorialFist", TextureNameOf(rig, "FistLeft"));
            }
        }

        [Test]
        public void WithPictures_TheHandsAreQuadsOfThePictures_NotTheGlyphsDrawnInCode()
        {
            var open = Picture("hand-open.png");
            var fist = Picture("hand-fist.png");
            try
            {
                using (var rig = new Rig(openHandPicture: open, fistPicture: fist))
                {
                    Assert.IsTrue(rig.View.OpenHandIsPicture);
                    Assert.IsTrue(rig.View.FistIsPicture);
                    Assert.AreEqual("TutorialOpenHandPicture", TextureNameOf(rig, "OpenHand"));
                    Assert.AreEqual("TutorialFistPicture", TextureNameOf(rig, "FistLeft"));
                    Assert.AreEqual("TutorialFistPicture", TextureNameOf(rig, "FistRight"));

                    rig.View.Show(Frame(1f, 0, 0.5f), Clock);
                    var hand = rig.View.transform.Find("OpenHand");
                    Assert.AreEqual(1, rig.View.OpenHandCount);
                    Assert.AreEqual(Mathf.Abs(hand.localScale.x), hand.localScale.y, 1e-5f, "a picture is a square");

                    rig.View.Show(Frame(1f, 2, 0.9f), Clock);
                    Assert.AreEqual(2, rig.View.FistCount);
                    var left = rig.View.transform.Find("FistLeft");
                    var right = rig.View.transform.Find("FistRight");
                    Assert.Less(left.localScale.x, 0f, "the picture is a right fist: the one on the left is turned round");
                    Assert.Greater(right.localScale.x, 0f);
                    Assert.AreEqual(Mathf.Abs(left.localScale.x), left.localScale.y, 1e-5f);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(open);
                UnityEngine.Object.DestroyImmediate(fist);
            }
        }

        [Test]
        public void TheFistDrawnInCode_IsTurnedRoundOnTheRight_TheOppositeOfThePicture()
        {
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 2, 0.9f), Clock);

                Assert.Greater(rig.View.transform.Find("FistLeft").localScale.x, 0f);
                Assert.Less(rig.View.transform.Find("FistRight").localScale.x, 0f);
            }
        }

        [Test]
        public void WithOnlyOnePicture_TheOtherHandIsDrawnInCode()
        {
            var open = Picture("hand-open.png");
            try
            {
                using (var rig = new Rig(openHandPicture: open))
                {
                    Assert.IsTrue(rig.View.OpenHandIsPicture);
                    Assert.IsFalse(rig.View.FistIsPicture, "the fist picture is not there");
                    rig.View.Show(Frame(1f, 2, 0.9f), Clock);
                    Assert.AreEqual(2, rig.View.FistCount, "and the two fists are still drawn");
                    Assert.AreEqual("TutorialFist", TextureNameOf(rig, "FistLeft"));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(open);
            }
        }

        [Test]
        public void ThePicturesAreCopied_SoTheContentThatHeldThemCanBeGivenBack()
        {
            var open = Picture("hand-open.png");
            var fist = Picture("hand-fist.png");
            using (var rig = new Rig(openHandPicture: open, fistPicture: fist))
            {
                UnityEngine.Object.DestroyImmediate(open);   // what a reload of the content does to the bundle it replaces
                UnityEngine.Object.DestroyImmediate(fist);

                foreach (var step in new[] { 0, 1, 2 })
                {
                    rig.View.Show(Frame(1f, step, 0.5f), Clock);
                    var picture = rig.Picture();
                    UnityEngine.Object.DestroyImmediate(picture);
                }

                Assert.IsTrue(rig.View.OpenHandIsPicture);
                Assert.AreEqual(512, rig.View.transform.Find("OpenHand").GetComponent<MeshRenderer>().sharedMaterial.mainTexture.width, "still the whole picture");
            }
        }

        [Test]
        public void WithPictures_EachStepPaintsTheHands_AndNothingIsMagenta()
        {
            var open = Picture("hand-open.png");
            var fist = Picture("hand-fist.png");
            try
            {
                using (var rig = new Rig(openHandPicture: open, fistPicture: fist))
                {
                    foreach (var step in new[] { 0, 1, 2 })
                    {
                        rig.View.Show(Frame(1f, step, step == 2 ? 0.9f : 0.5f), Clock);
                        var picture = rig.Picture();
                        try
                        {
                            CaptureOutput.SavePicture(picture, $"tutorial-pictures-step{step + 1}.png");
                            var golden = 0;
                            var magenta = 0;
                            foreach (var p in picture.GetPixels32())
                            {
                                if (p.r > 240 && p.g < 40 && p.b > 240) magenta++;
                                if (p.r > 200 && p.g > 150 && p.r - p.b > 70) golden++;   // the gold of the hologram hands, core #FFE7A6 and glow #F2B45A (the white of the texts has no such tint)
                            }
                            Assert.AreEqual(0, magenta, $"step {step + 1}: no missing shader or texture");
                            TestContext.Out.WriteLine($"step {step + 1}: {golden} gold pixels");
                            Assert.Greater(golden, GoldPixelsOfAHand, $"step {step + 1} shows the gold of a hand");
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(picture);
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(open);
                UnityEngine.Object.DestroyImmediate(fist);
            }
        }

        [Test]
        public void WithPictures_EveryElementStaysAboveTheRecordStrip()
        {
            var open = Picture("hand-open.png");
            var fist = Picture("hand-fist.png");
            try
            {
                using (var rig = new Rig(openHandPicture: open, fistPicture: fist)) AssertEveryElementIsAboveTheStrip(rig);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(open);
                UnityEngine.Object.DestroyImmediate(fist);
            }
        }

        [UnityTest]
        public IEnumerator WithPictures_DestroyingTheView_ReleasesItsCopies_AndLeavesTheCallersPictures()
        {
            yield return null;
            var open = Picture("hand-open.png");
            var fist = Picture("hand-fist.png");
            var before = Owned();
            using (var rig = new Rig(openHandPicture: open, fistPicture: fist))
            {
                rig.View.Show(Frame(1f, 2, 0.5f), Clock);
                Assert.Greater(Owned(), before);
            }
            yield return null;

            Assert.AreEqual(before, Owned(), "the copies are given back with the view");
            Assert.IsTrue(open != null && fist != null, "the pictures of the content are the caller's");
            UnityEngine.Object.DestroyImmediate(open);
            UnityEngine.Object.DestroyImmediate(fist);
        }

        [Test]
        public void ListsOfTheWrongLength_AreRefused()
        {
            var cameraObject = new GameObject("camera");
            try
            {
                var settings = TutorialSettings.From(null);
                Func<float, float, Vector3> mapping = (x, y) => new Vector3(x, y, 1f);
                Assert.Throws<ArgumentException>(() => TutorialView.Create(cameraObject.transform, mapping, 0.28f, settings, new[] { "a" }, Colours, Fonts));
                Assert.Throws<ArgumentException>(() => TutorialView.Create(cameraObject.transform, mapping, 0.28f, settings, Labels, new[] { Color.white }, Fonts));
                Assert.Throws<ArgumentException>(() => TutorialView.Create(cameraObject.transform, mapping, 0.28f, settings, Labels, Colours, new string[0]));
                Assert.Throws<ArgumentOutOfRangeException>(() => TutorialView.Create(cameraObject.transform, mapping, 0.9f, settings, Labels, Colours, Fonts));
                Assert.AreEqual(0, cameraObject.transform.childCount, "a refused view leaves nothing behind");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator Destroying_TheView_ReleasesTheTexturesMeshesAndMaterialsItMade()
        {
            yield return null;   // what earlier tests destroyed goes at the end of a frame
            var before = Owned();
            using (var rig = new Rig())
            {
                rig.View.Show(Frame(1f, 1, 0.5f, true), Clock);
                UnityEngine.Object.DestroyImmediate(rig.Picture());
                Assert.Greater(Owned(), before, "the view made its own textures, meshes and materials");
            }
            yield return null;

            Assert.AreEqual(before, Owned(), "and gave them all back with the view");
        }

        [UnityTest]
        public IEnumerator ABuiltAgain_ViewDoesNotPileUp()
        {
            yield return null;
            var before = Owned();
            for (var i = 0; i < 4; i++)
            {
                using (var rig = new Rig())
                {
                    rig.View.Show(Frame(1f, i % 3, 0.5f), Clock);
                    UnityEngine.Object.DestroyImmediate(rig.Picture());
                }
                yield return null;
            }

            Assert.AreEqual(before, Owned());
        }

        /// <summary>How many textures, meshes and materials of the tutorial are alive (their names begin with "Tutorial").</summary>
        static int Owned() =>
            Resources.FindObjectsOfTypeAll<Texture2D>().Count(o => o.name.StartsWith("Tutorial", StringComparison.Ordinal))
            + Resources.FindObjectsOfTypeAll<Mesh>().Count(o => o.name.StartsWith("Tutorial", StringComparison.Ordinal))
            + Resources.FindObjectsOfTypeAll<Material>().Count(o => o.name.StartsWith("Tutorial", StringComparison.Ordinal));

        static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        static float Luminance(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static double Difference(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            double sum = 0;
            for (var i = 0; i < pa.Length; i++) sum += Math.Abs(Luminance(pa[i]) - Luminance(pb[i]));
            return sum / pa.Length;
        }
    }
}
