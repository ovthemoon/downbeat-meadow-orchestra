using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>
    /// A plain dot per hand on the stage, placed where the palm is and growing with its speed. It exists to show (and to test by eye) that
    /// views get continuous hand data every frame, not only note events; it is not part of the final look.
    /// </summary>
    public sealed class HandCursorView
    {
        const float BaseSize = 0.8f;
        const float SizePerPalmSpeed = 0.12f;
        const float MaxPalmSpeed = 10f;
        const int SortingOrder = 500;
        const float Alpha = 1f;
        static readonly Color[] HandColors = { new Color(0.5f, 0.84f, 0.66f), new Color(0.95f, 0.82f, 0.48f) };

        readonly IHandMotionView _motion;
        readonly SpriteRenderer[] _dots = new SpriteRenderer[HandBufferLayout.MaxHands];

        public HandCursorView(Sprite disc, IHandMotionView motion)
        {
            _motion = motion;
            var root = new GameObject("HandCursorView").transform;
            for (var i = 0; i < _dots.Length; i++)
            {
                var dot = new GameObject("HandCursor" + i).AddComponent<SpriteRenderer>();
                dot.transform.SetParent(root, false);
                dot.sprite = disc;
                dot.sortingOrder = SortingOrder;
                dot.enabled = false;
                _dots[i] = dot;
            }
        }

        public void Tick()
        {
            for (var i = 0; i < _dots.Length; i++) _dots[i].enabled = false;
            for (var i = 0; i < _motion.Count; i++)
            {
                var hand = _motion.Hand(i);
                var dot = _dots[hand.Id];
                dot.enabled = true;
                dot.transform.localPosition = new Vector3((hand.X - 0.5f) * StageView.WorldWidth, (0.5f - hand.Y) * StageView.WorldHeight, 0f);
                var size = BaseSize + SizePerPalmSpeed * Mathf.Min(hand.SpeedPalms, MaxPalmSpeed);
                dot.transform.localScale = new Vector3(size, size, 1f);
                var color = HandColors[hand.Id % HandColors.Length];
                color.a = Alpha;
                dot.color = color;
            }
        }
    }
}
