using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The hand glyphs are painted in code (no picture file, no font): an open hand, a fist, a tick badge and an arrow. The tests look at single points of the picture, given in the 100 x 120 units of the drawing.</summary>
    public class TutorialGlyphTests
    {
        const float Unit = TutorialGlyphs.PixelsPerUnit;

        static Color32 At(Texture2D texture, float x, float yDown, float unitsHigh) =>
            texture.GetPixel(Mathf.FloorToInt(x * Unit), Mathf.FloorToInt((unitsHigh - yDown) * Unit));

        static Color32 OpenHandAt(Texture2D texture, float x, float y) => At(texture, x, y, TutorialGlyphs.HandUnitsHigh);

        [Test]
        public void TheOpenHand_HasAPalmAndFiveFingers()
        {
            var hand = TutorialGlyphs.CreateOpenHand(true);
            try
            {
                Assert.AreEqual(Mathf.RoundToInt(TutorialGlyphs.HandUnitsWide * Unit), hand.width);
                Assert.AreEqual(Mathf.RoundToInt(TutorialGlyphs.HandUnitsHigh * Unit), hand.height);
                Assert.AreEqual(255, OpenHandAt(hand, 50f, 85f).a, "the palm");
                foreach (var finger in new[] { new Vector2(32.5f, 25f), new Vector2(45.5f, 20f), new Vector2(58.5f, 25f), new Vector2(71f, 38f) })
                    Assert.AreEqual(255, OpenHandAt(hand, finger.x, finger.y).a, $"a finger at {finger}");
                Assert.AreEqual(255, OpenHandAt(hand, 14f, 73f).a, "the thumb");
                Assert.AreEqual(0, OpenHandAt(hand, 3f, 3f).a, "nothing in the corner");
                Assert.AreEqual(0, OpenHandAt(hand, 95f, 60f).a, "nothing beside the palm");
            }
            finally
            {
                Object.DestroyImmediate(hand);
            }
        }

        [Test]
        public void TheOpenHand_IsCreamWithABrownOutline()
        {
            var hand = TutorialGlyphs.CreateOpenHand(true);
            try
            {
                var inside = OpenHandAt(hand, 50f, 90f);
                Assert.That(inside.r, Is.GreaterThan(240));
                Assert.That(inside.b, Is.InRange(200, 235), "cream, not white");

                // the outline: walking left to right across the palm along y = 90 the colour goes dark and then light again
                var darkest = 255;
                for (var x = 21f; x <= 27f; x += 0.2f) darkest = Mathf.Min(darkest, OpenHandAt(hand, x, 90f).r);
                Assert.That(darkest, Is.LessThan(170), "the edge of the palm is drawn");
            }
            finally
            {
                Object.DestroyImmediate(hand);
            }
        }

        [Test]
        public void TheFist_IsARoundedBlock_WithNoFingersAbove()
        {
            var fist = TutorialGlyphs.CreateFist(true);
            try
            {
                Assert.AreEqual(255, OpenHandAt(fist, 50f, 80f).a, "the block");
                Assert.AreEqual(255, OpenHandAt(fist, 40f, 70f).a, "the thumb across it");
                Assert.AreEqual(0, OpenHandAt(fist, 45f, 20f).a, "where the open hand has its middle finger there is nothing");
                Assert.AreEqual(0, OpenHandAt(fist, 5f, 100f).a, "and nothing in the corner");
            }
            finally
            {
                Object.DestroyImmediate(fist);
            }
        }

        [Test]
        public void TheFist_HasKnuckleLines()
        {
            var fist = TutorialGlyphs.CreateFist(true);
            try
            {
                // the three short lines at x = 35, 50 and 64 run from y = 40 to 56: darker than the cream around them
                foreach (var x in new[] { 35f, 50f, 64f })
                {
                    var onLine = OpenHandAt(fist, x, 42f).r;
                    var beside = OpenHandAt(fist, x + 5f, 42f).r;
                    Assert.That(onLine, Is.LessThan(beside - 40), $"a knuckle line at x = {x}");
                }
            }
            finally
            {
                Object.DestroyImmediate(fist);
            }
        }

        [Test]
        public void TheTwoHands_AreNotTheSamePicture()
        {
            var open = TutorialGlyphs.CreateOpenHand(true);
            var fist = TutorialGlyphs.CreateFist(true);
            try
            {
                Assert.AreEqual(open.width, fist.width, "they share the same box so that one can replace the other");
                Assert.AreEqual(open.height, fist.height);
                Assert.That(OpaqueShare(open), Is.GreaterThan(OpaqueShare(fist) * 0.8f).And.LessThan(OpaqueShare(fist) * 1.6f), "similar weight on the screen");
                Assert.AreNotEqual(OpenHandAt(open, 45f, 20f).a, OpenHandAt(fist, 45f, 20f).a);
            }
            finally
            {
                Object.DestroyImmediate(open);
                Object.DestroyImmediate(fist);
            }
        }

        static float OpaqueShare(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var count = 0;
            foreach (var p in pixels)
            {
                if (p.a > 127) count++;
            }
            return (float)count / pixels.Length;
        }

        [Test]
        public void TheCheckBadge_IsAGreenDiscWithAWhiteTick()
        {
            var badge = TutorialGlyphs.CreateCheckBadge(true);
            try
            {
                var units = TutorialGlyphs.BadgeUnits;
                Assert.AreEqual(Mathf.RoundToInt(units * Unit), badge.width);
                var disc = At(badge, 32f, 20f, units);
                Assert.AreEqual(255, disc.a);
                Assert.That(disc.g, Is.GreaterThan(disc.r + 40), "green");
                var tick = At(badge, 28f, 43f, units);
                Assert.That(tick.r, Is.GreaterThan(230), "the tick is white");
                Assert.AreEqual(0, At(badge, 1f, 1f, units).a, "round: nothing in the corner");
            }
            finally
            {
                Object.DestroyImmediate(badge);
            }
        }

        [Test]
        public void TheChevron_PointsLeft()
        {
            var chevron = TutorialGlyphs.CreateChevron(true);
            try
            {
                var high = TutorialGlyphs.ChevronUnitsHigh;
                Assert.AreEqual(255, At(chevron, 10f, high * 0.5f, high).a, "the point of the arrow is on the left");
                Assert.AreEqual(0, At(chevron, 36f, high * 0.5f, high).a, "and it is open on the right");
                Assert.That(At(chevron, 10f, high * 0.5f, high).r, Is.GreaterThan(240), "white, to be tinted by the colour of the quad");
            }
            finally
            {
                Object.DestroyImmediate(chevron);
            }
        }

        [Test]
        public void ATextureThatIsNotAskedToBeReadable_CannotBeRead()
        {
            var hand = TutorialGlyphs.CreateOpenHand();
            try
            {
                Assert.IsFalse(hand.isReadable, "the memory of the copy on the CPU is given back");
                Assert.AreEqual(TextureWrapMode.Clamp, hand.wrapMode);
                Assert.AreEqual(FilterMode.Bilinear, hand.filterMode);
            }
            finally
            {
                Object.DestroyImmediate(hand);
            }
        }
    }
}
