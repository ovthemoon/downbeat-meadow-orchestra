using Downbeat.Core;
using UnityEngine;

using System.Collections.Generic;

namespace Downbeat.Meadow
{
    /// <summary>The picture of one trail note (a quaver, white inside a nearly black outline and halo, see <see cref="NoteGlyphShape"/>) made in code: no picture file, no font. The view tints it with the colour of the cell.</summary>
    public static class NoteGlyphTexture
    {
        public const int Size = 128;

        /// <param name="readable">Keep the pixels readable (for a test); the views let them go once they are on the graphics card.</param>
        public static Texture2D Create(bool readable = false)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "NoteGlyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var alpha = NoteGlyphShape.Sample((x + 0.5f) / Size, (y + 0.5f) / Size, 1f / Size, out var grey);
                    var level = (byte)Mathf.RoundToInt(grey * 255f);
                    pixels[y * Size + x] = new Color32(level, level, level, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, !readable);
            return texture;
        }
    }

    /// <summary>
    /// The picture of the frame of a cell (see <see cref="CellFrameShape"/>): brightest at the edge of the square, falling off towards the middle, drawn white and tinted by the cell. The cell is built from nine
    /// pieces of it (the corners keep their size, the sides are stretched), so the edge is as thick on the long side of a cell as on the short one.
    /// </summary>
    public static class CellFrameTexture
    {
        public const int Size = 64;

        public static Texture2D Create(bool readable = false)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "CellFrame", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = (x + 0.5f) / Size;
                    var v = (y + 0.5f) / Size;
                    var distance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 2f;
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(CellFrameShape.Alpha(distance) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }
    }
}

namespace Downbeat.Meadow
{
    /// <summary>What the flat pictures drawn over the meadow (trail notes, cell frames) are made of: the shader of the record screen's quads (vertex colour times a texture, alpha-blended, no depth, no light), and the way to give anything they made back.</summary>
    static class OverlayAssets
    {
        const string ShaderPath = "Record/RecordQuad";

        public static Material QuadMaterial(string name, Texture2D texture)
        {
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null) throw new System.InvalidOperationException($"the shader Resources/{ShaderPath} is missing from the build");
            return new Material(shader) { name = name, mainTexture = texture };
        }

        public static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }

    /// <summary>
    /// The operating-system font of the cell names, made once for a list of names and size and kept: a font that is made again for every build of the meadow (a content reload, a change of tier) would leave its
    /// atlas texture and material behind when it is destroyed, and the names of the cells are the same in every build.
    /// </summary>
    static class OverlayFonts
    {
        static readonly Dictionary<string, Font> Made = new Dictionary<string, Font>();

        public static Font Get(string[] names, int size)
        {
            var key = size + "|" + string.Join("|", names);
            if (Made.TryGetValue(key, out var font) && font != null) return font;
            font = Font.CreateDynamicFontFromOSFont(names, size);
            Made[key] = font;
            return font;
        }
    }
}
