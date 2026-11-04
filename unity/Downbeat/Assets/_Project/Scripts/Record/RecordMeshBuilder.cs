using System.Collections.Generic;
using UnityEngine;

namespace Downbeat.Record
{
    /// <summary>
    /// Collects coloured, textured quads (in logical pixels, y up) into one mesh. One builder per texture; <see cref="Upload"/> hands them to a mesh that is drawn in a single call.
    /// Colours are linear (the project is in linear colour space and vertex colours are not converted).
    /// </summary>
    public sealed class RecordMeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1024);
        readonly List<Color> _colors = new List<Color>(1024);
        readonly List<Vector2> _uvs = new List<Vector2>(1024);
        readonly List<int> _triangles = new List<int>(1536);
        // the corners of a rounded rectangle, kept so that drawing one makes no garbage
        readonly float[] _xs = new float[4];
        readonly float[] _ys = new float[4];
        readonly float[] _us = { 0f, RoundedRectTexture.CornerShare, 1f - RoundedRectTexture.CornerShare, 1f };

        public int QuadCount => _vertices.Count / 4;

        /// <summary>The colour of the bottom left corner of a quad made so far (what the quad was drawn with; tests).</summary>
        public Color QuadColor(int quadIndex) => _colors[quadIndex * 4];

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _triangles.Clear();
        }

        /// <summary>An axis-aligned quad with the texture area <paramref name="uvMin"/>..<paramref name="uvMax"/>.</summary>
        public void Quad(float x, float y, float width, float height, Color color, Vector2 uvMin, Vector2 uvMax) =>
            Quad(x, y, width, height, color, color, color, color, uvMin, uvMax);

        /// <summary>A quad with a colour per corner (bottom left, bottom right, top right, top left): gradients and fades.</summary>
        public void Quad(float x, float y, float width, float height, Color bottomLeft, Color bottomRight, Color topRight, Color topLeft, Vector2 uvMin, Vector2 uvMax)
        {
            var i = _vertices.Count;
            _vertices.Add(new Vector3(x, y, 0f));
            _vertices.Add(new Vector3(x + width, y, 0f));
            _vertices.Add(new Vector3(x + width, y + height, 0f));
            _vertices.Add(new Vector3(x, y + height, 0f));
            _colors.Add(bottomLeft);
            _colors.Add(bottomRight);
            _colors.Add(topRight);
            _colors.Add(topLeft);
            _uvs.Add(new Vector2(uvMin.x, uvMin.y));
            _uvs.Add(new Vector2(uvMax.x, uvMin.y));
            _uvs.Add(new Vector2(uvMax.x, uvMax.y));
            _uvs.Add(new Vector2(uvMin.x, uvMax.y));
            _triangles.Add(i);
            _triangles.Add(i + 2);
            _triangles.Add(i + 1);
            _triangles.Add(i);
            _triangles.Add(i + 3);
            _triangles.Add(i + 2);
        }

        /// <summary>A quad turned by <paramref name="radians"/> (counter-clockwise, y up) round its middle: the petals.</summary>
        public void Rotated(float cx, float cy, float width, float height, float radians, Color color, Vector2 uvMin, Vector2 uvMax)
        {
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            var hx = width * 0.5f;
            var hy = height * 0.5f;
            var i = _vertices.Count;
            _vertices.Add(new Vector3(cx - hx * cos + hy * sin, cy - hx * sin - hy * cos, 0f));
            _vertices.Add(new Vector3(cx + hx * cos + hy * sin, cy + hx * sin - hy * cos, 0f));
            _vertices.Add(new Vector3(cx + hx * cos - hy * sin, cy + hx * sin + hy * cos, 0f));
            _vertices.Add(new Vector3(cx - hx * cos - hy * sin, cy - hx * sin + hy * cos, 0f));
            for (var k = 0; k < 4; k++) _colors.Add(color);
            _uvs.Add(new Vector2(uvMin.x, uvMin.y));
            _uvs.Add(new Vector2(uvMax.x, uvMin.y));
            _uvs.Add(new Vector2(uvMax.x, uvMax.y));
            _uvs.Add(new Vector2(uvMin.x, uvMax.y));
            _triangles.Add(i);
            _triangles.Add(i + 2);
            _triangles.Add(i + 1);
            _triangles.Add(i);
            _triangles.Add(i + 3);
            _triangles.Add(i + 2);
        }

        /// <summary>A plain coloured quad (the texture of the material should be white or a mask that is 1 at <c>uv (0.5, 0.5)</c>).</summary>
        public void Solid(float x, float y, float width, float height, Color color) => Quad(x, y, width, height, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        /// <summary>A quad centred on a point, <paramref name="size"/> across.</summary>
        public void Centered(float cx, float cy, float size, Color color, Vector2 uvMin, Vector2 uvMax) => Quad(cx - size * 0.5f, cy - size * 0.5f, size, size, color, uvMin, uvMax);

        /// <summary>A rounded rectangle from a rounded-corner texture: nine quads (the corners keep their size, the edges and the middle stretch).</summary>
        public void RoundedRect(float x, float y, float width, float height, float radius, Color color)
        {
            var r = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);
            _xs[0] = x; _xs[1] = x + r; _xs[2] = x + width - r; _xs[3] = x + width;
            _ys[0] = y; _ys[1] = y + r; _ys[2] = y + height - r; _ys[3] = y + height;
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    Quad(_xs[column], _ys[row], _xs[column + 1] - _xs[column], _ys[row + 1] - _ys[row], color, new Vector2(_us[column], _us[row]), new Vector2(_us[column + 1], _us[row + 1]));
                }
            }
        }

        public void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100f)); // never culled: the quads are placed in logical pixels
        }
    }

    /// <summary>A white rounded square with a soft anti-aliased edge, made in code: the corner is <see cref="CornerShare"/> of its size.</summary>
    public static class RoundedRectTexture
    {
        public const float CornerShare = 0.25f;
        const int Size = 64;

        public static Texture2D Create()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "RoundedRect", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            var radius = Size * CornerShare;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var cx = Mathf.Clamp(px, radius, Size - radius);
                    var cy = Mathf.Clamp(py, radius, Size - radius);
                    var distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    var a = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
