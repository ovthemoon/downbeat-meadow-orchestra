using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// One flat quad of the tutorial: a texture tinted by a colour, or a shape worked out by the shader (a rounded box with a fill and a border, which also makes discs, rings and bars). It is placed and scaled by its transform and
    /// coloured by a property block, so that many quads share a handful of materials. Colours are given as sRGB (like the colours of the content) and sent to the shader as linear.
    /// </summary>
    sealed class TutorialQuad
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int BorderId = Shader.PropertyToID("_Border");
        static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        static readonly int BorderAlphaId = Shader.PropertyToID("_BorderAlpha");

        /// <summary>Below this alpha a quad is not worth drawing.</summary>
        public const float InvisibleAlpha = 0.004f;

        readonly MeshRenderer _renderer;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        bool _visible;

        public TutorialQuad(string name, Transform parent, Mesh mesh, Material material, int sortingOrder)
        {
            GameObject = new GameObject(name) { layer = parent.gameObject.layer };
            Transform = GameObject.transform;
            Transform.SetParent(parent, false);
            GameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = GameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObject.SetActive(false);
        }

        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public bool IsVisible => _visible;

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            GameObject.SetActive(false);
        }

        /// <summary>The texture of the material times <paramref name="tint"/>, <paramref name="size"/> big, turned round left to right when <paramref name="mirror"/>.</summary>
        public void DrawTexture(Vector3 center, Vector2 size, Color tint, float alpha, bool mirror = false)
        {
            if (alpha <= InvisibleAlpha) { Hide(); return; }
            Place(center, new Vector2(mirror ? -size.x : size.x, size.y));
            _block.SetFloat(ShapeId, 0f);
            _block.SetVector(ColorId, Linear(tint, alpha));
            Apply();
        }

        /// <summary>A rounded box: filled with <paramref name="fillAlpha"/>, with a border of <paramref name="border"/> units and <paramref name="borderAlpha"/> (0 units: no border). All of it times <paramref name="alpha"/>.</summary>
        public void DrawBox(Vector3 center, Vector2 size, float radius, Color color, float alpha, float fillAlpha, float border = 0f, float borderAlpha = 1f)
        {
            if (alpha <= InvisibleAlpha || (fillAlpha <= 0f && (border <= 0f || borderAlpha <= 0f))) { Hide(); return; }
            Place(center, size);
            _block.SetFloat(ShapeId, 1f);
            _block.SetVector(SizeId, new Vector4(size.x, size.y, 0f, 0f));
            _block.SetFloat(RadiusId, radius);
            _block.SetFloat(BorderId, border);
            _block.SetFloat(FillAlphaId, fillAlpha);
            _block.SetFloat(BorderAlphaId, borderAlpha);
            _block.SetVector(ColorId, Linear(color, alpha));
            Apply();
        }

        /// <summary>A disc (or an ellipse) is a box whose corners are as round as can be.</summary>
        public void DrawDisc(Vector3 center, float diameter, Color color, float alpha) =>
            DrawBox(center, new Vector2(diameter, diameter), diameter * 0.5f, color, alpha, 1f);

        /// <summary>A ring: a disc with nothing in it and a border.</summary>
        public void DrawRing(Vector3 center, float diameter, float width, Color color, float alpha) =>
            DrawBox(center, new Vector2(diameter, diameter), diameter * 0.5f, color, alpha, 0f, width, 1f);

        void Place(Vector3 center, Vector2 size)
        {
            Transform.localPosition = center;
            Transform.localRotation = Quaternion.identity;
            Transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        void Apply()
        {
            _renderer.SetPropertyBlock(_block);
            if (_visible) return;
            _visible = true;
            GameObject.SetActive(true);
        }

        static Vector4 Linear(Color srgb, float alpha)
        {
            var linear = srgb.linear;
            return new Vector4(linear.r, linear.g, linear.b, alpha);
        }
    }
}
