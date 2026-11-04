using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Record
{
    /// <summary>How the record screen is shown: as a strip drawn over the lower part of the meadow, on a screen of its own, or only into a picture (captures and measurements).</summary>
    public enum RecordMount { Strip, Display, Offscreen }

    /// <summary>
    /// The record screen: the live flower score, the lines of the nature layers and today's cards, drawn as meshes and text meshes by <see cref="RecordPainter"/>. The app carries no UI package
    /// and no font file: the text is a TextMesh in an operating-system font that has Korean (the first of <c>record.fontNames</c> that exists), the quads are made in code.
    /// On its own screen it has a camera of its own (placed far from the meadow); as a strip it hangs in front of the meadow camera, so it follows that camera's size.
    /// </summary>
    public sealed class RecordScreenView : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;     // layouts are made for a screen this tall and scale with it
        const float FarX = 100000f;              // where the screen's own camera looks: far from the meadow, which the meadow camera's far plane never reaches
        const float StripDistance = 1f;
        const float DefaultAspect = 16f / 9f;
        const int GlyphResolution = 96;
        const float TextMeshUnitsPerPoint = 0.1f;
        const int MaxThumbnails = 12;
        const int OrderBackground = 0, OrderMotes = 1, OrderLines = 2, OrderRound = 3, OrderThumbnail = 4, OrderOverlay = 5, OrderGlow = 6, OrderFlowers = 7, OrderText = 10;
        const string ShaderPath = "Record/RecordQuad";

        readonly RecordBuilders _builders = new RecordBuilders();
        readonly List<TextCommand> _textCommands = new List<TextCommand>(32);
        readonly List<ThumbnailCommand> _thumbnailCommands = new List<ThumbnailCommand>(MaxThumbnails);
        readonly Dictionary<string, TextMesh> _texts = new Dictionary<string, TextMesh>();
        readonly HashSet<string> _usedTexts = new HashSet<string>();
        readonly List<RecordCardView> _cards = new List<RecordCardView>();
        readonly List<Material> _materials = new List<Material>();   // one per layer: made here, destroyed here (the shader is shared and stays)

        RecordDto _record;
        RecordPainter _painter;
        ScoreModel _score;
        Camera _meadowCamera;
        Camera _recordCamera;
        Transform _root;
        Font _font;
        Texture2D _atlas, _rounded, _disc, _white, _moteAtlas;
        Mesh _solidMesh, _linesMesh, _roundMesh, _overlayMesh, _glowMesh, _flowerMesh, _moteMesh, _unitQuad;
        MeshRenderer _moteRenderer;
        MeshRenderer[] _thumbnailRenderers;
        MaterialPropertyBlock _block;
        RenderTexture _offscreen;
        RecordLayout _layout;
        float _overlayFraction;
        float _panelWidth, _panelHeight;
        bool _configured;
        bool _continuousOffscreen;

        /// <summary>The object that holds the layers, the texts and the camera of the screen (not a child of this view).</summary>
        public Transform ScreenRoot => _root;
        public RecordMount Mount { get; private set; }
        public DisplayMode Mode => Mount == RecordMount.Strip ? DisplayMode.SingleOverlay : DisplayMode.Dual;
        public RecordLayout Layout => _layout;
        public int QuadCount => _builders.Solid.QuadCount + _builders.Lines.QuadCount + _builders.Round.QuadCount + _builders.Overlay.QuadCount + _builders.Glow.QuadCount + _builders.Flowers.QuadCount;
        public int FlowerQuadCount => _builders.Flowers.QuadCount;
        public int TextCount => _usedTexts.Count;

        /// <summary>A visit is on (the hands are in): the idle motes behind the panels fade down to <c>record.idleMoteDimmed</c>, and come back when this is false again. Set once per frame by the coordinator.</summary>
        public bool Performing { get; set; }
        /// <summary>1 when the idle motes are at their full brightness, less while a visit is on.</summary>
        public float MoteLevel => _painter.MoteLevel;
        /// <summary>The idle motes as they were drawn last (screen heights, see <see cref="MoteState"/>): for tests.</summary>
        public int MoteCount => _painter.MoteCount;
        public MoteState MoteAt(int index) => _painter.MoteAt(index);

        /// <summary>Shown or hidden (the Tab key of the strip).</summary>
        public bool Visible
        {
            get => _configured && _root.gameObject.activeSelf;
            set
            {
                if (!_configured) return;
                _root.gameObject.SetActive(value);
                if (_recordCamera != null) _recordCamera.enabled = value && (Mount == RecordMount.Display || _continuousOffscreen);
            }
        }

        /// <param name="display">The screen of the record camera (Mount.Display); ignored otherwise.</param>
        /// <param name="continuousOffscreen">Offscreen mount only: draw into the picture every frame (to measure what a second screen costs) instead of only when asked.</param>
        public void Configure(ContentDto content, ScoreModel score, Camera meadowCamera, RecordMount mount, int display, bool continuousOffscreen = false)
        {
            _record = content.record;
            _score = score;
            _meadowCamera = meadowCamera;
            Mount = mount;
            _overlayFraction = content.display.overlayFraction;
            _continuousOffscreen = continuousOffscreen;
            _painter = new RecordPainter(_record, BuildPalette(content), content.song.pitchSteps);

            _atlas = FlowerIconPainter.Create(
                new[] { Hex(content.meadow.flowers.bellflower.openColor), Hex(content.meadow.flowers.dandelion.openColor), Hex(content.meadow.flowers.poppy.openColor) },
                new[] { Hex(content.meadow.flowers.bellflower.centerColor), Hex(content.meadow.flowers.dandelion.centerColor), Hex(content.meadow.flowers.poppy.centerColor) });
            _rounded = RoundedRectTexture.Create();
            _moteAtlas = MoteAtlas.Create();
            _disc = MakeDisc();
            _white = MakeWhite();
            _font = Font.CreateDynamicFontFromOSFont(content.record.fontNames, GlyphResolution);
            _block = new MaterialPropertyBlock();

            BuildScene(display);
            if (Mount == RecordMount.Strip) SetPanelSize(ReferenceHeight * DefaultAspect, ReferenceHeight * _overlayFraction); // until the meadow camera draws its first picture and tells its real shape
            _configured = true;
            Tick(0.0);
        }

        /// <summary>Today's newest cards, newest first. The textures belong to the caller.</summary>
        public void SetCards(IReadOnlyList<RecordCardView> newestFirst)
        {
            _cards.Clear();
            if (newestFirst != null) _cards.AddRange(newestFirst);
        }

        /// <summary>Once per frame with the audio time that is being heard.</summary>
        public void Tick(double heardNow) => Tick(heardNow, Time.realtimeSinceStartupAsDouble);

        /// <param name="clockSeconds">The wall-clock seconds of the slow things on the screen (the breathing hint, the idle motes); a test or a capture can give its own.</param>
        public void Tick(double heardNow, double clockSeconds)
        {
            if (!_configured || !_root.gameObject.activeInHierarchy) return;
            if (Mount != RecordMount.Strip) UpdateOwnCameraSize();
            Redraw(heardNow, clockSeconds);
        }

        /// <summary>The record screen as it looks on a screen of its own, drawn into a picture of this size (a capture). Works whatever the mount, but not for a strip (it has no screen of its own).</summary>
        public Texture2D RenderPicture(int width, int height, double heardNow) => RenderPicture(width, height, heardNow, Time.realtimeSinceStartupAsDouble);

        /// <param name="clockSeconds">The wall-clock seconds of the slow things on the screen (the breathing hint, the idle motes): the same clock gives the same picture.</param>
        public Texture2D RenderPicture(int width, int height, double heardNow, double clockSeconds)
        {
            if (Mount == RecordMount.Strip) throw new InvalidOperationException("a strip has no screen of its own; mount the record as Offscreen or Display");
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousTarget = _recordCamera.targetTexture;
            var previousActive = RenderTexture.active;
            var wasEnabled = _recordCamera.enabled;
            try
            {
                _recordCamera.targetTexture = target;
                _recordCamera.enabled = true;
                SetPanelSize(ReferenceHeight * width / height, ReferenceHeight);
                Redraw(heardNow, clockSeconds);
                _recordCamera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                picture.Apply();
                return picture;
            }
            finally
            {
                _recordCamera.targetTexture = previousTarget;
                _recordCamera.enabled = wasEnabled;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        /// <summary>Everything this view made goes with it: the textures, the meshes, the materials of the layers, the font, the offscreen picture and the object that holds the layers. The shader is a shared asset and stays.</summary>
        void OnDestroy()
        {
            foreach (var texture in new[] { _atlas, _rounded, _disc, _white, _moteAtlas }) Disposal.Release(texture);
            foreach (var mesh in new[] { _solidMesh, _linesMesh, _roundMesh, _overlayMesh, _glowMesh, _flowerMesh, _moteMesh, _unitQuad }) Disposal.Release(mesh);
            foreach (var material in _materials) Disposal.Release(material);
            _materials.Clear();
            Disposal.Release(_font);
            if (_root != null) Disposal.Release(_root.gameObject);   // the layers, the texts and the camera of the screen are not children of this object
            if (_offscreen != null)
            {
                _offscreen.Release();
                Disposal.Release(_offscreen);
            }
        }

        /// <summary>The strip follows the size of the picture the meadow camera is drawing (a window, a resized window, a capture).</summary>
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_configured || Mount != RecordMount.Strip || camera != _meadowCamera || camera.pixelHeight == 0) return;
            var aspect = (float)camera.pixelWidth / camera.pixelHeight;
            var halfHeight = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * StripDistance;
            _root.localScale = Vector3.one * (2f * halfHeight / ReferenceHeight);
            _root.localPosition = new Vector3(-halfHeight * aspect, -halfHeight, StripDistance);
            _root.localRotation = Quaternion.identity;
            SetPanelSize(ReferenceHeight * aspect, ReferenceHeight * _overlayFraction);
        }

        void BuildScene(int display)
        {
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"the shader Resources/{ShaderPath} is missing from the build");

            _root = new GameObject("RecordScreen").transform;
            if (Mount == RecordMount.Strip) _root.SetParent(_meadowCamera.transform, false);
            else _root.position = new Vector3(FarX, 0f, 0f);
            if (Mount != RecordMount.Strip) BuildOwnCamera(display);

            _solidMesh = NewMesh("RecordSolid");
            _linesMesh = NewMesh("RecordLines");
            _roundMesh = NewMesh("RecordRound");
            _overlayMesh = NewMesh("RecordOverlay");
            _glowMesh = NewMesh("RecordGlow");
            _flowerMesh = NewMesh("RecordFlowers");
            _moteMesh = NewMesh("RecordMotes");
            AddLayer("Solid", _solidMesh, shader, _white, OrderBackground);
            _moteRenderer = AddLayer("Motes", _moteMesh, shader, _moteAtlas, OrderMotes);
            _moteRenderer.enabled = Mount != RecordMount.Strip && _painter.MoteCount > 0;   // nothing to draw on the strip or with no motes
            AddLayer("Lines", _linesMesh, shader, _white, OrderLines);   // the staff lines and their kin: over the motes, under the translucent panels
            AddLayer("Round", _roundMesh, shader, _rounded, OrderRound);
            AddLayer("Overlay", _overlayMesh, shader, _white, OrderOverlay);
            AddLayer("Glow", _glowMesh, shader, _disc, OrderGlow);
            AddLayer("Flowers", _flowerMesh, shader, _atlas, OrderFlowers);

            _unitQuad = new Mesh { name = "RecordUnitQuad" };
            _unitQuad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            _unitQuad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            _unitQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _unitQuad.bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 1f));
            _thumbnailRenderers = new MeshRenderer[MaxThumbnails];
            for (var i = 0; i < MaxThumbnails; i++)
            {
                var layer = AddLayer("Thumbnail" + i, _unitQuad, shader, _white, OrderThumbnail);
                layer.gameObject.SetActive(false);
                _thumbnailRenderers[i] = layer;
            }
        }

        void BuildOwnCamera(int display)
        {
            var cameraObject = new GameObject("RecordCamera");
            cameraObject.transform.SetParent(_root, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            _recordCamera = cameraObject.AddComponent<Camera>();
            _recordCamera.orthographic = true;
            _recordCamera.orthographicSize = ReferenceHeight * 0.5f;
            _recordCamera.nearClipPlane = 1f;
            _recordCamera.farClipPlane = 30f;
            _recordCamera.clearFlags = CameraClearFlags.SolidColor;
            _recordCamera.backgroundColor = Hex(_record.colors.background);
            _recordCamera.depth = -5f;
            _recordCamera.allowHDR = false;
            _recordCamera.allowMSAA = false;
            var data = _recordCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            if (Mount == RecordMount.Display)
            {
                _recordCamera.targetDisplay = Mathf.Max(0, display);
            }
            else
            {
                _offscreen = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "RecordOffscreen" };
                _recordCamera.targetTexture = _offscreen;
                _recordCamera.enabled = _continuousOffscreen;
            }
        }

        MeshRenderer AddLayer(string name, Mesh mesh, Shader shader, Texture2D texture, int order)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(_root, false);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            var material = new Material(shader) { name = "Record" + name };
            _materials.Add(material);
            material.mainTexture = texture;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        static Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            return mesh;
        }

        /// <summary>On a screen of its own the picture is as tall as the reference and as wide as that screen's shape: the panel follows the camera.</summary>
        void UpdateOwnCameraSize()
        {
            var width = _recordCamera.pixelWidth;
            var height = _recordCamera.pixelHeight;
            var aspect = height > 0 ? (float)width / height : DefaultAspect;
            SetPanelSize(ReferenceHeight * aspect, ReferenceHeight);
        }

        void SetPanelSize(float width, float height)
        {
            if (Mathf.Approximately(width, _panelWidth) && Mathf.Approximately(height, _panelHeight) && _layout != null) return;
            _panelWidth = width;
            _panelHeight = height;
            _layout = RecordLayout.Compute(Mode, width, height, _record.cardsShown);
            if (_recordCamera != null) _recordCamera.transform.localPosition = new Vector3(width * 0.5f, height * 0.5f, -10f); // the camera looks at the middle of the panel
        }

        void Redraw(double heardNow, double clockSeconds)
        {
            if (_layout == null) return;
            _painter.Paint(_layout, heardNow, clockSeconds, _score, _cards, DateTime.Now, _panelWidth, _panelHeight, _builders, _textCommands, _thumbnailCommands, Performing);
            _builders.Solid.Upload(_solidMesh);
            _builders.Lines.Upload(_linesMesh);
            _builders.Round.Upload(_roundMesh);
            _builders.Overlay.Upload(_overlayMesh);
            _builders.Glow.Upload(_glowMesh);
            _builders.Flowers.Upload(_flowerMesh);
            if (_moteRenderer.enabled) _builders.Motes.Upload(_moteMesh);
            ApplyTexts();
            ApplyThumbnails();
        }

        void ApplyTexts()
        {
            _usedTexts.Clear();
            foreach (var command in _textCommands)
            {
                var mesh = TextFor(command.Slot);
                mesh.gameObject.SetActive(true);
                mesh.text = command.Text;
                mesh.color = command.Color;
                mesh.anchor = command.Anchor == TextAnchorKind.MiddleLeft ? TextAnchor.MiddleLeft : command.Anchor == TextAnchorKind.MiddleCenter ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight;
                mesh.characterSize = command.Height / (GlyphResolution * TextMeshUnitsPerPoint);
                mesh.transform.localPosition = new Vector3(command.X, command.Y, 0f);
                _usedTexts.Add(command.Slot);
            }
            foreach (var pair in _texts)
            {
                if (!_usedTexts.Contains(pair.Key)) pair.Value.gameObject.SetActive(false);
            }
        }

        TextMesh TextFor(string slot)
        {
            if (_texts.TryGetValue(slot, out var existing)) return existing;
            var textObject = new GameObject("Text_" + slot);
            textObject.transform.SetParent(_root, false);
            var mesh = textObject.AddComponent<TextMesh>();
            mesh.font = _font;
            mesh.fontSize = GlyphResolution;
            mesh.alignment = TextAlignment.Left;
            var renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
            renderer.sortingOrder = OrderText;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _texts[slot] = mesh;
            return mesh;
        }

        void ApplyThumbnails()
        {
            for (var i = 0; i < _thumbnailRenderers.Length; i++) _thumbnailRenderers[i].gameObject.SetActive(false);
            foreach (var command in _thumbnailCommands)
            {
                if (command.Slot >= _thumbnailRenderers.Length) continue;
                var renderer = _thumbnailRenderers[command.Slot];
                renderer.gameObject.SetActive(true);
                renderer.transform.localPosition = new Vector3(command.Rect.X + command.Rect.Width * 0.5f, command.Rect.Y + command.Rect.Height * 0.5f, 0f);
                renderer.transform.localScale = new Vector3(command.Rect.Width, command.Rect.Height, 1f);
                _block.SetTexture("_MainTex", command.Texture);
                renderer.SetPropertyBlock(_block);
            }
        }

        RecordPalette BuildPalette(ContentDto content)
        {
            var c = _record.colors;
            var flowers = content.meadow.flowers;
            var particles = content.meadow.particles;
            return new RecordPalette
            {
                Background = Hex(c.background).linear, BackgroundBottom = Hex(c.backgroundBottom).linear, Ink = Hex(c.ink).linear, Muted = Hex(c.muted).linear,
                Staff = Hex(c.staff).linear, Accent = Hex(c.accent).linear, Card = Hex(c.card).linear,
                FlowerGlow = PartColours(content),
                NatureLine = new[] { Hex(particles.fireflyColor).linear, Hex(content.meadow.sky.stops[2].lightColor).linear, Hex(content.meadow.sky.stops[0].lightColor).linear },
            };
        }

        /// <summary>The colour of each of the six parts: the open colour of the flower of its column for the ground row, the colour of its cell for the sky row (the colour of its light dot).</summary>
        static Color[] PartColours(ContentDto content)
        {
            var flowers = content.meadow.flowers;
            var openColours = new[] { flowers.bellflower.openColor, flowers.dandelion.openColor, flowers.poppy.openColor };
            var cells = CellSettings.From(content.cells);
            var colours = new Color[Parts.Count];
            for (var i = 0; i < colours.Length; i++)
            {
                var part = (Part)i;
                colours[i] = Hex(Parts.IsSky(part) ? cells.Colors[i] : openColours[Parts.Column(part)]).linear;
            }
            return colours;
        }

        static Color Hex(string hex)
        {
            if (!ColorHex.TryParse(hex, out var rgb)) throw new FormatException($"'{hex}' is not a colour like #RRGGBB");
            return new Color(rgb.R, rgb.G, rgb.B, 1f);
        }

        static Texture2D MakeWhite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "RecordWhite", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A round soft gradient (alpha 1 in the middle, 0 at the edge).</summary>
        static Texture2D MakeDisc()
        {
            const int Size = 64;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "RecordDisc", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f) / Size * 2f - 1f;
                    var dy = (y + 0.5f) / Size * 2f - 1f;
                    var t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(t * t * (3f - 2f * t) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
