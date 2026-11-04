using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The visitor's hands as ribbons of light: one TrailRenderer per hand (two at most), drawn additively, a trail of <c>ribbon.trailSec</c> seconds, drawn where the hand is on the screen
    /// (<see cref="HandScreenMap"/>, <see cref="HandPlacement"/>): a hand of the bottom row has its ribbon <c>ribbon.height</c> over the grass, on the line of sight through its screen point, and blows the grass at the
    /// ground that line meets; a hand of the top row has its ribbon in the air at the sky distance of the effects and blows nothing. The row is the row of the cell the hand plays in (<see cref="HandCellTracker"/>, dead band of the
    /// rows included), not the height of the drawn ribbon, so the ribbon and the wind change row exactly when the sound and the lit cell do; inside the dead band the ribbon is still drawn where the hand is on the screen. The ribbon starts the frame a hand arrives, at the hand's place (the first reaction is
    /// not delayed), then glides with <see cref="HandRibbonFollow"/> between tracker frames, filtering the jitter of a slow hand (the filter runs on the hand's place on the tracker's picture, so it is the same
    /// in both rows and across the line between them); it fades over <c>ribbon.fadeSec</c> when the hand is gone. Only the drawing is smoothed, never the strikes. No hand outline, bones or camera picture is ever shown.
    /// </summary>
    public sealed class HandWindView : MonoBehaviour
    {
        public const int Slots = 2;

        sealed class Ribbon
        {
            public GameObject Host;
            public TrailRenderer Trail;
            public Material Material;
            public float Alpha;
            public bool WasPresent;
            public int HandId;
            public int HandEpoch;
            public readonly HandRibbonFollow Follow = new HandRibbonFollow();
            public Vector3 Head;
            public bool InSky;
            public bool HasWind;
            public FieldPoint WindPlace;
            public FieldPoint WindVelocity;
        }

        [SerializeField] Shader ribbonShader;

        readonly Ribbon[] _ribbons = new Ribbon[Slots];
        MeadowRibbonDto _settings;
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        public bool IsConfigured => _settings != null;

        /// <summary>The brightness of the ribbon of a hand slot, 0 (gone) .. 1; for tests and the debug overlay.</summary>
        public float AlphaOf(int slot) => _ribbons[slot] == null ? 0f : _ribbons[slot].Alpha;

        public void Configure(MeadowRibbonDto settings)
        {
            Release();
            _settings = settings;
            var head = MeadowColors.Srgb(settings.color);
            var tail = MeadowColors.Srgb(settings.tailColor);
            for (var slot = 0; slot < Slots; slot++)
            {
                var host = new GameObject("HandRibbon" + slot);
                host.transform.SetParent(transform, false);
                var trail = host.AddComponent<TrailRenderer>();
                trail.time = settings.trailSec;
                trail.minVertexDistance = settings.minVertexDistance;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.numCapVertices = 4;
                trail.numCornerVertices = 3;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, settings.midWidth), new Keyframe(1f, 0f));
                trail.widthMultiplier = settings.width;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(new Color(head.r * settings.glow, head.g * settings.glow, head.b * settings.glow), 0f), new GradientColorKey(tail, 1f) },
                    new[] { new GradientAlphaKey(settings.headAlpha, 0f), new GradientAlphaKey(settings.midAlpha, 0.4f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
                var material = new Material(ribbonShader) { name = "Ribbon " + slot };
                material.SetFloat(FadeId, 0f);
                trail.sharedMaterial = material;
                trail.emitting = false;
                _ribbons[slot] = new Ribbon { Host = host, Trail = trail, Material = material };
            }
        }

        /// <summary>Where the head of the ribbon of a hand slot is in the world (the newest point of its trail), and whether it is in the air (the top row) or over the grass (the bottom row). False while no hand is there.</summary>
        public bool TryGetHead(int slot, out Vector3 head, out bool inSky)
        {
            head = default;
            inSky = false;
            var ribbon = slot >= 0 && slot < Slots ? _ribbons[slot] : null;
            if (ribbon == null || !ribbon.WasPresent) return false;
            head = ribbon.Head;
            inSky = ribbon.InSky;
            return true;
        }

        /// <summary>
        /// What the hand of a slot blows the grass with: the ground its ribbon is over (a little ahead along the motion, <c>wind.handLookAheadSec</c>, smoothed every drawn frame like the ribbon, so the grass does not jump when a tracker frame arrives)
        /// and how fast that ground point moves (metres per second). False for a hand that is not there, a hand of the top row (the grass only sways in the breeze), and a line of sight that never meets the ground.
        /// </summary>
        public bool TryGetWind(int slot, out FieldPoint place, out FieldPoint velocity)
        {
            place = default;
            velocity = default;
            var ribbon = slot >= 0 && slot < Slots ? _ribbons[slot] : null;
            if (ribbon == null || !ribbon.WasPresent || !ribbon.HasWind) return false;
            place = ribbon.WindPlace;
            velocity = ribbon.WindVelocity;
            return true;
        }

        /// <summary>
        /// Once per frame: places a ribbon on each tracked hand and fades the ones whose hand is gone. <paramref name="lookAheadSec"/> is how far ahead along its motion the wind of a hand is read. <paramref name="cells"/> says which
        /// row each hand plays in (updated for this frame before this is called); a hand that is in no cell, and every hand when it is null, is put in the row its height says (<see cref="HandPlacement.IsSkyRow"/>).
        /// </summary>
        public void Tick(IHandMotionView motion, HandPlacement placement, float lookAheadSec, float dt, HandCellTracker cells = null)
        {
            if (_settings == null) return;
            for (var slot = 0; slot < Slots; slot++)
            {
                var ribbon = _ribbons[slot];
                var present = motion != null && slot < motion.Count && motion.Hand(slot).IsTracked;
                if (present)
                {
                    var hand = motion.Hand(slot);
                    var isNew = !ribbon.WasPresent || hand.Id != ribbon.HandId || hand.Epoch != ribbon.HandEpoch;
                    if (isNew)
                    {
                        // a new hand, or the other hand moving into this slot, neither glides from where the last one left ...
                        ribbon.Follow.Reset();
                        ribbon.HandId = hand.Id;
                        ribbon.HandEpoch = hand.Epoch;
                    }
                    Place(ribbon, hand, placement, cells, lookAheadSec, dt);
                    if (isNew) ribbon.Trail.Clear();   // ... nor draws a line from there: the trail is cleared after the ribbon is moved, so that it starts at the new place
                }
                ribbon.Trail.emitting = present;
                ribbon.WasPresent = present;
                ribbon.Alpha = HandRibbonFade.Step(ribbon.Alpha, present, dt, _settings.appearSec, _settings.fadeSec);
                ribbon.Material.SetFloat(FadeId, ribbon.Alpha);
            }
        }

        // The smoothed place of the hand on the tracker's picture, then where that is in the world: in the air for the top row, on the line of sight over the grass for the bottom row.
        void Place(Ribbon ribbon, in HandMotion hand, HandPlacement placement, HandCellTracker cells, float lookAheadSec, float dt)
        {
            var scale = placement.FollowScale;
            var smoothed = ribbon.Follow.Step(new FieldPoint(hand.X * scale, hand.Y * scale), hand.CaptureMs, dt, _settings.followMinHz, _settings.followBeta, _settings.followSpeedHz);
            var x01 = smoothed.X / scale;
            var y01 = smoothed.Z / scale;
            var smoothing = _settings.followMinHz > 0f;   // 0 turns the smoothing off: the filter has no velocity and the raw one of the tracker is read
            var velocityX = smoothing ? ribbon.Follow.Velocity.X / scale : hand.VelocityX;
            var velocityY = smoothing ? ribbon.Follow.Velocity.Z / scale : hand.VelocityY;

            var (vx, vy) = placement.Viewport(x01, y01);
            ribbon.InSky = IsInSkyRow(cells, hand.Id, placement, y01);
            ribbon.HasWind = false;
            var onGround = !ribbon.InSky && placement.TryGroundRowHead(x01, y01, _settings.height, out ribbon.Head);
            if (!onGround)
            {
                ribbon.InSky = true;
                ribbon.Head = placement.SkyPoint(vx, vy);
            }
            ribbon.Host.transform.position = ribbon.Head;
            if (onGround) ribbon.HasWind = TryGroundWind(ribbon, placement, x01, y01, velocityX, velocityY, lookAheadSec);
        }

        // the row of the instrument the hand plays (held through the dead band of the rows, as the sound and the lit cell are), else the row its height says
        static bool IsInSkyRow(HandCellTracker cells, int handId, HandPlacement placement, float y01) =>
            cells != null && cells.TryGetRow(handId, out var isSky) ? isSky : placement.IsSkyRow(y01);

        // the ground under the hand now and a little ahead along its motion: the wind is read there, and its speed is that of the ground point
        static bool TryGroundWind(Ribbon ribbon, HandPlacement placement, float x01, float y01, float velocityX, float velocityY, float lookAheadSec)
        {
            if (!placement.TryGroundOfHand(x01, y01, out var here)) return false;
            if (!(lookAheadSec > 0f) || !placement.TryGroundOfHand(x01 + velocityX * lookAheadSec, y01 + velocityY * lookAheadSec, out var ahead))
            {
                ribbon.WindPlace = here;
                ribbon.WindVelocity = default;
                return true;
            }
            ribbon.WindPlace = ahead;
            ribbon.WindVelocity = new FieldPoint((ahead.X - here.X) / lookAheadSec, (ahead.Z - here.Z) / lookAheadSec);
            return true;
        }

        void OnDisable() => Release();

        void OnDestroy() => Release();

        void Release()
        {
            for (var i = 0; i < _ribbons.Length; i++)
            {
                var ribbon = _ribbons[i];
                if (ribbon == null) continue;
                if (ribbon.Material != null) Destroy(ribbon.Material);
                if (ribbon.Host != null) Destroy(ribbon.Host);
                _ribbons[i] = null;
            }
            _settings = null;
        }
    }
}
