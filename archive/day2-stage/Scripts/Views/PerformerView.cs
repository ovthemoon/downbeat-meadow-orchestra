using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>
    /// One musician on the stage: idle sprite with a gentle sway, and on its own note the play sprite, a scale pulse, a small hop
    /// and an additive glow. Size, position and scale come from content.json (per performer), not from code.
    /// </summary>
    public sealed class PerformerView
    {
        const float PlaySpriteSeconds = 0.22f;
        const float PulseSeconds = 0.40f;
        const float HopSeconds = 0.22f;
        const float PulseScale = 0.10f;
        const float HopWorldUnits = 0.25f;
        const float GlowPeakAlpha = 1.0f;
        const float GlowMinScale = 3.6f;
        const float GlowMaxScale = 6.0f;
        const float GlowHeightFraction = 0.45f; // glow sits at 45% of the body height
        const float SwayHeight = 0.035f;
        const float SwayRate = 1.6f;
        const float BreathAmount = 0.01f;
        const int BodySortingOrder = 5;
        const int GlowSortingOrder = 4;

        readonly Transform _root;
        readonly SpriteRenderer _body;
        readonly SpriteRenderer _glow;
        readonly Sprite _idle;
        readonly Sprite _play;
        readonly Vector2 _foot;
        readonly float _baseScale;
        readonly float _phase;
        readonly Color _glowColor;
        float _flashAt = -100f;
        float _strength;

        public PerformerView(Transform parent, PerformerDto data, float pixelsPerUnit, Sprite idle, Sprite play, Color color, Material additive, Sprite disc, int index)
        {
            _idle = idle;
            _play = play;
            _foot = new Vector2(data.footX, data.footY);
            _baseScale = data.scale;
            _phase = index * 1.7f;
            _glowColor = color;

            var go = new GameObject("Performer-" + data.part);
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localPosition = new Vector3(_foot.x, _foot.y, 0f);

            _body = new GameObject("Body").AddComponent<SpriteRenderer>();
            _body.transform.SetParent(_root, false);
            _body.sprite = idle;
            _body.sortingOrder = BodySortingOrder;

            _glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            _glow.transform.SetParent(_root, false);
            _glow.transform.localPosition = new Vector3(0f, data.bodyHeightPx / pixelsPerUnit * GlowHeightFraction, 0f); // parent scale applies on top
            _glow.sprite = disc;
            _glow.sharedMaterial = additive;
            _glow.sortingOrder = GlowSortingOrder;
            _glow.color = new Color(color.r, color.g, color.b, 0f);
            _root.localScale = Vector3.one * _baseScale;
        }

        /// <summary>The performer plays: <paramref name="strength"/> is the note velocity (0..1).</summary>
        public void Flash(float strength, float time)
        {
            _flashAt = time;
            _strength = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(strength));
            _body.sprite = _play;
        }

        public void Tick(float time)
        {
            var since = time - _flashAt;
            if (since > PlaySpriteSeconds && _body.sprite != _idle) _body.sprite = _idle;

            var sway = Mathf.Sin(time * SwayRate + _phase) * SwayHeight;
            var breath = 1f + BreathAmount * Mathf.Sin(time * 1.1f + _phase);
            var pulse = 0f;
            var hop = 0f;
            var glow = 0f;
            var glowScale = GlowMaxScale;
            if (since >= 0f && since < PulseSeconds)
            {
                var e = 1f - since / PulseSeconds;
                pulse = PulseScale * _strength * e * e;
                glow = GlowPeakAlpha * _strength * e * e;
                glowScale = Mathf.Lerp(GlowMaxScale, GlowMinScale, e);
            }
            if (since >= 0f && since < HopSeconds) hop = HopWorldUnits * _strength * Mathf.Sin(Mathf.PI * since / HopSeconds);

            _root.localPosition = new Vector3(_foot.x, _foot.y + sway + hop, 0f);
            _root.localScale = Vector3.one * (_baseScale * breath * (1f + pulse));
            _glow.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b, glow);
            _glow.transform.localScale = Vector3.one * glowScale;
        }
    }
}
