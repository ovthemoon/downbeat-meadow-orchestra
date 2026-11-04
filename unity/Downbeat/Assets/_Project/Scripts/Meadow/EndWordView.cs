using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The word "끝" in the middle of the meadow's picture for <c>cutoff.endWordSec</c> after the piece was ended (with the hands, or at the end of a song): a TextMesh in an operating-system font that has Korean (the first of
    /// <c>record.fontNames</c> that exists; the app carries no font file), a child of the overlay layer, with a darker copy behind it so it reads on the bright meadow. It sorts above everything else on the
    /// layer (<see cref="SortingOrderAboveLayerBase"/> over the layer's base: the follow-along guide and the note trail are below it).
    /// </summary>
    public sealed class EndWordView : MonoBehaviour
    {
        public const string Word = "끝";
        /// <summary>The sorting order of the word, counted from <see cref="MeadowOverlayLayer.SortingOrderBase"/>.</summary>
        public const int SortingOrderAboveLayerBase = 30;
        const int GlyphResolution = 96;
        const float TextMeshUnitsPerPoint = 0.1f;
        /// <summary>The height of the word as a share of the height of the picture.</summary>
        const float HeightShare = 0.17f;   // 57% of the 0.3 it was: a word, not a wall
        const float ShadowOffsetShare = 0.035f;
        const float ShadowAlpha = 0.55f;
        static readonly Color WordColor = new Color(0.99f, 0.96f, 0.88f, 1f);
        static readonly Color ShadowColor = new Color(0.04f, 0.06f, 0.12f, 1f);

        MeadowOverlayLayer _layer;
        Font _font;
        TextMesh _word;
        TextMesh _shadow;
        double _shownAt = double.NegativeInfinity;
        double _durationSec;
        float _alpha;
        bool _visible;

        /// <summary>The layer this word was made on (a new one is made when the layer is).</summary>
        public MeadowOverlayLayer Layer => _layer;
        /// <summary>The word is up: it was shown and its time is not over (it is still fading in in the first moment).</summary>
        public bool IsShowing => _visible;
        /// <summary>0..1, how visible the word is now.</summary>
        public float Alpha => _alpha;
        /// <summary>The text on the screen.</summary>
        public string Text => _word.text;
        public int SortingOrder => _word.GetComponent<MeshRenderer>().sortingOrder;

        public static EndWordView Create(MeadowOverlayLayer layer, string[] fontNames)
        {
            var view = new GameObject("EndWord").AddComponent<EndWordView>();
            view._layer = layer;
            view.transform.SetParent(layer.Root, false);
            view._font = Font.CreateDynamicFontFromOSFont(fontNames, GlyphResolution);
            view._font.RequestCharactersInTexture(Word, GlyphResolution);   // the glyph is in the font's texture before the word is first shown
            view._shadow = view.MakeText("Shadow", layer.SortingOrderBase + SortingOrderAboveLayerBase);
            view._word = view.MakeText("Word", layer.SortingOrderBase + SortingOrderAboveLayerBase + 1);
            view.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Shows the word from <paramref name="now"/> (wall-clock seconds) for <paramref name="durationSec"/>; showing it again starts it again.</summary>
        public void Show(double now, double durationSec)
        {
            _shownAt = now;
            _durationSec = durationSec;
            _visible = durationSec > 0.0;
            Tick(now);
        }

        /// <summary>Once per frame.</summary>
        public void Tick(double now)
        {
            _alpha = EndWordTiming.Alpha(now - _shownAt, _durationSec);
            if (now - _shownAt >= _durationSec) _visible = false;
            if (!_visible)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Place();
            var word = WordColor;
            word.a = _alpha;
            var shadow = ShadowColor;
            shadow.a = _alpha * ShadowAlpha;
            _word.color = word;
            _shadow.color = shadow;
        }

        void Place()
        {
            // the middle of the part of the picture that the record strip leaves free
            var centreY = _layer.StripFraction + (1f - _layer.StripFraction) * 0.5f;
            var height = _layer.PictureHeight * HeightShare;
            var size = height / (GlyphResolution * TextMeshUnitsPerPoint);
            _word.characterSize = size;
            _shadow.characterSize = size;
            transform.localPosition = _layer.LocalPoint(0.5f, centreY);
            _word.transform.localPosition = Vector3.zero;
            _shadow.transform.localPosition = new Vector3(height * ShadowOffsetShare, -height * ShadowOffsetShare, 0.001f);
        }

        TextMesh MakeText(string name, int sortingOrder)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(transform, false);
            var mesh = textObject.AddComponent<TextMesh>();
            mesh.font = _font;
            mesh.fontSize = GlyphResolution;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.text = Word;
            var renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        void OnDestroy()
        {
            if (_font != null) Destroy(_font);
        }
    }
}
