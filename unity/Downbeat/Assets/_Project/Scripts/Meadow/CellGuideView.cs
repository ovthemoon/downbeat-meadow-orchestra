using System;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Tells the player which of the six cells (three columns, two rows) a hand is in. The grid is not drawn: nothing shows until a hand is in a cell, and then the border of that cell glows softly, all the time the
    /// hand stays in it, and the name of the cell (a TextMesh in an operating-system font that has Korean, the first of the given names that exists: the app carries no font file; the font is made once and shared) shows once, when the hand comes in,
    /// for <c>trail.labelSec</c> seconds and then fades out. When a hand moves on to another cell, that cell glows and shows its name. What shows when is worked out by <see cref="CellLabelTimer"/>; where the
    /// cells are by <see cref="CellGuideLayout"/>. The frames are one mesh of nine pieces a cell, one draw call; nothing is drawn while no cell glows and no name shows.
    /// </summary>
    public sealed class CellGuideView : MonoBehaviour
    {
        public const int Cells = CellGuideLayout.CellCount;
        const int GlyphResolution = 96;
        const float TextMeshUnitsPerPoint = 0.1f;
        const int PiecesPerCell = 9;
        const int GridSide = 4;
        // sizes as shares of the height of the picture
        const float LabelHeight = 0.05f;
        const float LabelFromBottom = 0.16f; // of the height of the cell
        const float FrameThickness = 0.03f;
        const float CellGap = 0.008f;
        const float ShadowOffset = 0.0025f;
        const float FrameAlpha = 0.6f;
        const float ShadowAlpha = 0.6f;
        const float LabelWhiteMix = 0.35f;
        const float SmallestAlpha = 0.004f;
        static readonly string[] FallbackFonts = { "Arial" };

        Func<float, float, Vector3> _toLocal;
        CellLabelTimer _timer;
        double _now;
        Color[] _srgb;
        Color32[] _linear;
        Texture2D _frameTexture;
        Material _frameMaterial;
        Mesh _mesh;
        MeshRenderer _renderer;
        Vector3[] _vertices;
        Color32[] _colors;
        Vector2[] _uvs;
        int[] _indices;
        Font _font;
        TextMesh[] _labels;
        TextMesh[] _shadows;
        readonly float[] _xs = new float[GridSide];
        readonly float[] _ys = new float[GridSide];
        int _visibleLabels;

        public bool IsConfigured => _toLocal != null;

        /// <summary>The height of the strip at the bottom of the picture that the cells do not cover (<c>display.overlayFraction</c> while the record strip is shown on this screen, else 0). Read at every <see cref="Tick"/>: the strip can be hidden while running.</summary>
        public float OverlayFraction { get; set; }

        /// <summary>The viewport y of the horizon, where the two rows of cells meet (<see cref="HandScreenMap"/>): the top row is the sky above it, the bottom row the ground below it; NaN: half way between the strip and the top. Read at every <see cref="Tick"/>.</summary>
        public float HorizonViewport { get; set; } = float.NaN;

        /// <summary>How many names were showing (even faintly) in the last <see cref="Tick"/>.</summary>
        public int VisibleLabelCount => _visibleLabels;

        /// <summary>How visible the name of a cell is now, 0..1.</summary>
        public float LabelAlpha(int cell) => _timer == null ? 0f : _timer.LabelAlpha(cell, _now);

        /// <summary>How brightly the border of a cell glows now, 0..1.</summary>
        public float Glow(int cell) => _timer == null ? 0f : _timer.Glow(cell);

        /// <summary>The colour (sRGB) of a cell: the notes of a hand in the cell have it.</summary>
        public Color ColorOf(int cell) => cell >= 0 && cell < Cells && _srgb != null ? _srgb[cell] : Color.white;

        /// <param name="parent">Where the cells are drawn: the view becomes its child (and takes its layer).</param>
        /// <param name="toLocal">Viewport (x to the right, y up, 0..1 across the picture) to a point in the space of <paramref name="parent"/>.</param>
        /// <param name="labels">The six names of the cells, cell number = row * 3 + column, row 0 on top (<c>cells.labels</c>).</param>
        /// <param name="colorHex">The six colours <c>#RRGGBB</c> of the cells (<c>cells.colors</c>).</param>
        /// <param name="fontNames">Operating-system fonts to try in order (<c>record.fontNames</c>).</param>
        /// <param name="overlayFraction">The starting value of <see cref="OverlayFraction"/>.</param>
        /// <param name="settings">The <c>trail</c> block (<c>labelSec</c>).</param>
        /// <param name="sortingOrder">The sorting order of the frames among the other flat pictures of the overlay; the names are over them.</param>
        public void Configure(Transform parent, Func<float, float, Vector3> toLocal, string[] labels, string[] colorHex, string[] fontNames,
            float overlayFraction, TrailSettings settings, int sortingOrder)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (labels == null || labels.Length != Cells) throw new ArgumentException($"the cells need {Cells} names", nameof(labels));
            if (colorHex == null || colorHex.Length != Cells) throw new ArgumentException($"the cells need {Cells} colours", nameof(colorHex));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Release();
            _toLocal = toLocal ?? throw new ArgumentNullException(nameof(toLocal));
            OverlayFraction = overlayFraction;
            _timer = new CellLabelTimer(settings.LabelSec);
            transform.SetParent(parent, false);
            gameObject.layer = parent.gameObject.layer;

            _srgb = new Color[Cells];
            _linear = new Color32[Cells];
            for (var cell = 0; cell < Cells; cell++)
            {
                _srgb[cell] = MeadowColors.Srgb(colorHex[cell]);
                var linear = _srgb[cell].linear;
                _linear[cell] = new Color32(ToByte(linear.r), ToByte(linear.g), ToByte(linear.b), 255);
            }

            BuildFrames(sortingOrder);
            BuildTexts(labels, fontNames, sortingOrder);
            SetVisible(false);
        }

        /// <summary>Once per frame with the cell (0..5) each hand is in, any other number for a hand that is not there or does not play (a fist).</summary>
        public void Tick(double nowSec, int cellOfHand0, int cellOfHand1)
        {
            if (!IsConfigured) return;
            _now = nowSec;
            _timer.Update(nowSec, cellOfHand0, cellOfHand1);
            var anyGlow = false;
            _visibleLabels = 0;
            for (var cell = 0; cell < Cells; cell++)
            {
                anyGlow |= _timer.Glow(cell) > SmallestAlpha;
                if (_timer.LabelAlpha(cell, nowSec) > SmallestAlpha) _visibleLabels++;
            }
            SetVisible(anyGlow || _visibleLabels > 0);
            if (!anyGlow && _visibleLabels == 0) return;
            Layout(nowSec);
        }

        void BuildFrames(int sortingOrder)
        {
            _frameTexture = CellFrameTexture.Create();
            _frameMaterial = OverlayAssets.QuadMaterial("CellFrames", _frameTexture);
            _vertices = new Vector3[Cells * GridSide * GridSide];
            _colors = new Color32[_vertices.Length];
            _uvs = new Vector2[_vertices.Length];
            float[] edge = { 0f, 0.5f, 0.5f, 1f };
            var triangles = new int[Cells * PiecesPerCell * 6];
            var t = 0;
            for (var cell = 0; cell < Cells; cell++)
            {
                var first = cell * GridSide * GridSide;
                for (var row = 0; row < GridSide; row++)
                {
                    for (var column = 0; column < GridSide; column++) _uvs[first + row * GridSide + column] = new Vector2(edge[column], edge[row]);
                }
                for (var row = 0; row < GridSide - 1; row++)
                {
                    for (var column = 0; column < GridSide - 1; column++)
                    {
                        var a = first + row * GridSide + column;
                        triangles[t++] = a; triangles[t++] = a + GridSide + 1; triangles[t++] = a + 1;
                        triangles[t++] = a; triangles[t++] = a + GridSide; triangles[t++] = a + GridSide + 1;
                    }
                }
            }
            _indices = triangles;
            _mesh = new Mesh { name = "CellFrames" };
            _mesh.MarkDynamic();
            if (_renderer == null)
            {
                gameObject.AddComponent<MeshFilter>();
                _renderer = gameObject.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off;
                _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer.sharedMaterial = _frameMaterial;
            _renderer.sortingOrder = sortingOrder;
        }

        void BuildTexts(string[] labels, string[] fontNames, int sortingOrder)
        {
            _font = OverlayFonts.Get(fontNames == null || fontNames.Length == 0 ? FallbackFonts : fontNames, GlyphResolution);
            _labels = new TextMesh[Cells];
            _shadows = new TextMesh[Cells];
            for (var cell = 0; cell < Cells; cell++)
            {
                _shadows[cell] = NewText("Shadow" + cell, labels[cell], sortingOrder + 1);
                _labels[cell] = NewText("Label" + cell, labels[cell], sortingOrder + 2);
            }
        }

        TextMesh NewText(string name, string text, int sortingOrder)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.layer = gameObject.layer;
            var mesh = host.AddComponent<TextMesh>();
            mesh.font = _font;
            mesh.fontSize = GlyphResolution;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.text = text;
            var renderer = host.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            host.SetActive(false);
            return mesh;
        }

        void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
            if (visible || _labels == null) return;
            for (var cell = 0; cell < Cells; cell++)
            {
                _labels[cell].gameObject.SetActive(false);
                _shadows[cell].gameObject.SetActive(false);
            }
        }

        void Layout(double nowSec)
        {
            var across = _toLocal(1f, 0.5f) - _toLocal(0f, 0.5f);
            var upward = _toLocal(0.5f, 1f) - _toLocal(0.5f, 0f);
            var width = across.magnitude;
            var height = upward.magnitude;
            if (width < 1e-9f || height < 1e-9f) return;
            var scale = new PictureScale(width, height, (across.normalized * ShadowOffset - upward.normalized * ShadowOffset) * height);
            for (var cell = 0; cell < Cells; cell++)
            {
                var rect = CellGuideLayout.Cell(cell, OverlayFraction, HorizonViewport);
                PlaceFrame(cell, rect, scale);
                PlaceLabel(cell, rect, scale, nowSec);
            }
            _mesh.Clear(false);
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = new Bounds(_toLocal(0.5f, 0.5f), new Vector3(1e4f, 1e4f, 1e4f));
        }

        /// <summary>How big the picture is in the space of the parent: its width and height (one viewport unit across and up), and how far a name's shadow is moved.</summary>
        readonly struct PictureScale
        {
            public PictureScale(float width, float height, Vector3 shadowShift)
            {
                Width = width;
                Height = height;
                ShadowShift = shadowShift;
            }

            public float Width { get; }
            public float Height { get; }
            public Vector3 ShadowShift { get; }
        }

        // the 4 x 4 points of the nine pieces of the frame of a cell, and its colour (the glow of the cell is its opacity)
        void PlaceFrame(int cell, CellRect rect, in PictureScale scale)
        {
            var thicknessX = FrameThickness * scale.Height / scale.Width;
            var gapX = CellGap * scale.Height / scale.Width;
            var x0 = rect.XMin + gapX;
            var x1 = rect.XMax - gapX;
            var y0 = rect.YMin + CellGap;
            var y1 = rect.YMax - CellGap;
            _xs[0] = x0; _xs[1] = x0 + thicknessX; _xs[2] = x1 - thicknessX; _xs[3] = x1;
            _ys[0] = y0; _ys[1] = y0 + FrameThickness; _ys[2] = y1 - FrameThickness; _ys[3] = y1;
            var color = _linear[cell];
            color.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(_timer.Glow(cell)) * FrameAlpha * 255f);
            var first = cell * GridSide * GridSide;
            for (var row = 0; row < GridSide; row++)
            {
                for (var column = 0; column < GridSide; column++)
                {
                    _vertices[first + row * GridSide + column] = _toLocal(_xs[column], _ys[row]);
                    _colors[first + row * GridSide + column] = color;
                }
            }
        }

        // the name of a cell near its bottom edge, with a dark copy a little to the lower right under it
        void PlaceLabel(int cell, CellRect rect, in PictureScale scale, double nowSec)
        {
            var alpha = _timer.LabelAlpha(cell, nowSec);
            var showing = alpha > SmallestAlpha;
            _labels[cell].gameObject.SetActive(showing);
            _shadows[cell].gameObject.SetActive(showing);
            if (!showing) return;
            var y0 = rect.YMin + CellGap;
            var at = _toLocal(rect.CenterX, y0 + LabelFromBottom * (rect.YMax - CellGap - y0));
            _labels[cell].transform.localPosition = at;
            _shadows[cell].transform.localPosition = at + scale.ShadowShift;
            var characterSize = LabelHeight * scale.Height / (GlyphResolution * TextMeshUnitsPerPoint);
            if (!Mathf.Approximately(_labels[cell].characterSize, characterSize))
            {
                _labels[cell].characterSize = characterSize;
                _shadows[cell].characterSize = characterSize;
            }
            var ink = Color.Lerp(_srgb[cell], Color.white, LabelWhiteMix).linear;
            _labels[cell].color = new Color(ink.r, ink.g, ink.b, alpha);
            _shadows[cell].color = new Color(0f, 0f, 0f, alpha * ShadowAlpha);
        }

        static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        void OnDestroy() => Release();

        // The components stay (a view can be configured again); what was made for them goes.
        void Release()
        {
            if (_renderer != null) _renderer.enabled = false;
            if (_labels != null)
            {
                for (var cell = 0; cell < Cells; cell++)
                {
                    if (_labels[cell] != null) OverlayAssets.Release(_labels[cell].gameObject);
                    if (_shadows[cell] != null) OverlayAssets.Release(_shadows[cell].gameObject);
                }
            }
            OverlayAssets.Release(_mesh);
            OverlayAssets.Release(_frameMaterial);
            OverlayAssets.Release(_frameTexture);
            _labels = null;
            _shadows = null;
            _mesh = null;
            _frameMaterial = null;
            _frameTexture = null;
            _font = null;
            _toLocal = null;
            _timer = null;
        }
    }
}
