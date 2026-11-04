using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// One line of Korean on the tutorial: a TextMesh in an operating-system font (the app carries no font file). It is sized by the height of its line in the units of the scene, so that it follows the size of the picture.
    /// Only the Hangul comes from the font: the signs of the tutorial (hands, ticks, arrows) are drawn in code.
    /// </summary>
    sealed class TutorialText
    {
        public const int GlyphResolution = 96;
        const float TextMeshUnitsPerPoint = 0.1f;   // a TextMesh line is characterSize x fontSize x 0.1 units tall

        readonly GameObject _object;
        readonly TextMesh _mesh;
        bool _visible;

        public TutorialText(string name, Transform parent, Font font, int sortingOrder)
        {
            _object = new GameObject(name) { layer = parent.gameObject.layer };
            _object.transform.SetParent(parent, false);
            _mesh = _object.AddComponent<TextMesh>();
            _mesh.font = font;
            _mesh.fontSize = GlyphResolution;
            _mesh.anchor = TextAnchor.MiddleCenter;
            _mesh.alignment = TextAlignment.Center;
            var renderer = _object.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _object.SetActive(false);
        }

        public bool IsVisible => _visible;
        public string Text => _mesh.text;
        public Transform Transform => _object.transform;

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            _object.SetActive(false);
        }

        public void Show(string text, Vector3 center, float lineHeight, Color color, float alpha)
        {
            if (alpha <= TutorialQuad.InvisibleAlpha || string.IsNullOrEmpty(text)) { Hide(); return; }
            if (_mesh.text != text) _mesh.text = text;
            _mesh.characterSize = lineHeight / (GlyphResolution * TextMeshUnitsPerPoint);
            _mesh.color = new Color(color.r, color.g, color.b, alpha);
            _object.transform.localPosition = center;
            if (_visible) return;
            _visible = true;
            _object.SetActive(true);
        }
    }
}
