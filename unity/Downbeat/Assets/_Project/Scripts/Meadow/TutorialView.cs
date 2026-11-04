using System;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The follow-along tutorial on the screen: the meadow is dimmed, and one of three little scenes plays over it with a line of Korean under it. 1: an open hand breathes in the middle ("손을 펴서 들어 보세요"). 2: the six cells with the
    /// names of their instruments, and a hand that walks from cell to cell, each lighting up as it is reached. 3: two fists spread apart and the word 끝. Under the line three dots show the step; a step the visitor has done gets a tick.
    /// Everything is drawn in code (the hands are painted by <see cref="TutorialGlyphs"/>, the shapes by a shader) and only the Hangul needs a font. The look follows the mock-up the owner approved.
    /// The view hangs under a parent (the overlay layer in front of the meadow camera), is told how to find a point of the picture there, and keeps everything above the strip of the record screen (the bottom
    /// <c>overlayFraction</c> of the picture). It is a drawing: <c>TutorialDirector</c> decides what is on, <see cref="Show"/> is called with its frame every picture.
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        const string ShaderPath = "Tutorial/TutorialQuad";
        const int OrderDim = 0, OrderCells = 1, OrderRing = 2, OrderBars = 3, OrderGlyphs = 4, OrderDots = 5, OrderBadges = 6, OrderText = 7;
        const float MaxOverlayFraction = 0.5f;
        const float GridRowSplit = 0.5f;   // the grid's two rows are half the grid each; the rows of the cells follow the horizon
        const float RoamingHandCellShare = 0.5f;   // the walking hand is at most this share of the height of the cell it is in (the ground row is a low one) ...
        const float RoamingHandDrop = 0.12f;       // ... and stands this share of that height under the middle of the cell, clear of the name at the top

        // The layout, in heights of the stage (the picture above the record strip); v runs from the top (0) to the bottom (1) of the stage.
        const float HandV = 0.40f, OpenHandHeight = 0.40f, RoamingHandHeight = 0.26f, FistHeight = 0.30f;
        const float RingMinDiameter = 0.30f, RingMaxDiameter = 0.78f, RingWidth = 0.007f, RingV = 0.44f, RingStartAlpha = 0.9f;
        const float CellInset = 0.012f, CellRadius = 0.03f, CellBorder = 0.0035f, CellLabelHeight = 0.05f, CellLabelV = 0.06f;
        const float UnlitBorderAlpha = 0.33f, LitBorderAlpha = 0.95f, LitFillAlpha = 0.28f, LitMixWithWhite = 0.3f;
        const float CaptionV = 0.88f, CaptionHeight = 0.07f, CaptionWidthShare = 0.9f;
        const float DotsV = 0.953f, DotSpacing = 0.055f, DotSize = 0.022f, CurrentDotSize = 0.03f, OtherDotAlpha = 0.55f, TickSize = 0.045f;
        const float BadgeSize = 0.10f, BadgeV = 0.09f, BadgeInset = 0.075f, PopSec = 0.35f;
        const float ArrowV = 0.64f, ArrowHeight = 0.07f, ArrowOffset = 0.22f;
        const float BarWidth = 0.012f, BarSpacing = 0.026f, BarMinHeight = 0.006f, BarMaxHeight = 0.10f, BarsBaseV = 0.19f, DeadBarAlpha = 0.15f;
        const float EndWordV = 0.74f, EndWordHeight = 0.13f;
        // A picture of a hand fills its square less than the hand drawn in code fills its box (the open hand: three quarters of the height of the square, the fist: two thirds), so its square is drawn larger for the hand to look as big.
        const float OpenPictureScale = 1.13f, FistPictureScale = 1.37f;

        static readonly Color DimColour = new Color(10f / 255f, 8f / 255f, 30f / 255f, 1f);   // the blue-black of the mock-up's veil
        static readonly Color Gold = new Color(1f, 0.886f, 0.659f, 1f);                       // #FFE2A8: rings, arrows, bars, dots
        static readonly Color Cream = new Color(1f, 0.957f, 0.89f, 1f);                       // #FFF4E3: the lines of text

        struct Stage
        {
            public Func<float, float, Vector3> Point;
            public float OverlayFraction;
            public float HorizonViewport;
            public float Width;
            public float Height;

            /// <summary>The point of the stage at u (0 left .. 1 right) and v (0 top .. 1 bottom).</summary>
            public Vector3 At(float u, float v) => Point(u, OverlayFraction + (1f - v) * (1f - OverlayFraction));

            /// <summary>
            /// The point of the six cells at u (0 left .. 1 right) and v (0 top .. 1 bottom of the grid): the two rows meet at the horizon (<see cref="HandScreenMap"/>, with the rows split half and half), so a hand that walks
            /// from cell to cell is in the middle of each one and the grid is the one the visitor's hand is drawn over.
            /// </summary>
            public Vector3 AtCell(float u, float v)
            {
                var (vx, vy) = HandScreenMap.Viewport(u, v, OverlayFraction, GridRowSplit, HorizonViewport);
                return Point(vx, vy);
            }
        }

        TutorialSettings _settings;
        string[] _labels;
        Color[] _colours;
        float _overlayFraction;
        float _horizonViewport = float.NaN;
        Stage _stage;
        float _dimAlpha;
        readonly double[] _tickedSince = { double.NaN, double.NaN, double.NaN };
        Camera _camera;
        TutorialFrame _lastFrame;
        double _lastClock;

        Mesh _mesh;
        Texture2D _handTexture, _fistTexture, _badgeTexture, _chevronTexture;
        bool _openHandIsPicture, _fistIsPicture;
        Material _shapeMaterial, _handMaterial, _fistMaterial, _badgeMaterial, _chevronMaterial;
        Font _font;

        TutorialQuad _dim, _ring, _openHand, _fistLeft, _fistRight, _arrowLeft, _arrowRight, _popBadge;
        readonly TutorialQuad[] _cells = new TutorialQuad[TutorialChoreography.CellCount];
        readonly TutorialQuad[] _dots = new TutorialQuad[TutorialSettings.StepCount];
        readonly TutorialQuad[] _dotTicks = new TutorialQuad[TutorialSettings.StepCount];
        readonly TutorialQuad[] _bars = new TutorialQuad[TutorialChoreography.SoundBarCount];
        readonly TutorialText[] _cellLabels = new TutorialText[TutorialChoreography.CellCount];
        TutorialText _caption, _endWord;

        /// <summary>The tutorial is on the screen.</summary>
        public bool IsShown => gameObject.activeSelf;
        /// <summary>The share of the meadow the veil hides at this moment (the dim of the content times the fade of the frame).</summary>
        public float DimAlpha => _dimAlpha;
        public string CaptionText => _caption.Text;
        public string EndWordText => _endWord.Text;
        public bool EndWordShown => _endWord.IsVisible;
        public int OpenHandCount => _openHand.IsVisible ? 1 : 0;
        public int FistCount => (_fistLeft.IsVisible ? 1 : 0) + (_fistRight.IsVisible ? 1 : 0);
        public int VisibleLabelCount => Count(_cellLabels);
        public int VisibleDotCount => Count(_dots) + Count(_dotTicks);
        /// <summary>How many of the dots have been replaced by a tick.</summary>
        public int TickedDotCount => Count(_dotTicks);
        /// <summary>1 while the large tick of the step on show is there.</summary>
        public int VisibleBadgeCount => _popBadge.IsVisible ? 1 : 0;
        public string LabelText(int cell) => _cellLabels[cell].Text;
        /// <summary>The open hand is the picture that came with the content (true) or the hand drawn in code (false).</summary>
        public bool OpenHandIsPicture => _openHandIsPicture;
        /// <summary>The fists are the picture that came with the content (true) or the fist drawn in code (false).</summary>
        public bool FistIsPicture => _fistIsPicture;

        /// <summary>The share of the picture, from the bottom, that the record strip covers; nothing of the tutorial is drawn there. 0 with two screens.</summary>
        public float OverlayFraction
        {
            get => _overlayFraction;
            set => _overlayFraction = Mathf.Clamp(value, 0f, MaxOverlayFraction);
        }

        /// <summary>The viewport y of the horizon, where the two rows of cells meet (<see cref="HandScreenMap"/>); NaN: half way between the strip and the top, which is two rows of the same height.</summary>
        public float HorizonViewport
        {
            get => _horizonViewport;
            set => _horizonViewport = value;
        }

        /// <param name="parent">What the view hangs under: the overlay layer of the meadow camera (a dummy in a test).</param>
        /// <param name="localPoint">The point of the parent's space at a point of the picture: (0, 0) the bottom left, (1, 1) the top right, y up.</param>
        /// <param name="overlayFraction">The share of the picture, from the bottom, that the record strip covers (0 when the record has a screen of its own).</param>
        /// <param name="labels">The names of the six cells, in the order of the parts (the top row first).</param>
        /// <param name="colours">The colours of the six cells, in the same order.</param>
        /// <param name="fontNames">Operating-system fonts to try in order, a Korean one first (<c>record.fontNames</c>).</param>
        /// <param name="sortingOrderBase">The sorting order of the lowest thing drawn; the others are a few above it.</param>
        /// <param name="openHandPicture">The picture of the open hand (a transparent background), or null for the hand drawn in code. The view makes a copy of its own; the picture stays the caller's.</param>
        /// <param name="fistPicture">The picture of a right fist, or null for the fist drawn in code. The fist on the left is the picture turned round.</param>
        public static TutorialView Create(Transform parent, Func<float, float, Vector3> localPoint, float overlayFraction, TutorialSettings settings, string[] labels, Color[] colours, string[] fontNames, int sortingOrderBase = 100,
            Texture2D openHandPicture = null, Texture2D fistPicture = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (localPoint == null) throw new ArgumentNullException(nameof(localPoint));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!(overlayFraction >= 0f && overlayFraction <= MaxOverlayFraction)) throw new ArgumentOutOfRangeException(nameof(overlayFraction), overlayFraction, "0..0.5");
            if (labels == null || labels.Length != TutorialChoreography.CellCount) throw new ArgumentException($"the {TutorialChoreography.CellCount} cells need a name each", nameof(labels));
            if (colours == null || colours.Length != TutorialChoreography.CellCount) throw new ArgumentException($"the {TutorialChoreography.CellCount} cells need a colour each", nameof(colours));
            if (fontNames == null || fontNames.Length == 0) throw new ArgumentException("at least one font name is needed", nameof(fontNames));
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"the shader Resources/{ShaderPath} is missing from the build");

            var host = new GameObject("Tutorial") { layer = parent.gameObject.layer };
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<TutorialView>();
            view._camera = parent.GetComponentInParent<Camera>();
            try
            {
                view.Build(shader, localPoint, overlayFraction, settings, labels, colours, fontNames, sortingOrderBase, openHandPicture, fistPicture);
            }
            catch
            {
                DestroyImmediate(host);
                throw;
            }
            host.SetActive(false);
            return view;
        }

        void Build(Shader shader, Func<float, float, Vector3> localPoint, float overlayFraction, TutorialSettings settings, string[] labels, Color[] colours, string[] fontNames, int order, Texture2D openHandPicture, Texture2D fistPicture)
        {
            _overlayFraction = overlayFraction;
            _settings = settings;
            _labels = (string[])labels.Clone();
            _colours = (Color[])colours.Clone();
            _stage = new Stage { Point = localPoint, OverlayFraction = overlayFraction };
            _mesh = UnitQuad();
            _handTexture = TutorialPictures.Copy(openHandPicture, "TutorialOpenHandPicture");
            _openHandIsPicture = _handTexture != null;
            if (!_openHandIsPicture) _handTexture = TutorialGlyphs.CreateOpenHand();
            _fistTexture = TutorialPictures.Copy(fistPicture, "TutorialFistPicture");
            _fistIsPicture = _fistTexture != null;
            if (!_fistIsPicture) _fistTexture = TutorialGlyphs.CreateFist();
            _badgeTexture = TutorialGlyphs.CreateCheckBadge();
            _chevronTexture = TutorialGlyphs.CreateChevron();
            _shapeMaterial = NewMaterial("TutorialShape", shader, null);
            _handMaterial = NewMaterial("TutorialHand", shader, _handTexture);
            _fistMaterial = NewMaterial("TutorialFistMaterial", shader, _fistTexture);
            _badgeMaterial = NewMaterial("TutorialBadgeMaterial", shader, _badgeTexture);
            _chevronMaterial = NewMaterial("TutorialChevronMaterial", shader, _chevronTexture);
            _font = Font.CreateDynamicFontFromOSFont(fontNames, TutorialText.GlyphResolution);

            var t = transform;
            _dim = new TutorialQuad("Dim", t, _mesh, _shapeMaterial, order + OrderDim);
            for (var i = 0; i < _cells.Length; i++) _cells[i] = new TutorialQuad("Cell" + i, t, _mesh, _shapeMaterial, order + OrderCells);
            _ring = new TutorialQuad("Ring", t, _mesh, _shapeMaterial, order + OrderRing);
            for (var i = 0; i < _bars.Length; i++) _bars[i] = new TutorialQuad("Bar" + i, t, _mesh, _shapeMaterial, order + OrderBars);
            _arrowLeft = new TutorialQuad("ArrowLeft", t, _mesh, _chevronMaterial, order + OrderBars);
            _arrowRight = new TutorialQuad("ArrowRight", t, _mesh, _chevronMaterial, order + OrderBars);
            _openHand = new TutorialQuad("OpenHand", t, _mesh, _handMaterial, order + OrderGlyphs);
            _fistLeft = new TutorialQuad("FistLeft", t, _mesh, _fistMaterial, order + OrderGlyphs);
            _fistRight = new TutorialQuad("FistRight", t, _mesh, _fistMaterial, order + OrderGlyphs);
            for (var i = 0; i < _dots.Length; i++)
            {
                _dots[i] = new TutorialQuad("Dot" + i, t, _mesh, _shapeMaterial, order + OrderDots);
                _dotTicks[i] = new TutorialQuad("DotTick" + i, t, _mesh, _badgeMaterial, order + OrderDots);
            }
            _popBadge = new TutorialQuad("CheckBadge", t, _mesh, _badgeMaterial, order + OrderBadges);
            for (var i = 0; i < _cellLabels.Length; i++) _cellLabels[i] = new TutorialText("Label" + i, t, _font, order + OrderText);
            _caption = new TutorialText("Caption", t, _font, order + OrderText);
            _endWord = new TutorialText("EndWord", t, _font, order + OrderText);
        }

        /// <summary>Draws the tutorial as the frame says (once a picture), or takes it off the screen when the frame is not visible.</summary>
        /// <param name="clockSeconds">A clock that runs all the time, for the things that move by themselves: the breath of the hand, the ring, the pop of a tick.</param>
        public void Show(TutorialFrame frame, double clockSeconds)
        {
            if (!frame.IsVisible)
            {
                Hide();
                return;
            }
            _lastFrame = frame;
            _lastClock = clockSeconds;
            MeasureStage();
            var progress = frame.StepProgress;
            var scene = frame.Alpha * TutorialChoreography.SceneAlpha(progress);

            DrawDim(frame.Alpha);
            DrawCells(frame.Step, progress, frame.Alpha);
            switch (frame.Step)
            {
                case 0: DrawBreathingHand(clockSeconds, scene); break;
                case 1: DrawRoamingHand(progress, scene); break;
                default: DrawFists(progress, scene); break;
            }
            DrawCaption(frame.Step, scene);
            DrawDots(frame, clockSeconds);
            DrawTick(frame, clockSeconds);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            _dimAlpha = 0f;
            _lastFrame = default;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        /// <summary>
        /// The picture the camera is about to draw may have another shape than the one the tutorial was last laid out for (a capture or a recording of another size than the window, a window that was resized
        /// since): then the same frame is laid out again for it, so that the cells meet the picture's edges and the hand glyphs keep their shape.
        /// </summary>
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || !_lastFrame.IsVisible) return;
            var width = _stage.Width;
            var height = _stage.Height;
            MeasureStage();
            if (Mathf.Approximately(width, _stage.Width) && Mathf.Approximately(height, _stage.Height)) return;
            Show(_lastFrame, _lastClock);
        }

        void MeasureStage()
        {
            _stage.OverlayFraction = _overlayFraction;
            _stage.HorizonViewport = _horizonViewport;
            _stage.Width = Mathf.Abs(_stage.At(1f, 0.5f).x - _stage.At(0f, 0.5f).x);
            _stage.Height = Mathf.Abs(_stage.At(0.5f, 0f).y - _stage.At(0.5f, 1f).y);
        }

        void DrawDim(float alpha)
        {
            _dimAlpha = _settings.Dim * alpha;
            _dim.DrawBox(_stage.At(0.5f, 0.5f), new Vector2(_stage.Width, _stage.Height), 0f, DimColour, _dimAlpha, 1f);
        }

        void DrawCells(int step, float progress, float alpha)
        {
            var strength = TutorialChoreography.GridStrength(step, progress) * alpha;
            var nameStrength = TutorialChoreography.LabelStrength(step, progress) * alpha;
            var inset = CellInset * _stage.Height;
            var pictureHeight = _stage.Height / (1f - _stage.OverlayFraction);
            for (var cell = 0; cell < _cells.Length; cell++)
            {
                var rect = CellGuideLayout.Cell(cell, _stage.OverlayFraction, _stage.HorizonViewport);   // the sky row above the horizon, the ground row below it
                var size = new Vector2(_stage.Width / TutorialChoreography.Columns - 2f * inset, (rect.YMax - rect.YMin) * pictureHeight - 2f * inset);
                var light = step == 1 ? TutorialChoreography.CellLight(cell, progress) : 0f;
                var tint = Color.Lerp(Color.white, Color.Lerp(_colours[cell], Color.white, LitMixWithWhite), light);
                _cells[cell].DrawBox(_stage.Point(rect.CenterX, rect.CenterY), size, CellRadius * _stage.Height, tint, strength, LitFillAlpha * light, CellBorder * _stage.Height, Mathf.Lerp(UnlitBorderAlpha, LitBorderAlpha, light));
                var nameTint = Color.Lerp(Color.white, _colours[cell], 0.35f + 0.65f * light);
                _cellLabels[cell].Show(_labels[cell], _stage.Point(rect.CenterX, rect.YMax - CellLabelV * (1f - _stage.OverlayFraction)), CellLabelHeight * _stage.Height, nameTint, nameStrength);
            }
        }

        void DrawBreathingHand(double clockSeconds, float scene)
        {
            var scale = TutorialChoreography.Breath(clockSeconds);
            DrawHand(_openHand, 0.5f, HandV, OpenHandHeight * scale, scene, false, _openHandIsPicture ? OpenPictureScale : 0f);
            var phase = TutorialChoreography.RingPhase(clockSeconds);
            var diameter = Mathf.Lerp(RingMinDiameter, RingMaxDiameter, phase) * _stage.Height;
            _ring.DrawRing(_stage.At(0.5f, RingV), diameter, RingWidth * _stage.Height, Gold, RingStartAlpha * (1f - phase) * scene);
            HideFistScene();
        }

        void DrawRoamingHand(float progress, float scene)
        {
            var (u, v) = TutorialChoreography.RoamPosition(progress);
            var cellHeight = CellHeightAt(v);
            var height = Mathf.Min(RoamingHandHeight, RoamingHandCellShare * cellHeight);
            DrawHand(_openHand, u, v, height, scene, false, _openHandIsPicture ? OpenPictureScale : 0f, inGrid: true, drop: RoamingHandDrop * cellHeight);
            _ring.Hide();
            HideFistScene();
        }

        // The height of the cells at v (0 top .. 1 bottom of the grid), in heights of the stage: that of the top row, that of the bottom row, and a smooth change between them while the hand goes from one row to the other.
        float CellHeightAt(float v)
        {
            var top = CellGuideLayout.Cell(0, _stage.OverlayFraction, _stage.HorizonViewport);
            var bottom = CellGuideLayout.Cell(TutorialChoreography.Columns, _stage.OverlayFraction, _stage.HorizonViewport);
            var between = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, v));
            return Mathf.Lerp(top.YMax - top.YMin, bottom.YMax - bottom.YMin, between) / (1f - _stage.OverlayFraction);
        }

        void DrawFists(float progress, float scene)
        {
            var (left, right) = TutorialChoreography.FistCentres(TutorialChoreography.FistSpread(progress));
            // the drawn fist has its thumb to the left (the one on the right is turned round), the picture is a right fist (the one on the left is turned round)
            var pictureScale = _fistIsPicture ? FistPictureScale : 0f;
            DrawHand(_fistLeft, left, HandV, FistHeight, scene, _fistIsPicture, pictureScale);
            DrawHand(_fistRight, right, HandV, FistHeight, scene, !_fistIsPicture, pictureScale);
            DrawArrows(progress, scene);
            DrawSoundBars(progress, scene);
            _endWord.Show(_settings.EndWord, _stage.At(0.5f, EndWordV), EndWordHeight * _stage.Height, Color.white, scene * TutorialChoreography.EndWordAlpha(progress));
            _openHand.Hide();
            _ring.Hide();
        }

        /// <param name="pictureScale">0 for the hand drawn in code (its box is 100 by 120), else the side of the square of a picture as a multiple of <paramref name="height"/>.</param>
        /// <param name="inGrid">(u, v) is a place of the six cells (the rows meet at the horizon), not of the plain stage.</param>
        /// <param name="drop">How far under (u, v) the middle of the hand is, in heights of the stage.</param>
        void DrawHand(TutorialQuad quad, float u, float v, float height, float alpha, bool mirror, float pictureScale, bool inGrid = false, float drop = 0f)
        {
            var h = height * _stage.Height;
            var size = pictureScale > 0f ? new Vector2(h * pictureScale, h * pictureScale) : new Vector2(h * TutorialGlyphs.HandUnitsWide / TutorialGlyphs.HandUnitsHigh, h);
            var centre = inGrid ? _stage.AtCell(u, v) : _stage.At(u, v);
            quad.DrawTexture(centre + new Vector3(0f, -drop * _stage.Height, 0f), size, Color.white, alpha, mirror);
        }

        void DrawArrows(float progress, float scene)
        {
            var alpha = TutorialChoreography.ArrowAlpha(progress) * scene;
            var height = ArrowHeight * _stage.Height;
            var size = new Vector2(height * TutorialGlyphs.ChevronUnitsWide / TutorialGlyphs.ChevronUnitsHigh, height);
            var reach = ArrowOffset * _stage.Height / _stage.Width;
            _arrowLeft.DrawTexture(_stage.At(0.5f - reach, ArrowV), size, Gold, alpha);
            _arrowRight.DrawTexture(_stage.At(0.5f + reach, ArrowV), size, Gold, alpha, true);
        }

        void DrawSoundBars(float progress, float scene)
        {
            var width = BarWidth * _stage.Height;
            var spacing = BarSpacing * _stage.Height / _stage.Width;
            for (var i = 0; i < _bars.Length; i++)
            {
                var level = TutorialChoreography.SoundLevel(progress, i);
                var height = Mathf.Lerp(BarMinHeight, BarMaxHeight, level) * _stage.Height;
                var centreV = BarsBaseV - height * 0.5f / _stage.Height;   // the bars stand on a line
                _bars[i].DrawBox(_stage.At(0.5f + (i - (_bars.Length - 1) * 0.5f) * spacing, centreV), new Vector2(width, height), width * 0.5f, Gold, Mathf.Lerp(DeadBarAlpha, 1f, level) * scene, 1f);
            }
        }

        void HideFistScene()
        {
            _fistLeft.Hide();
            _fistRight.Hide();
            _arrowLeft.Hide();
            _arrowRight.Hide();
            for (var i = 0; i < _bars.Length; i++) _bars[i].Hide();
            _endWord.Hide();
        }

        void DrawCaption(int step, float scene)
        {
            var text = _settings.Texts[step];
            var height = Mathf.Min(CaptionHeight * _stage.Height, CaptionWidthShare * _stage.Width / Mathf.Max(1, text.Length));
            _caption.Show(text, _stage.At(0.5f, CaptionV), height, Cream, scene);
        }

        void DrawDots(TutorialFrame frame, double clockSeconds)
        {
            var spacing = DotSpacing * _stage.Height / _stage.Width;
            for (var i = 0; i < _dots.Length; i++)
            {
                var centre = _stage.At(0.5f + (i - 1) * spacing, DotsV);
                UpdateTickedSince(i, frame.IsChecked(i), clockSeconds);
                if (frame.IsChecked(i))
                {
                    var size = TickSize * _stage.Height * Pop(_tickedSince[i], clockSeconds);
                    _dotTicks[i].DrawTexture(centre, new Vector2(size, size), Color.white, frame.Alpha);
                    _dots[i].Hide();
                    continue;
                }
                var current = i == frame.Step;
                _dots[i].DrawDisc(centre, (current ? CurrentDotSize : DotSize) * _stage.Height, Gold, (current ? 1f : OtherDotAlpha) * frame.Alpha);
                _dotTicks[i].Hide();
            }
        }

        /// <summary>The large tick that pops up when the step on show has been done.</summary>
        void DrawTick(TutorialFrame frame, double clockSeconds)
        {
            if (!frame.IsChecked(frame.Step))
            {
                _popBadge.Hide();
                return;
            }
            var size = BadgeSize * _stage.Height * Pop(_tickedSince[frame.Step], clockSeconds);
            _popBadge.DrawTexture(_stage.At(1f - BadgeInset * _stage.Height / _stage.Width, BadgeV), new Vector2(size, size), Color.white, frame.Alpha);
        }

        void UpdateTickedSince(int step, bool isChecked, double clockSeconds)
        {
            if (!isChecked) _tickedSince[step] = double.NaN;
            else if (double.IsNaN(_tickedSince[step])) _tickedSince[step] = clockSeconds;
        }

        /// <summary>The size of a tick as it pops up: 0, past full size, then settling at 1 (ease out with a little overshoot).</summary>
        static float Pop(double since, double clockSeconds)
        {
            const float Overshoot = 1.70158f;
            var t = Mathf.Clamp01((float)((clockSeconds - since) / PopSec)) - 1f;
            return 1f + (Overshoot + 1f) * t * t * t + Overshoot * t * t;
        }

        static int Count(TutorialQuad[] quads)
        {
            var n = 0;
            for (var i = 0; i < quads.Length; i++)
            {
                if (quads[i].IsVisible) n++;
            }
            return n;
        }

        static int Count(TutorialText[] texts)
        {
            var n = 0;
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i].IsVisible) n++;
            }
            return n;
        }

        static Mesh UnitQuad()
        {
            var mesh = new Mesh { name = "TutorialQuad" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.1f));
            return mesh;
        }

        static Material NewMaterial(string name, Shader shader, Texture2D texture)
        {
            var material = new Material(shader) { name = name };
            if (texture != null) material.mainTexture = texture;
            return material;
        }

        /// <summary>Everything the view made goes with it: the textures, the mesh, the materials and the font. The shader is a shared asset and stays.</summary>
        void OnDestroy()
        {
            foreach (var texture in new[] { _handTexture, _fistTexture, _badgeTexture, _chevronTexture }) Release(texture);
            foreach (var material in new[] { _shapeMaterial, _handMaterial, _fistMaterial, _badgeMaterial, _chevronMaterial }) Release(material);
            Release(_mesh);
            ReleaseFont();
        }

        void ReleaseFont()
        {
            if (_font == null) return;
            var material = _font.material;
            var texture = material != null ? material.mainTexture : null;
            Release(_font);
            Release(texture);
            Release(material);
            _font = null;
        }

        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
