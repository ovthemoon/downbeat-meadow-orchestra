using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The title of the song, near the top of the meadow's picture, for a few seconds from the first bar (<c>experience.songTitleSec</c>): a TextMesh in an operating-system font that has Korean (the first of
    /// <c>record.fontNames</c> that exists), a child of the overlay layer, with a darker copy behind it so it reads on the bright meadow. It comes in and goes out the way the word of the end does (<see cref="EndWordTiming"/>),
    /// smaller and higher, and sorts just above that word on the layer (<see cref="SortingOrderAboveLayerBase"/> over the layer's base: shadow, then text).
    /// </summary>
    public sealed class SongTitleView : MonoBehaviour
    {
        /// <summary>The sorting order of the title's shadow, counted from <see cref="MeadowOverlayLayer.SortingOrderBase"/> (the text is one above); the word of the end has 30 and 31.</summary>
        public const int SortingOrderAboveLayerBase = 32;
        const int GlyphResolution = 96;
        const float TextMeshUnitsPerPoint = 0.1f;
        /// <summary>The height of the letters as a share of the height of the picture.</summary>
        const float HeightShare = 0.07f;
        /// <summary>Where the middle of the line is, as a share of the part of the picture that the record strip leaves free, counted from its bottom.</summary>
        const float CentreShare = 0.9f;
        /// <summary>A title is never wider than this share of the picture: a long one is made smaller.</summary>
        const float MaxWidthShare = 0.8f;
        const float ShadowOffsetShare = 0.045f;
        const float ShadowAlpha = 0.55f;
        static readonly Color TitleColor = new Color(0.99f, 0.96f, 0.88f, 1f);
        static readonly Color ShadowColor = new Color(0.04f, 0.06f, 0.12f, 1f);

        MeadowOverlayLayer _layer;
        Font _font;
        TextMesh _title;
        TextMesh _shadow;
        double _shownAt = double.NegativeInfinity;
        double _durationSec;
        float _alpha;
        bool _visible;

        /// <summary>The layer this title was made on (a new one is made when the layer is).</summary>
        public MeadowOverlayLayer Layer => _layer;
        /// <summary>The title is up: it was shown and its time is not over (it is still fading in in the first moment).</summary>
        public bool IsShowing => _visible;
        /// <summary>0..1, how visible the title is now.</summary>
        public float Alpha => _alpha;
        /// <summary>The text on the screen (empty until a title was set).</summary>
        public string Text => _title.text;
        public int SortingOrder => _title.GetComponent<MeshRenderer>().sortingOrder;

        public static SongTitleView Create(MeadowOverlayLayer layer, string[] fontNames)
        {
            var view = new GameObject("SongTitle").AddComponent<SongTitleView>();
            view._layer = layer;
            view.transform.SetParent(layer.Root, false);
            view._font = Font.CreateDynamicFontFromOSFont(fontNames, GlyphResolution);
            view._shadow = view.MakeText("Shadow", layer.SortingOrderBase + SortingOrderAboveLayerBase);
            view._title = view.MakeText("Title", layer.SortingOrderBase + SortingOrderAboveLayerBase + 1);
            view.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Sets the text that <see cref="Show"/> shows; its letters are put into the texture of the font now, so that they are there in the frame in which the title appears.</summary>
        public void SetTitle(string title)
        {
            var text = title ?? string.Empty;
            _title.text = text;
            _shadow.text = text;
            if (text.Length > 0) _font.RequestCharactersInTexture(text, GlyphResolution);
        }

        /// <summary>Shows the title from <paramref name="nowRealtime"/> (wall-clock seconds) for <paramref name="durationSec"/>; showing it again starts it again. With no title set, or no time, nothing shows.</summary>
        public void Show(double nowRealtime, double durationSec)
        {
            _shownAt = nowRealtime;
            _durationSec = durationSec;
            _visible = durationSec > 0.0 && _title.text.Length > 0;
            Tick(nowRealtime);
        }

        /// <summary>The visit is over, or the piece was ended: the title goes at once.</summary>
        public void Hide()
        {
            _visible = false;
            _alpha = 0f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Once per frame.</summary>
        public void Tick(double nowRealtime)
        {
            _alpha = EndWordTiming.Alpha(nowRealtime - _shownAt, _durationSec);
            if (nowRealtime - _shownAt >= _durationSec) _visible = false;
            if (!_visible)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Place();
            var title = TitleColor;
            title.a = _alpha;
            var shadow = ShadowColor;
            shadow.a = _alpha * ShadowAlpha;
            _title.color = title;
            _shadow.color = shadow;
        }

        void Place()
        {
            var free = 1f - _layer.StripFraction;
            var height = _layer.PictureHeight * HeightShare;
            var fitting = _layer.PictureHeight * _layer.Aspect * MaxWidthShare / Mathf.Max(EstimateEms(_title.text), 1f);
            var letters = Mathf.Min(height, fitting);
            var size = letters / (GlyphResolution * TextMeshUnitsPerPoint);
            _title.characterSize = size;
            _shadow.characterSize = size;
            transform.localPosition = _layer.LocalPoint(0.5f, _layer.StripFraction + free * CentreShare);
            _title.transform.localPosition = Vector3.zero;
            _shadow.transform.localPosition = new Vector3(letters * ShadowOffsetShare, -letters * ShadowOffsetShare, 0.001f);
        }

        /// <summary>About how wide a line is in ems (the height of the letters): a Korean syllable is one em wide, a space a third of that, a letter of the Latin alphabet about half.</summary>
        static float EstimateEms(string text)
        {
            var width = 0f;
            foreach (var c in text) width += c == ' ' ? 0.33f : (c < 128 ? 0.55f : 1f);
            return width;
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
            mesh.text = string.Empty;
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
