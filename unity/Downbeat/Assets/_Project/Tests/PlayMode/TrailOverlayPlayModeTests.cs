using System;
using System.Collections;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// The note trail and the cell guide, built on a dummy parent (a camera and a plane in front of it, as the overlay of the meadow camera will be) and drawn by a camera: they build, draw something where a hand is and
    /// nothing where it is not, log no error, and leave nothing behind when they are destroyed.
    /// </summary>
    public class TrailOverlayPlayModeTests
    {
        const int Width = 960;
        const int Height = 540;
        const float FovDeg = 50f;
        const float StripFraction = 0.28f;
        const double FrameDt = 1.0 / 30.0;
        // the test draws only what it made: far from the origin of whatever scene an earlier test left loaded, and on a layer of its own
        const int TestLayer = 31;
        static readonly Vector3 FarAway = new Vector3(5000f, 5000f, 5000f);
        static readonly string[] Labels = { "하프", "리코더", "종", "낮은 현", "마림바", "손북" };
        static readonly string[] Colors = { "#F2C46B", "#F4F1E6", "#BFE3FF", "#C9A27A", "#E2453A", "#E8A04A" };
        static readonly string[] Fonts = { "Apple SD Gothic Neo", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR" };

        Camera _camera;
        Transform _plane;

        [SetUp]
        public void BuildCamera()
        {
            var cameraObject = new GameObject("TrailTestCamera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.fieldOfView = FovDeg;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 10f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.02f, 0.03f, 0.09f);
            _camera.allowHDR = false;
            _camera.enabled = false;
            _camera.cullingMask = 1 << TestLayer;
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cameraObject.transform.position = FarAway;
            _plane = new GameObject("TrailTestPlane") { layer = TestLayer }.transform;
            _plane.SetParent(cameraObject.transform, false);
        }

        [TearDown]
        public void RemoveCamera()
        {
            if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera.gameObject);   // at once: the next test makes its camera in the same frame, at the same place
        }

        /// <summary>The point of the viewport on the plane one metre in front of the camera, in the space of the plane.</summary>
        Vector3 LocalPoint(float vx, float vy)
        {
            var halfHeight = Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad);
            return new Vector3((vx - 0.5f) * 2f * halfHeight * Width / Height, (vy - 0.5f) * 2f * halfHeight, 1f);
        }

        static T NewView<T>(string name) where T : Component => new GameObject(name).AddComponent<T>();

        int LitPixels(string pictureName)
        {
            var picture = MeadowCapture.Render(_camera, Width, Height);
            CaptureOutput.SavePicture(picture, pictureName);
            var pixels = picture.GetPixels32();
            UnityEngine.Object.Destroy(picture);
            var count = 0;
            foreach (var pixel in pixels)
            {
                if (pixel.r + pixel.g + pixel.b > 3 * 90) count++;
            }
            return count;
        }

        static TrailHandInput Hand(float x, float y, double captureMs, bool isFist, Color color, bool visible = true) =>
            new TrailHandInput(visible, isFist, x, y, captureMs, handId: 1, epoch: 0, color: color);

        [Test]
        public void NoteTrail_BuildsAndDraws_NotesBehindAnOpenHand_AndNoneBehindAFist()
        {
            var view = NewView<NoteTrailView>("NoteTrail");
            view.Configure(_plane, LocalPoint, TrailSettings.From(null), StripFraction, sortingOrder: 101);
            var inputs = new TrailHandInput[2];
            var gold = new Color(0.95f, 0.77f, 0.42f);
            var drawn = 0;
            var fistDrawn = 0;

            // act: an open hand and a fist cross the picture side by side (in 1.5 s), seen by the tracker 6 times a second
            for (var frame = 0; frame < 45; frame++)
            {
                var now = frame * FrameDt;
                var seen = Mathf.Floor((float)(now * 6.0)) / 6f;
                inputs[0] = Hand(0.1f + 0.8f * seen / 1.5f, 0.35f, seen * 1000.0, false, gold);
                inputs[1] = Hand(0.9f - 0.8f * seen / 1.5f, 0.35f, seen * 1000.0, true, gold);
                view.Tick(now, inputs);
                drawn = Math.Max(drawn, view.NoteCount);
            }
            var lit = LitPixels("trail-notes.png");

            // assert
            Assert.Greater(drawn, 3, "the open hand leaves notes");
            Assert.LessOrEqual(view.AliveCount, TrailSettings.From(null).MaxNotes, "only one hand leaves any; never more than maxNotes");
            Assert.Greater(lit, 80, "and they are in the picture (a few notes of about 20 pixels)");

            // a view with only a fist draws nothing
            var fistOnly = NewView<NoteTrailView>("FistTrail");
            fistOnly.Configure(_plane, LocalPoint, TrailSettings.From(null), StripFraction, sortingOrder: 102);
            for (var frame = 0; frame < 90; frame++)
            {
                var now = frame * FrameDt;
                inputs[0] = Hand(0.1f + 0.8f * frame / 90f, 0.35f, now * 1000.0, true, gold);
                inputs[1] = default;
                fistOnly.Tick(now, inputs);
                fistDrawn = Math.Max(fistDrawn, fistOnly.NoteCount);
            }
            Assert.AreEqual(0, fistDrawn, "a fist leaves no trail");
        }

        [Test]
        public void NoteTrail_LowTier_DrawsFewerNotes_AndTurnsOffWithAScaleOfZero()
        {
            var gold = new Color(0.95f, 0.77f, 0.42f);
            int Most(float scale)
            {
                var view = NewView<NoteTrailView>("Trail" + scale);
                view.Configure(_plane, LocalPoint, TrailSettings.From(new TrailDto { notesPerSec = 20f, maxNotes = 40, lifeSec = 2f }), StripFraction, 103);
                view.CountScale = scale;
                var inputs = new TrailHandInput[1];
                var most = 0;
                for (var frame = 0; frame < 150; frame++)
                {
                    var now = frame * FrameDt;
                    inputs[0] = Hand(0.2f + 0.6f * (frame % 60) / 60f, 0.4f, now * 1000.0, false, gold);
                    view.Tick(now, inputs);
                    most = Math.Max(most, view.NoteCount);
                }
                return most;
            }

            var high = Most(1f);
            var low = Most(0.5f);
            var off = Most(0f);

            Assert.Greater(high, low, "the Low tier draws fewer notes");
            Assert.Greater(low, 0);
            Assert.AreEqual(0, off);
        }

        [Test]
        public void NoteTrail_AHandThatLeavesAndComesBack_AndAHandThatIsMissing_LogNoError()
        {
            var view = NewView<NoteTrailView>("NoteTrail");
            view.Configure(_plane, LocalPoint, TrailSettings.From(null), 0f, 101);
            var green = new Color(0.4f, 0.9f, 0.5f);

            for (var frame = 0; frame < 120; frame++)
            {
                var now = frame * FrameDt;
                var inputs = frame % 40 < 20
                    ? new[] { Hand(0.1f + 0.02f * frame, 0.5f, now * 1000.0, false, green), Hand(0.3f, 0.5f, now * 1000.0, false, green, visible: false) }
                    : null;
                view.Tick(now, inputs);
            }

            Assert.IsTrue(view.IsConfigured);
            view.Configure(_plane, LocalPoint, TrailSettings.From(null), 0f, 101); // configured again
            view.Tick(10.0, null);
            Assert.AreEqual(0, view.NoteCount);
        }

        [Test]
        public void NoteGlyph_TextureHasAHeadAStemAndAFlag_AndEmptyCorners()
        {
            var texture = NoteGlyphTexture.Create(readable: true);
            try
            {
                Assert.Greater(texture.GetPixelBilinear(0.36f, 0.22f).a, 0.95f, "the head");
                Assert.Greater(texture.GetPixelBilinear(0.492f, 0.55f).a, 0.95f, "the stem");
                Assert.Greater(texture.GetPixelBilinear(0.36f, 0.22f).r, 0.9f, "the head is white, so that the colour of the cell shows as it is");
                var edge = texture.GetPixelBilinear(0.492f - 0.028f - 0.02f, 0.55f);
                Assert.Greater(edge.a, 0.9f, "a solid outline beside the stem");
                Assert.Less(edge.r, 0.15f, "that is nearly black, so that a light note reads on a bright sky");
                Assert.Less(texture.GetPixelBilinear(0.03f, 0.03f).a, 0.03f, "the corner");
                Assert.Less(texture.GetPixelBilinear(0.97f, 0.97f).a, 0.03f);
                Assert.Greater(texture.mipmapCount, 1, "mip maps for the note drawn small");
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        [Test]
        public void CellGuide_ShowsTheNameOnceAndTheBorderWhileTheHandStaysInTheCell()
        {
            var view = NewView<CellGuideView>("CellGuide");
            view.Configure(_plane, LocalPoint, Labels, Colors, Fonts, StripFraction, TrailSettings.From(null), sortingOrder: 100);
            Assert.AreEqual(0, LitPixels("cell-guide-idle.png"), "nothing shows until a hand is in a cell");

            // a hand comes into the top middle cell
            var now = 0.0;
            for (; now < 0.5; now += FrameDt) view.Tick(now, 1, -1);
            var withName = LitPixels("cell-guide-name.png");
            var nameAlpha = view.LabelAlpha(1);
            var glowIn = view.Glow(1);
            Assert.AreEqual(1, view.VisibleLabelCount);

            // it stays there well past labelSec
            for (; now < 4.0; now += FrameDt) view.Tick(now, 1, -1);
            var onlyBorder = LitPixels("cell-guide-border.png");

            // and moves into the bottom right cell
            for (; now < 4.5; now += FrameDt) view.Tick(now, 5, -1);
            var secondName = view.LabelAlpha(5);
            LitPixels("cell-guide-second.png");

            // and goes
            for (; now < 8.0; now += FrameDt) view.Tick(now, -1, -1);
            var after = LitPixels("cell-guide-gone.png");

            Assert.Greater(nameAlpha, 0.9f, "the name of the cell is there");
            Assert.Greater(glowIn, 0.5f, "and the border glows");
            Assert.Greater(withName, 200, "both are in the picture");
            Assert.AreEqual(0, view.LabelAlpha(1), "the name is not shown again while the hand stays");
            Assert.Greater(onlyBorder, 100, "but the border still glows");
            Assert.Less(onlyBorder, withName, "without the name there is less to see");
            Assert.Greater(secondName, 0.5f, "the next cell shows its own name");
            Assert.AreEqual(0, after, "a cell nobody is in is not drawn at all");
            Assert.AreEqual(0, view.VisibleLabelCount);
        }

        [Test]
        public void CellGuide_RefusesTheWrongNumberOfLabelsOrColours()
        {
            var view = NewView<CellGuideView>("CellGuide");

            Assert.Throws<ArgumentException>(() => view.Configure(_plane, LocalPoint, new[] { "a", "b" }, Colors, Fonts, 0f, TrailSettings.From(null), 100));
            Assert.Throws<ArgumentException>(() => view.Configure(_plane, LocalPoint, Labels, new[] { "#FFFFFF" }, Fonts, 0f, TrailSettings.From(null), 100));
            Assert.IsFalse(view.IsConfigured);
            view.Tick(0.0, 0, 1); // an unconfigured view does nothing
        }

        [UnityTest]
        public IEnumerator BothViews_LeaveNothingBehind_WhenTheyAreDestroyed()
        {
            var baseline = CountOfOurAssets();   // the scene of an earlier test may have its own
            var trail = NewView<NoteTrailView>("NoteTrail");
            trail.Configure(_plane, LocalPoint, TrailSettings.From(null), StripFraction, 101);
            var guide = NewView<CellGuideView>("CellGuide");
            guide.Configure(_plane, LocalPoint, Labels, Colors, Fonts, StripFraction, TrailSettings.From(null), 100);
            guide.Tick(0.0, 2, -1);
            guide.Tick(0.1, 2, -1);
            trail.Tick(0.0, new[] { Hand(0.5f, 0.5f, 0.0, false, Color.white) });
            Assert.AreEqual(baseline + 6, CountOfOurAssets(), "two meshes, two textures (the glyph and the frame) and two materials");

            UnityEngine.Object.Destroy(trail.gameObject);
            UnityEngine.Object.Destroy(guide.gameObject);
            yield return null;

            Assert.AreEqual(baseline, CountOfOurAssets(), "meshes, textures and materials are all given back");
        }

        static int CountOfOurAssets()
        {
            var count = 0;
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) if (mesh.name == "TrailNotes" || mesh.name == "CellFrames") count++;
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>()) if (texture.name == "NoteGlyph" || texture.name == "CellFrame") count++;
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>()) if (material.name == "TrailNotes" || material.name == "CellFrames") count++;
            return count;
        }
    }
}
