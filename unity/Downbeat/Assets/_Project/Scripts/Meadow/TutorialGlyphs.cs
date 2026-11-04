using System.Collections.Generic;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The pictures of the tutorial, painted in code: an open hand (a palm and five fingers), a fist (a rounded block with knuckle lines and a thumb across it), a green tick badge and a white arrow head. The two hands are
    /// the drawings the owner approved in the mock-up, shape for shape. No picture file and no font is needed, so no operating system can change how they look. The white arrow head is tinted by the colour of the quad it is drawn on.
    /// </summary>
    public static class TutorialGlyphs
    {
        public const float PixelsPerUnit = 3f;
        public const float HandUnitsWide = 100f;
        public const float HandUnitsHigh = 120f;
        public const float BadgeUnits = 64f;
        public const float ChevronUnitsWide = 40f;
        public const float ChevronUnitsHigh = 64f;

        static readonly Color Outline = Hex(0x8A5A2B);
        static readonly Color OpenFill = Hex(0xFFF4E3);
        static readonly Color FistFill = Hex(0xFFE9CF);
        const float OutlineWidth = 2.5f;

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        /// <summary>The open hand, fingers up, thumb to the left: 100 x 120 units.</summary>
        public static Texture2D CreateOpenHand(bool readable = false)
        {
            var painter = new GlyphPainter(HandUnitsWide, HandUnitsHigh, PixelsPerUnit);
            painter
                .RoundBox(27f, 14f, 11f, 46f, 5.5f, OpenFill, Outline, OutlineWidth)
                .RoundBox(40f, 6f, 11f, 52f, 5.5f, OpenFill, Outline, OutlineWidth)
                .RoundBox(53f, 10f, 11f, 48f, 5.5f, OpenFill, Outline, OutlineWidth)
                .RoundBox(66f, 22f, 10f, 38f, 5f, OpenFill, Outline, OutlineWidth)
                .RoundBox(9f, 56f, 11f, 34f, 5.5f, OpenFill, Outline, OutlineWidth, -38f, 14f, 73f)   // the thumb
                .RoundBox(24f, 50f, 54f, 56f, 18f, OpenFill, Outline, OutlineWidth)                   // the palm
                .RoundBox(27f, 48f, 48f, 14f, 0f, OpenFill, null);                                   // over the top of the palm, where the fingers join it
            return painter.ToTexture("TutorialOpenHand", readable, Outline);
        }

        /// <summary>The fist: a rounded block with the knuckles on its top edge and the thumb across the front: the same 100 x 120 box as the open hand.</summary>
        public static Texture2D CreateFist(bool readable = false)
        {
            var painter = new GlyphPainter(HandUnitsWide, HandUnitsHigh, PixelsPerUnit);
            painter.RoundBox(20f, 36f, 60f, 62f, 20f, FistFill, Outline, OutlineWidth);
            painter.QuadraticCurve(new[] { new Vector2(24f, 52f), new Vector2(35f, 44f), new Vector2(46f, 52f), new Vector2(57f, 44f), new Vector2(68f, 52f), new Vector2(74f, 47f), new Vector2(78f, 52f) }, Outline, OutlineWidth);
            foreach (var x in new[] { 35f, 50f, 64f }) painter.Line(new List<Vector2> { new Vector2(x, 40f), new Vector2(x, 56f) }, Outline, 2f);
            painter.RoundBox(18f, 64f, 44f, 15f, 7.5f, OpenFill, Outline, OutlineWidth);   // the thumb
            return painter.ToTexture("TutorialFist", readable, Outline);
        }

        /// <summary>A green disc with a white tick: "done".</summary>
        public static Texture2D CreateCheckBadge(bool readable = false)
        {
            var painter = new GlyphPainter(BadgeUnits, BadgeUnits, PixelsPerUnit);
            painter.Disc(BadgeUnits * 0.5f, BadgeUnits * 0.5f, 29f, Hex(0x2E7D4F), Color.white, 3f);
            painter.Line(new List<Vector2> { new Vector2(18f, 33f), new Vector2(28f, 43f), new Vector2(46f, 22f) }, Color.white, 7f);
            return painter.ToTexture("TutorialCheckBadge", readable, Color.white);
        }

        /// <summary>An arrow head pointing left, white: set the colour on the quad, and turn the quad round for an arrow to the right.</summary>
        public static Texture2D CreateChevron(bool readable = false)
        {
            var painter = new GlyphPainter(ChevronUnitsWide, ChevronUnitsHigh, PixelsPerUnit);
            painter.Line(new List<Vector2> { new Vector2(30f, 8f), new Vector2(10f, 32f), new Vector2(30f, 56f) }, Color.white, 9f);
            return painter.ToTexture("TutorialChevron", readable, Color.white);
        }
    }
}
