using System;
using System.Collections.Generic;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// A small painter of flat vector shapes into a texture: rounded boxes (turned if need be), thick polylines and discs, each with a fill and an outline, laid one over the other in the order they are added, the way the
    /// shapes of an SVG drawing are. Every shape is a distance function, so the edges are smooth at any size. The units of the drawing are its own (a 100 x 120 hand), <c>pixelsPerUnit</c> says how many pixels one unit gets.
    /// Used for the glyphs of the tutorial, which must not depend on a font for their pictures.
    /// </summary>
    sealed class GlyphPainter
    {
        /// <summary>A shape, as the distance from a point to its edge (negative inside) and the box it lives in (outline included).</summary>
        abstract class Shape
        {
            public float MinX, MinY, MaxX, MaxY;
            public abstract float Distance(float x, float y);
        }

        sealed class RoundBoxShape : Shape
        {
            readonly float _cx, _cy, _hx, _hy, _radius, _cos, _sin, _pivotX, _pivotY;

            public RoundBoxShape(float x, float y, float width, float height, float radius, float turnDegrees, float pivotX, float pivotY)
            {
                _cx = x + width * 0.5f;
                _cy = y + height * 0.5f;
                _hx = width * 0.5f;
                _hy = height * 0.5f;
                _radius = Mathf.Min(radius, Mathf.Min(_hx, _hy));
                var turn = turnDegrees * Mathf.Deg2Rad;
                _cos = Mathf.Cos(turn);
                _sin = Mathf.Sin(turn);
                _pivotX = pivotX;
                _pivotY = pivotY;
                MinX = float.MaxValue; MinY = float.MaxValue; MaxX = float.MinValue; MaxY = float.MinValue;
                foreach (var corner in new[] { new Vector2(x, y), new Vector2(x + width, y), new Vector2(x, y + height), new Vector2(x + width, y + height) })
                {
                    // the box turned by +angle about the pivot (clockwise on a drawing with y downwards, like SVG's rotate)
                    var dx = corner.x - pivotX;
                    var dy = corner.y - pivotY;
                    var tx = pivotX + dx * _cos - dy * _sin;
                    var ty = pivotY + dx * _sin + dy * _cos;
                    MinX = Mathf.Min(MinX, tx); MaxX = Mathf.Max(MaxX, tx);
                    MinY = Mathf.Min(MinY, ty); MaxY = Mathf.Max(MaxY, ty);
                }
            }

            public override float Distance(float x, float y)
            {
                // into the frame of the box: turned back by the angle
                var dx = x - _pivotX;
                var dy = y - _pivotY;
                var lx = _pivotX + dx * _cos + dy * _sin;
                var ly = _pivotY - dx * _sin + dy * _cos;
                var qx = Mathf.Abs(lx - _cx) - _hx + _radius;
                var qy = Mathf.Abs(ly - _cy) - _hy + _radius;
                var outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - _radius;
            }
        }

        sealed class PolylineShape : Shape
        {
            readonly Vector2[] _points;

            public PolylineShape(IReadOnlyList<Vector2> points)
            {
                _points = new Vector2[points.Count];
                MinX = float.MaxValue; MinY = float.MaxValue; MaxX = float.MinValue; MaxY = float.MinValue;
                for (var i = 0; i < _points.Length; i++)
                {
                    _points[i] = points[i];
                    MinX = Mathf.Min(MinX, points[i].x); MaxX = Mathf.Max(MaxX, points[i].x);
                    MinY = Mathf.Min(MinY, points[i].y); MaxY = Mathf.Max(MaxY, points[i].y);
                }
            }

            public override float Distance(float x, float y)
            {
                var best = float.MaxValue;
                for (var i = 0; i + 1 < _points.Length; i++)
                {
                    var a = _points[i];
                    var b = _points[i + 1];
                    var abx = b.x - a.x;
                    var aby = b.y - a.y;
                    var length2 = abx * abx + aby * aby;
                    var t = length2 <= 0f ? 0f : Mathf.Clamp01(((x - a.x) * abx + (y - a.y) * aby) / length2);
                    var dx = x - (a.x + abx * t);
                    var dy = y - (a.y + aby * t);
                    best = Mathf.Min(best, dx * dx + dy * dy);
                }
                return Mathf.Sqrt(best);
            }
        }

        sealed class DiscShape : Shape
        {
            readonly float _cx, _cy, _radius;

            public DiscShape(float cx, float cy, float radius)
            {
                _cx = cx;
                _cy = cy;
                _radius = radius;
                MinX = cx - radius; MaxX = cx + radius; MinY = cy - radius; MaxY = cy + radius;
            }

            public override float Distance(float x, float y) => Mathf.Sqrt((x - _cx) * (x - _cx) + (y - _cy) * (y - _cy)) - _radius;
        }

        readonly int _width;
        readonly int _height;
        readonly float _unitsHigh;
        readonly float _pixelsPerUnit;
        readonly Color[] _pixels;   // straight (not premultiplied) colour and alpha

        public GlyphPainter(float unitsWide, float unitsHigh, float pixelsPerUnit)
        {
            _width = Mathf.RoundToInt(unitsWide * pixelsPerUnit);
            _height = Mathf.RoundToInt(unitsHigh * pixelsPerUnit);
            _unitsHigh = unitsHigh;
            _pixelsPerUnit = pixelsPerUnit;
            _pixels = new Color[_width * _height];
        }

        /// <summary>A box with rounded corners; turned by <paramref name="turnDegrees"/> (clockwise on the drawing) about the pivot.</summary>
        public GlyphPainter RoundBox(float x, float y, float width, float height, float radius, Color? fill, Color? outline, float outlineWidth = 0f, float turnDegrees = 0f, float pivotX = 0f, float pivotY = 0f) =>
            Paint(new RoundBoxShape(x, y, width, height, radius, turnDegrees, pivotX, pivotY), fill, outline, outlineWidth);

        public GlyphPainter Disc(float cx, float cy, float radius, Color? fill, Color? outline, float outlineWidth = 0f) =>
            Paint(new DiscShape(cx, cy, radius), fill, outline, outlineWidth);

        /// <summary>A line of the given width with round ends through the points.</summary>
        public GlyphPainter Line(IReadOnlyList<Vector2> points, Color color, float width) => Paint(new PolylineShape(points), null, color, width);

        /// <summary>A line along a curve that goes through the control points as the Q commands of an SVG path do (a start, then pairs of control point and end point).</summary>
        public GlyphPainter QuadraticCurve(IReadOnlyList<Vector2> startControlEndPairs, Color color, float width)
        {
            const int SegmentsPerCurve = 12;
            var points = new List<Vector2> { startControlEndPairs[0] };
            for (var i = 1; i + 1 < startControlEndPairs.Count; i += 2)
            {
                var p0 = points[points.Count - 1];
                var p1 = startControlEndPairs[i];
                var p2 = startControlEndPairs[i + 1];
                for (var s = 1; s <= SegmentsPerCurve; s++)
                {
                    var t = s / (float)SegmentsPerCurve;
                    var u = 1f - t;
                    points.Add(u * u * p0 + 2f * u * t * p1 + t * t * p2);
                }
            }
            return Line(points, color, width);
        }

        GlyphPainter Paint(Shape shape, Color? fill, Color? outline, float outlineWidth)
        {
            var reach = outline.HasValue ? outlineWidth * 0.5f : 0f;
            var margin = reach + 1.5f / _pixelsPerUnit;   // an edge pixel or two beyond the line
            var x0 = Mathf.Max(0, Mathf.FloorToInt((shape.MinX - margin) * _pixelsPerUnit));
            var x1 = Mathf.Min(_width - 1, Mathf.CeilToInt((shape.MaxX + margin) * _pixelsPerUnit));
            // the drawing has y downwards, the texture has row 0 at the bottom
            var y0 = Mathf.Max(0, Mathf.FloorToInt((_unitsHigh - shape.MaxY - margin) * _pixelsPerUnit));
            var y1 = Mathf.Min(_height - 1, Mathf.CeilToInt((_unitsHigh - shape.MinY + margin) * _pixelsPerUnit));
            var pixel = 1f / _pixelsPerUnit;

            for (var row = y0; row <= y1; row++)
            {
                var y = _unitsHigh - (row + 0.5f) * pixel;
                for (var column = x0; column <= x1; column++)
                {
                    var x = (column + 0.5f) * pixel;
                    var d = shape.Distance(x, y);
                    var index = row * _width + column;
                    if (fill.HasValue) Over(index, fill.Value, Coverage(d, pixel));
                    if (outline.HasValue) Over(index, outline.Value, Coverage(Mathf.Abs(d) - outlineWidth * 0.5f, pixel));
                }
            }
            return this;
        }

        static float Coverage(float distance, float pixel) => Mathf.Clamp01(0.5f - distance / pixel);

        /// <summary>The colour laid over what is there, with the given share of the pixel (the usual "over").</summary>
        void Over(int index, Color color, float coverage)
        {
            var sourceAlpha = color.a * coverage;
            if (sourceAlpha <= 0f) return;
            var below = _pixels[index];
            var outAlpha = sourceAlpha + below.a * (1f - sourceAlpha);
            var weightBelow = below.a * (1f - sourceAlpha);
            _pixels[index] = new Color(
                (color.r * sourceAlpha + below.r * weightBelow) / outAlpha,
                (color.g * sourceAlpha + below.g * weightBelow) / outAlpha,
                (color.b * sourceAlpha + below.b * weightBelow) / outAlpha,
                outAlpha);
        }

        /// <summary>
        /// The picture as a texture (clamped, bilinear). Not readable afterwards unless asked: the copy on the processor is not needed to draw it. The pixels with nothing in them get the colour <paramref name="edgeColour"/> (and
        /// no alpha), so that the smoothing between a pixel of the drawing and an empty one does not pull the edge towards black.
        /// </summary>
        public Texture2D ToTexture(string name, bool readable, Color edgeColour)
        {
            var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var bytes = new Color32[_pixels.Length];
            var empty = new Color(edgeColour.r, edgeColour.g, edgeColour.b, 0f);
            for (var i = 0; i < bytes.Length; i++) bytes[i] = _pixels[i].a <= 0f ? empty : _pixels[i];
            texture.SetPixels32(bytes);
            texture.Apply(false, !readable);
            return texture;
        }
    }
}
