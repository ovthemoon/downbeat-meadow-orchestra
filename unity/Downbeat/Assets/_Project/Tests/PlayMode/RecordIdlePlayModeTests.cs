using System;
using System.Collections;
using System.Linq;
using System.IO;
using Downbeat.Core;
using Downbeat.Record;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// Item 11 on a real picture: the record screen of its own (the offscreen mount, the same camera and shader as the second display) with no cards and nobody playing is alive, and calm.
    /// Pictures are drawn at a given clock, so the times are exact and the run takes no real seconds.
    /// </summary>
    public class RecordIdlePlayModeTests
    {
        const int Width = 960;
        const int Height = 540;
        const double Clock = 2000.0;
        const double Frame = 1.0 / 60.0;
        const double TenSeconds = 10.0;

        /// <summary>A luminance step of at most this many levels (of 255) does not count as a changed pixel.</summary>
        const float PixelNoise = 1.5f;

        // Thresholds, picked from what the first runs measured (idle screen, no cards, 960x540, luminance 0..255, pixels counted as changed above PixelNoise):
        //   10 s apart:       mean difference 0.486, 1.61 % of the pixels changed; with the motes switched off (only the breathing hint moves) 0.028 and 0.27 %
        //   one frame apart:  mean difference 0.0118, 0.27 % of the pixels changed; with the motes switched off 0.0003 and 0 %
        //   energy of the motes (their summed brightness over the picture without them): performing / idle = 0.44 (the level is 0.3 in linear light; the picture is sRGB)
        const float TenSecondsMeanAtLeast = 0.25f;
        const float TenSecondsChangedShareAtLeast = 0.008f;
        const float NeighbourMeanAtMost = 0.025f;
        const float NeighbourChangedShareAtMost = 0.005f;
        const double MotesVisibleEnergyAtLeast = 20000.0;
        const float PerformingEnergyAtMost = 0.5f;

        sealed class Screen : IDisposable
        {
            readonly GameObject _cameraObject;
            readonly GameObject _viewObject;
            public readonly RecordScreenView View;

            readonly int _width;
            readonly int _height;

            public Screen(Action<RecordDto> change = null, int Width = RecordIdlePlayModeTests.Width, int Height = RecordIdlePlayModeTests.Height)
            {
                _width = Width;
                _height = Height;
                var content = Shipped();
                change?.Invoke(content.record);
                _cameraObject = new GameObject("meadow camera");
                var camera = _cameraObject.AddComponent<Camera>();
                _viewObject = new GameObject("record");
                View = _viewObject.AddComponent<RecordScreenView>();
                View.Configure(content, new ScoreModel(16), camera, RecordMount.Offscreen, 0);
            }

            public Texture2D Picture(double clock) => View.RenderPicture(_width, _height, 0.0, clock);

            /// <summary>Ticks the screen 20 times a second of clock time from <paramref name="from"/> to <paramref name="until"/> (the dimming follows the clock, not the frames).</summary>
            public void Run(double from, double until)
            {
                for (var t = from; t < until; t += 1.0 / 20.0) View.Tick(0.0, t);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_viewObject);
                UnityEngine.Object.DestroyImmediate(_cameraObject);
            }
        }

        static ContentDto Shipped() =>
            JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        static float Luminance(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>Mean absolute luminance difference (0..255) and the share of pixels that changed by more than the noise.</summary>
        static (float Mean, float ChangedShare) Difference(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            var sum = 0.0;
            var changed = 0;
            for (var i = 0; i < pa.Length; i++)
            {
                var d = Math.Abs(Luminance(pa[i]) - Luminance(pb[i]));
                sum += d;
                if (d > PixelNoise) changed++;
            }
            return ((float)(sum / pa.Length), changed / (float)pa.Length);
        }

        /// <summary>How much the pictures with motes differ from the picture of the same moment without any: the brightness of the motes, summed.</summary>
        static double Energy(Texture2D withMotes, Texture2D without)
        {
            var pa = withMotes.GetPixels32();
            var pb = without.GetPixels32();
            var sum = 0.0;
            for (var i = 0; i < pa.Length; i++) sum += Math.Abs(Luminance(pa[i]) - Luminance(pb[i]));
            return sum;
        }

        static void Destroy(Texture2D[] textures)
        {
            foreach (var t in textures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
        }

        /// <summary>One screen at a time: every record screen sits at the same far place of the world, and a camera draws every one it can see.</summary>
        static Texture2D[] Capture(Action<RecordDto> change, params double[] clocks)
        {
            using (var screen = new Screen(change))
            {
                var pictures = new Texture2D[clocks.Length];
                for (var i = 0; i < clocks.Length; i++) pictures[i] = screen.Picture(clocks[i]);
                return pictures;
            }
        }

        [UnityTest]
        public IEnumerator TheIdleRecordScreen_MovesClearlyOverTenSeconds_ButBarelyBetweenTwoNeighbouringFrames()
        {
            yield return null;
            var withMotes = Capture(null, Clock, Clock + Frame, Clock + TenSeconds);
            yield return null;   // the screen above is gone at the end of the frame
            var without = Capture(r => r.idleMotes = 0, Clock, Clock + Frame, Clock + TenSeconds);
            try
            {
                var tenSeconds = Difference(withMotes[0], withMotes[2]);
                var neighbour = Difference(withMotes[0], withMotes[1]);
                var tenSecondsWithoutMotes = Difference(without[0], without[2]);
                var neighbourWithoutMotes = Difference(without[0], without[1]);
                TestContext.WriteLine($"[idle-motes] 10 s apart: mean {tenSeconds.Mean:0.0000}, changed {tenSeconds.ChangedShare * 100:0.000} %; without motes: mean {tenSecondsWithoutMotes.Mean:0.0000}, changed {tenSecondsWithoutMotes.ChangedShare * 100:0.000} %");
                TestContext.WriteLine($"[idle-motes] one frame apart: mean {neighbour.Mean:0.0000}, changed {neighbour.ChangedShare * 100:0.000} %; without motes: mean {neighbourWithoutMotes.Mean:0.0000}, changed {neighbourWithoutMotes.ChangedShare * 100:0.000} %");

                Assert.GreaterOrEqual(tenSeconds.Mean, TenSecondsMeanAtLeast, "ten seconds apart the pictures differ clearly");
                Assert.GreaterOrEqual(tenSeconds.ChangedShare, TenSecondsChangedShareAtLeast, "and a clear share of the pixels has changed");
                Assert.Greater(tenSeconds.Mean, tenSecondsWithoutMotes.Mean * 3f, "which is the motes: without them the screen is far stiller (only the breathing hint moves)");
                Assert.LessOrEqual(neighbour.Mean, NeighbourMeanAtMost, "two neighbouring frames are nearly the same picture");
                Assert.LessOrEqual(neighbour.ChangedShare, NeighbourChangedShareAtMost, "with hardly any pixel changed: nothing flickers");
                Assert.Greater(tenSeconds.Mean, neighbour.Mean * 10f, "ten seconds change the picture much more than one frame does");
            }
            finally
            {
                Destroy(withMotes);
                Destroy(without);
            }
        }

        [UnityTest]
        public IEnumerator WhilePerforming_TheMotesAreDimmer_AndTheyComeBackWhenTheMeadowIsIdleAgain()
        {
            yield return null;
            var without = Capture(r => r.idleMotes = 0, Clock, Clock + 8.0);
            yield return null;
            Texture2D[] calm;
            float calmLevel;
            using (var screen = new Screen())
            {
                screen.Run(Clock - 6.0, Clock);
                var first = screen.Picture(Clock);
                screen.Run(Clock, Clock + 8.0);
                calm = new[] { first, screen.Picture(Clock + 8.0) };
                calmLevel = screen.View.MoteLevel;
            }
            yield return null;
            var busy = new Texture2D[2];
            float busyLevel, backLevel;
            using (var screen = new Screen())
            {
                screen.View.Performing = true;
                screen.Run(Clock - 6.0, Clock);
                busy[0] = screen.Picture(Clock);
                busyLevel = screen.View.MoteLevel;
                screen.View.Performing = false;
                screen.Run(Clock, Clock + 8.0);
                busy[1] = screen.Picture(Clock + 8.0);
                backLevel = screen.View.MoteLevel;
            }
            try
            {
                var calmEnergy = Energy(calm[0], without[0]);
                var busyEnergy = Energy(busy[0], without[0]);
                var calmLaterEnergy = Energy(calm[1], without[1]);
                var backEnergy = Energy(busy[1], without[1]);
                TestContext.WriteLine($"[idle-motes] energy of the motes: idle {calmEnergy:0}, performing {busyEnergy:0} ({busyEnergy / calmEnergy:0.00} of idle), idle again {backEnergy:0} against {calmLaterEnergy:0} that never left; levels {calmLevel:0.00} / {busyLevel:0.00} / {backLevel:0.00}");

                Assert.AreEqual(1f, calmLevel, 1e-4f);
                Assert.AreEqual(0.3f, busyLevel, 1e-3f, "the level the content asks for");
                Assert.Greater(calmEnergy, MotesVisibleEnergyAtLeast, "there are motes to see when idle");
                Assert.LessOrEqual(busyEnergy, calmEnergy * PerformingEnergyAtMost, "a hand is in: the motes are clearly dimmer");
                Assert.AreEqual(1f, backLevel, 1e-3f, "the meadow is idle again");
                Assert.AreEqual(calmLaterEnergy, backEnergy, calmLaterEnergy * 0.02, "and the motes are as bright as the ones that never left");
            }
            finally
            {
                Destroy(without);
                Destroy(calm);
                Destroy(busy);
            }
        }

        static int Order(Transform root, string layer)
        {
            var found = root.Find(layer);
            Assert.IsNotNull(found, "the layer " + layer + " exists");
            return found.GetComponent<MeshRenderer>().sortingOrder;
        }

        [UnityTest]
        public IEnumerator TheMotes_AreOneMeshInOneDrawCall_AboveTheBackdropOnly_BehindTheLinesThePanelsAndTheText()
        {
            yield return null;
            using (var screen = new Screen())
            {
                var root = screen.View.ScreenRoot;
                var motes = root.Find("Motes");
                Assert.IsNotNull(motes, "one layer for all of them");
                Assert.AreEqual(24 * 4, motes.GetComponent<MeshFilter>().sharedMesh.vertexCount, "24 quads, one mesh");

                var order = Order(root, "Motes");
                Assert.Greater(order, Order(root, "Solid"), "the motes are over the backdrop fill");
                // the staff lines, the line of now and the lines of the nature layers are not part of the backdrop: they have a layer of their own, over the motes and still under the translucent panels
                Assert.Less(order, Order(root, "Lines"), "a firefly crossing the score goes behind its lines");
                Assert.Less(Order(root, "Lines"), Order(root, "Round"), "and the lines stay under the translucent panels, as they were");
                foreach (var behind in new[] { "Round", "Overlay", "Glow", "Flowers" })
                    Assert.Less(order, Order(root, behind), "the motes are drawn before (so behind) " + behind);
                foreach (var text in root.GetComponentsInChildren<TextMesh>(true))
                    Assert.Less(order, text.GetComponent<MeshRenderer>().sortingOrder, "and behind the text " + text.name);
            }
        }

        /// <summary>Finds a moment at which a mote sits on a staff line (away from the middle, where the hint lies), and where on the 1920x1080 picture.</summary>
        static bool FindMoteOnAStaffLine(Screen screen, out double clock, out int pixelX, out int pixelY, out int moteSizePixels)
        {
            var content = Shipped();
            var layout = screen.View.Layout;
            var score = layout.Score;
            var pad = Mathf.Min(score.Height * 0.07f, 26f);
            var px = score.X + pad;
            var pw = score.Width - 2f * pad;
            var ph = score.Height - 2f * pad;
            var centre = px + pw * 0.5f;
            const float Reference = 1080f;
            for (clock = 100.0; clock < 3000.0; clock += 0.25)
            {
                screen.View.Tick(0.0, clock);
                for (var i = 0; i < screen.View.MoteCount; i++)
                {
                    var mote = screen.View.MoteAt(i);
                    if (mote.Alpha < 0.25f) continue;
                    var x = mote.X * Reference;
                    var y = mote.Y * Reference;
                    if (x < px + pw * 0.2f || x > px + pw * 0.9f || Mathf.Abs(x - centre) < pw * 0.25f) continue;
                    for (var line = 0; line < 5; line++)
                    {
                        var lineY = score.Y + pad + ScoreLayout.StaffLineY(line, ph, content.song.pitchSteps);
                        if (Mathf.Abs(y - lineY) > mote.Size * Reference * 0.08f) continue;
                        pixelX = Mathf.RoundToInt(x);
                        pixelY = Mathf.FloorToInt(lineY);
                        moteSizePixels = Mathf.RoundToInt(mote.Size * Reference);
                        return true;
                    }
                }
            }
            pixelX = pixelY = moteSizePixels = 0;
            return false;
        }

        static int MaxChannelDifference(Texture2D a, Texture2D b, int x0, int x1, int y)
        {
            var worst = 0;
            for (var x = x0; x <= x1; x++)
            {
                var ca = a.GetPixel(x, y);
                var cb = b.GetPixel(x, y);
                worst = Mathf.Max(worst, Mathf.RoundToInt(Mathf.Abs(ca.r - cb.r) * 255f), Mathf.RoundToInt(Mathf.Abs(ca.g - cb.g) * 255f), Mathf.RoundToInt(Mathf.Abs(ca.b - cb.b) * 255f));
            }
            return worst;
        }

        [UnityTest]
        public IEnumerator AMoteOverAStaffLine_GoesBehindTheLine_TheLineIsNotTinted()
        {
            yield return null;
            Texture2D with = null, without = null;
            double clock;
            int x, y, size;
            using (var screen = new Screen(null, Width: 1920, Height: 1080))
            {
                Assert.IsTrue(FindMoteOnAStaffLine(screen, out clock, out x, out y, out size), "a mote crosses a staff line at some moment");
                TestContext.WriteLine($"[idle-motes] a mote of {size} px sits on a staff line at clock {clock}, picture ({x}, {y})");
                with = screen.View.RenderPicture(1920, 1080, 0.0, clock);
            }
            yield return null;
            using (var screen = new Screen(r => r.idleMotes = 0, Width: 1920, Height: 1080)) without = screen.View.RenderPicture(1920, 1080, 0.0, clock);
            try
            {
                var half = Mathf.Max(2, size / 6);
                var onTheLine = MaxChannelDifference(with, without, x - half, x + half, y);
                var besideTheLine = MaxChannelDifference(with, without, x - half, x + half, y + Mathf.Max(6, size / 3));
                TestContext.WriteLine($"[idle-motes] largest difference with and without the motes: on the line {onTheLine}, a few pixels above it {besideTheLine} (of 255)");

                Assert.Greater(besideTheLine, 3, "the mote is really there, just above the line");
                Assert.LessOrEqual(onTheLine, 1, "but where it crosses the line the line is the line: the mote is behind it");
            }
            finally
            {
                Destroy(new[] { with, without });
            }
        }

        static int Alive<T>(string prefix) where T : UnityEngine.Object => Resources.FindObjectsOfTypeAll<T>().Count(o => o.name.StartsWith(prefix));

        [UnityTest]
        public IEnumerator ADestroyedRecordScreen_LeavesNoMeshMaterialTextureFontOrRenderTextureBehind()
        {
            yield return null;
            // one round first: whatever the engine keeps for the first screen of the run (font tables, the first draw) is not a leak of the screen
            using (new Screen()) { }
            yield return null;
            yield return null;
            int Count() => Alive<Mesh>("Record") + Alive<Material>("Record") + Alive<Texture2D>("Record") + Alive<Texture2D>("FlowerIcons") + Alive<Texture2D>("RoundedRect") + Alive<RenderTexture>("Record")
                + Resources.FindObjectsOfTypeAll<Font>().Length + Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "RecordScreen");
            var before = Count();
            var meshes = Alive<Mesh>("Record");
            var fonts = Resources.FindObjectsOfTypeAll<Font>().Length;

            for (var round = 0; round < 3; round++)
            {
                using (new Screen()) { }
                yield return null;
                yield return null;
            }

            TestContext.WriteLine($"[idle-motes] after three screens were made and destroyed: {Alive<Mesh>("Record") - meshes} meshes, {Resources.FindObjectsOfTypeAll<Font>().Length - fonts} fonts, {Count() - before} objects more");
            Assert.AreEqual(before, Count(), "everything the screen made is gone with it: the meshes, the materials, the font, the offscreen picture");
        }
    }
}
