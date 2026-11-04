using System;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>What the trail needs to know of one hand in a frame (the caller reads it off the tracker and the cell the hand is in).</summary>
    public readonly struct TrailHandInput
    {
        public TrailHandInput(bool visible, bool isFist, float x01, float y01, double captureMs, int handId, int epoch, Color color)
        {
            Visible = visible;
            IsFist = isFist;
            X01 = x01;
            Y01 = y01;
            CaptureMs = captureMs;
            HandId = handId;
            Epoch = epoch;
            Color = color;
        }

        /// <summary>The tracker has the hand in its latest picture.</summary>
        public bool Visible { get; }
        /// <summary>A fist leaves no trail.</summary>
        public bool IsFist { get; }
        /// <summary>Where the hand is: <c>HandMotion.X</c> and <c>Y</c>, 0..1 across the mirrored camera picture, y down.</summary>
        public float X01 { get; }
        public float Y01 { get; }
        public double CaptureMs { get; }
        public int HandId { get; }
        public int Epoch { get; }
        /// <summary>The colour (sRGB) of the notes born now: the colour of the cell the hand is in.</summary>
        public Color Color { get; }
    }

    /// <summary>
    /// The music notes that follow the hands: for each of the two hands an open hand scatters quavers (♪, drawn from an outline in code, no font, each with a thin dark outline and a soft dark halo so that it reads on a
    /// bright sky) behind it, the more the more it moves; they float up a little and fade out in <c>trail.lifeSec</c> seconds (full at their birth, a little smaller as they fade), each in the colour of the cell the hand was in when it was born. A fist leaves none; a hand that is gone leaves no more and the notes it left fade out.
    /// The notes are in the picture (viewport units, y up) and are placed by <c>toLocal</c> in the space of <c>parent</c>, usually the overlay layer on the meadow camera; how many there are follows how fast the hand
    /// moves on the picture (the tracker's own speed of a hand is 0 at 6 pictures a second). Where the notes are is worked out by
    /// <see cref="HandNoteTrail"/>; this class only draws them: one mesh, one draw call, nothing is drawn while there are no notes.
    /// </summary>
    public sealed class NoteTrailView : MonoBehaviour
    {
        public const int Slots = 2;
        // the quaver fills this share of the height of its square, so the square is this much taller than the note
        const float GlyphHeightShare = 0.72f;
        const float SmallestAlpha = 0.004f;

        readonly HandNoteTrail[] _trails = new HandNoteTrail[Slots];
        Func<float, float, Vector3> _toLocal;
        TrailSettings _baseSettings;
        TrailSettings _settings;
        float _countScale = 1f;
        Mesh _mesh;
        Material _material;
        Texture2D _glyph;
        MeshRenderer _renderer;
        Vector3[] _vertices;
        Color32[] _colors;
        Vector2[] _uvs;
        int[] _indices;
        int _drawn;

        public bool IsConfigured => _toLocal != null;

        /// <summary>The height of the strip at the bottom of the picture that the hand's y does not reach (<c>display.overlayFraction</c> while the record strip is shown on this screen, else 0). Read at every <see cref="Tick"/>: the strip can be hidden while running.</summary>
        public float OverlayFraction { get; set; }

        /// <summary>The hand height (0 top .. 1 bottom) where the two rows of the hand meet (<c>cells.rowSplit</c>).</summary>
        public float RowSplit { get; set; } = MeadowOverlayLayer.DefaultRowSplit;

        /// <summary>The viewport y of the horizon, where the two rows meet on the picture (<see cref="HandScreenMap"/>); NaN: half way between the strip and the top. Read at every <see cref="Tick"/>.</summary>
        public float HorizonViewport { get; set; } = float.NaN;

        /// <summary>How many notes were drawn in the last <see cref="Tick"/>.</summary>
        public int NoteCount => _drawn;

        /// <summary>Where the notes of the hand in a slot (the id of the hand) are born as of the last <see cref="Tick"/>: its place on the picture (viewport units), smoothed between tracker frames. For tests and the debug overlay.</summary>
        public ScreenPoint PlaceOf(int slot) => slot >= 0 && slot < Slots && _trails[slot] != null ? _trails[slot].Place : default;

        /// <summary>The notes alive, drawn or not.</summary>
        public int AliveCount
        {
            get
            {
                var alive = 0;
                foreach (var trail in _trails) alive += trail == null ? 0 : trail.Notes.Count;
                return alive;
            }
        }

        /// <summary>The share of the notes a second and the notes alive that is used, 0..1: 1 on the High tier, <c>trail.lowCountScale</c> on the Low one. May be changed while running.</summary>
        public float CountScale
        {
            get => _countScale;
            set
            {
                _countScale = Mathf.Clamp01(value);
                if (_baseSettings == null) return;
                _settings = _baseSettings.WithCountScale(_countScale);
                foreach (var trail in _trails) trail.Configure(_settings);
            }
        }

        /// <param name="parent">Where the notes are drawn: the view becomes its child (and takes its layer).</param>
        /// <param name="toLocal">Viewport (x to the right, y up, 0..1 across the picture) to a point in the space of <paramref name="parent"/>.</param>
        /// <param name="settings">The <c>trail</c> block (the High tier's numbers; see <see cref="CountScale"/>).</param>
        /// <param name="overlayFraction">The starting value of <see cref="OverlayFraction"/>.</param>
        /// <param name="sortingOrder">The sorting order of the notes among the other flat pictures of the overlay.</param>
        public void Configure(Transform parent, Func<float, float, Vector3> toLocal, TrailSettings settings, float overlayFraction, int sortingOrder)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            Release();
            _toLocal = toLocal ?? throw new ArgumentNullException(nameof(toLocal));
            _baseSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings = settings.WithCountScale(_countScale);
            OverlayFraction = overlayFraction;
            transform.SetParent(parent, false);
            gameObject.layer = parent.gameObject.layer;

            for (var slot = 0; slot < Slots; slot++) _trails[slot] = new HandNoteTrail(_settings, seed: 17 + slot);
            BuildBuffers(Slots * Math.Max(1, settings.MaxNotes));
            BuildRenderer(sortingOrder);
        }

        // the quads of the notes are written into these every frame; the picture coordinates and the triangles of a quad never change
        void BuildBuffers(int capacity)
        {
            _vertices = new Vector3[capacity * 4];
            _colors = new Color32[capacity * 4];
            _uvs = new Vector2[capacity * 4];
            _indices = new int[capacity * 6];
            for (var quad = 0; quad < capacity; quad++)
            {
                var v = quad * 4;
                _uvs[v] = new Vector2(0f, 0f);
                _uvs[v + 1] = new Vector2(1f, 0f);
                _uvs[v + 2] = new Vector2(1f, 1f);
                _uvs[v + 3] = new Vector2(0f, 1f);
                var i = quad * 6;
                _indices[i] = v; _indices[i + 1] = v + 2; _indices[i + 2] = v + 1;
                _indices[i + 3] = v; _indices[i + 4] = v + 3; _indices[i + 5] = v + 2;
            }
        }

        void BuildRenderer(int sortingOrder)
        {
            _glyph = NoteGlyphTexture.Create();
            _material = OverlayAssets.QuadMaterial("TrailNotes", _glyph);
            _mesh = new Mesh { name = "TrailNotes" };
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
            _renderer.sharedMaterial = _material;
            _renderer.sortingOrder = sortingOrder;
            _renderer.enabled = false;
        }

        /// <summary>Once per frame: steps the trail of each hand (<paramref name="hands"/> holds one entry per hand slot, up to two; a missing one is a hand that is not there) and draws the notes.</summary>
        public void Tick(double nowSec, TrailHandInput[] hands)
        {
            if (!IsConfigured) return;
            for (var slot = 0; slot < Slots; slot++)
            {
                var hand = hands != null && slot < hands.Length ? hands[slot] : default;
                _trails[slot].Step(nowSec, FrameOf(hand));
            }
            Draw(nowSec);
        }

        TrailHandFrame FrameOf(in TrailHandInput hand)
        {
            var (x, y) = HandScreenMap.Viewport(hand.X01, hand.Y01, OverlayFraction, RowSplit, HorizonViewport);
            return new TrailHandFrame(hand.Visible, hand.IsFist, x, y, hand.CaptureMs, hand.HandId, hand.Epoch, Pack(hand.Color));
        }

        void Draw(double nowSec)
        {
            var across = _toLocal(1f, 0.5f) - _toLocal(0f, 0.5f);
            var upward = _toLocal(0.5f, 1f) - _toLocal(0.5f, 0f);
            var ex = across.sqrMagnitude > 1e-12f ? across.normalized : Vector3.right;
            var ey = upward.sqrMagnitude > 1e-12f ? upward.normalized : Vector3.up;
            var halfEdge = 0.5f * _settings.Size / GlyphHeightShare * upward.magnitude;
            var quads = 0;
            var capacity = _vertices.Length / 4;
            for (var slot = 0; slot < Slots; slot++)
            {
                var notes = _trails[slot].Notes;
                for (var i = 0; i < notes.Count && quads < capacity; i++)
                {
                    if (WriteQuad(quads, notes[i], nowSec, ex, ey, halfEdge)) quads++;
                }
            }
            _drawn = quads;
            _renderer.enabled = quads > 0;
            if (quads == 0) return;
            _mesh.Clear(false);
            _mesh.SetVertices(_vertices, 0, quads * 4);
            _mesh.SetColors(_colors, 0, quads * 4);
            _mesh.SetUVs(0, _uvs, 0, quads * 4);
            _mesh.SetTriangles(_indices, 0, quads * 6, 0, false);
            _mesh.bounds = new Bounds(_toLocal(0.5f, 0.5f), new Vector3(1e4f, 1e4f, 1e4f));
        }

        // The square of one note, turned by its tilt, at full size when it is born and a little smaller as it fades. False (nothing written) for a note that cannot be seen.
        bool WriteQuad(int quad, TrailNote note, double nowSec, Vector3 ex, Vector3 ey, float halfEdge)
        {
            var age = note.Age01(nowSec, _settings.LifeSec);
            var alpha = TrailNote.Alpha(age);
            if (alpha < SmallestAlpha) return false;
            var center = _toLocal(note.XAt(nowSec), note.YAt(nowSec));
            var half = halfEdge * TrailNote.Scale(age);
            var cos = Mathf.Cos(note.Tilt) * half;
            var sin = Mathf.Sin(note.Tilt) * half;
            var v = quad * 4;
            _vertices[v] = center + ex * (-cos + sin) + ey * (-sin - cos);
            _vertices[v + 1] = center + ex * (cos + sin) + ey * (sin - cos);
            _vertices[v + 2] = center + ex * (cos - sin) + ey * (sin + cos);
            _vertices[v + 3] = center + ex * (-cos - sin) + ey * (-sin + cos);
            var color = Unpack(note.Tag, alpha);
            _colors[v] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = color;
            return true;
        }

        // The tag of a note is the colour of its cell (sRGB bytes); the shader gets it as linear.
        static int Pack(Color srgb)
        {
            var c = (Color32)srgb;
            return (c.r << 16) | (c.g << 8) | c.b;
        }

        static Color32 Unpack(int tag, float alpha)
        {
            var srgb = new Color(((tag >> 16) & 255) / 255f, ((tag >> 8) & 255) / 255f, (tag & 255) / 255f, alpha);
            var linear = srgb.linear;
            return new Color32((byte)Mathf.RoundToInt(linear.r * 255f), (byte)Mathf.RoundToInt(linear.g * 255f), (byte)Mathf.RoundToInt(linear.b * 255f), (byte)Mathf.RoundToInt(alpha * 255f));
        }

        void OnDestroy() => Release();

        // The components stay (a view can be configured again); what was made for them goes.
        void Release()
        {
            if (_renderer != null) _renderer.enabled = false;
            OverlayAssets.Release(_mesh);
            OverlayAssets.Release(_material);
            OverlayAssets.Release(_glyph);
            _mesh = null;
            _material = null;
            _glyph = null;
            _toLocal = null;
        }
    }
}
