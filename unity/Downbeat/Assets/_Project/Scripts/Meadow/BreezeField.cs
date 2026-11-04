using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The gusts and cloud shadows of the steady wind, worked out once per frame for a grid of ground points and kept in a small half-float picture (see MeadowBreeze.hlsl and
    /// Resources/Breeze/BreezeBake.shader): the grass, the flowers, the ground and the painted far meadow read it instead of each working out the gusts themselves. The grid covers 128 x 96 m
    /// at half a metre a texel (a gust is metres wide); outside it (very far away) the plate does not ripple.
    /// </summary>
    public sealed class BreezeField : System.IDisposable
    {
        public const int Width = 256;
        public const int Height = 192;
        public const float OriginX = -64f;
        public const float OriginZ = -4f;
        public const float SizeX = 128f;
        public const float SizeZ = 96f;
        const string ShaderPath = "Breeze/BreezeBake";
        /// <summary>The field is worked out at most this often: gusts move at a few metres a second, so a texel (half a metre) takes about a fifth of a second to cross, and a faster rate shows nothing.</summary>
        public const double MinBakeIntervalSec = 1.0 / 60.0;

        static readonly int TextureId = Shader.PropertyToID("_BreezeFieldTex");
        static readonly int RectId = Shader.PropertyToID("_BreezeFieldRect");

        readonly RenderTexture _texture;
        readonly Material _material;
        double _lastBakeTime = double.NegativeInfinity;

        public BreezeField()
        {
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null) throw new System.InvalidOperationException($"the shader Resources/{ShaderPath} is missing");
            _material = new Material(shader) { name = "BreezeBake", hideFlags = HideFlags.HideAndDontSave };
            _texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "BreezeField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false,
            };
            _texture.Create();
            Shader.SetGlobalTexture(TextureId, _texture);
            Shader.SetGlobalVector(RectId, new Vector4(OriginX, OriginZ, 1f / SizeX, 1f / SizeZ));
        }

        public RenderTexture Texture => _texture;

        /// <summary>Works the field out for the wind that the shaders' globals describe now (call it after <see cref="BreezeGpu.Upload"/>).</summary>
        public void Bake() => Graphics.Blit(null, _texture, _material);

        /// <summary>Works the field out unless it was done less than <see cref="MinBakeIntervalSec"/> ago (scene time, seconds); a frame that comes sooner reads the same field again.</summary>
        public void BakeAt(double timeSec)
        {
            if (timeSec - _lastBakeTime < MinBakeIntervalSec && timeSec >= _lastBakeTime) return;
            _lastBakeTime = timeSec;
            Bake();
        }

        public void Dispose()
        {
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            if (_material != null) Destroy(_material);
        }

        static void Destroy(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
