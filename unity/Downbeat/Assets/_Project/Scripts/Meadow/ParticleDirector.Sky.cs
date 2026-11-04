using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>One effect in the air that was asked for (<see cref="ParticleDirector.EmitSky"/>): what, where, in what colour, how strongly, and the scene time at which it is made.</summary>
    public readonly struct SkyEffectRequest
    {
        public SkyEffectKind Kind { get; }
        public Vector3 WorldPoint { get; }
        public Color Color { get; }
        public float Strength01 { get; }
        public double SceneTime { get; }

        public SkyEffectRequest(SkyEffectKind kind, Vector3 worldPoint, Color color, float strength01, double sceneTime)
        {
            Kind = kind; WorldPoint = worldPoint; Color = color; Strength01 = strength01; SceneTime = sceneTime;
        }
    }

    /// <summary>
    /// The effects of the top row, made in the air at a point of the world (not on the ground): a ring of light that spreads (harp), seeds that rise and drift with the wind (recorder) and star dust that
    /// twinkles and falls slowly (bell). Three systems of their own, drawn without the haze (the sky is where they are). The numbers are <c>meadow.skyEffects</c>; the Low tier makes fewer.
    /// </summary>
    public sealed partial class ParticleDirector
    {
        const string RingName = "SkyRings";
        const string SeedName = "SkySeeds";
        const string StarName = "SkyStars";
        const float RingDotSize = 0.085f;
        const float RingStartRadius = 0.04f;
        const float RingDecay = 2.2f;          // per second: the dots leave fast and ease out (their speed falls as e^(-decay x time))
        const float RingDrag = RingDecay * 2f; // what the particle system is given: it applies half of its drag coefficient (measured: the speed of a dot falls as e^(-drag/2 x time))
        const float SeedSizeFactor = 2.2f;     // of meadow.particles.seedSize: in the air they are seen from farther than on the ground
        const float SeedLiftFactor = 0.6f;     // of meadow.particles.riseSpeed: the climb the seeds settle into, instead of the slow sink of the seeds on the ground
        const float StarSize = 0.14f;
        const float StarFallSpeed = 0.2f;
        const float StarSpread = 0.3f;         // metres: the dust comes down from a patch about this wide around the hand
        const int StarTwinkles = 6;            // dim-bright swings in a life (a gradient holds eight alpha keys)
        const float StarTwinkleLow = 0.2f;

        readonly List<SkyEffectRequest> _pendingSky = new List<SkyEffectRequest>(16);
        readonly int[] _emittedSky = new int[3];
        SkyEffectSettings _sky;
        bool _skyLow;
        Vector3 _skyRight = Vector3.right;
        Vector3 _skyUp = Vector3.up;
        Texture2D _star;
        ParticleSystem _skyRings;
        ParticleSystem _skySeeds;
        ParticleSystem _skyStars;

        /// <summary>Particles of an effect in the air that are alive now.</summary>
        public int AliveSky(SkyEffectKind kind) => SystemOf(kind) == null ? 0 : SystemOf(kind).particleCount;

        /// <summary>Particles of an effect in the air that were made so far (since the last <see cref="Configure"/>).</summary>
        public int EmittedSky(SkyEffectKind kind) => _emittedSky[(int)kind];

        /// <summary>Effects in the air that were asked for and are not due yet.</summary>
        public int PendingSkyCount => _pendingSky.Count;

        /// <summary>Whether this build of the director makes the fewer particles of the Low tier.</summary>
        public bool SkyIsLowTier => _skyLow;

        /// <summary>The places of the live particles of an effect in the air (world metres); for tests.</summary>
        public void GetSkyPositions(SkyEffectKind kind, List<Vector3> into)
        {
            into.Clear();
            var system = SystemOf(kind);
            if (system == null) return;
            var count = system.particleCount;
            if (_scratch.Length < count) _scratch = new ParticleSystem.Particle[count + 64];
            system.GetParticles(_scratch, count);
            for (var i = 0; i < count; i++) into.Add(_scratch[i].position);
        }

        /// <summary>
        /// Makes an effect in the air at <paramref name="worldPoint"/> when the scene clock reaches <paramref name="sceneTime"/> (at once when it already has). <paramref name="color"/> is the colour as
        /// <c>MeadowColors.Srgb(hex)</c> gives it (the cell's colour of <c>cells.colors</c>); <paramref name="strength01"/> is how hard the note was played (a softer note makes a smaller ring and fewer seeds
        /// or specks). The Low tier makes <c>meadow.skyEffects.lowCountScale</c> of the particles. Does nothing before <see cref="Configure"/>.
        /// </summary>
        public void EmitSky(SkyEffectKind kind, Vector3 worldPoint, Color color, float strength01, double sceneTime)
        {
            if (_skyRings == null) return;
            var request = new SkyEffectRequest(kind, worldPoint, color, strength01, sceneTime);
            if (sceneTime <= _field.WindTime) ThrowSky(request); else _pendingSky.Add(request);
        }

        /// <summary>The effects in the air that were asked for but are not due yet; a rebuild takes these along, like <see cref="PendingCues"/>.</summary>
        public List<SkyEffectRequest> PendingSkyEffects() => new List<SkyEffectRequest>(_pendingSky);

        /// <summary>Asks again for effects that <see cref="PendingSkyEffects"/> returned before a rebuild.</summary>
        public void EnqueueSkyAgain(IReadOnlyList<SkyEffectRequest> requests)
        {
            foreach (var request in requests) EmitSky(request.Kind, request.WorldPoint, request.Color, request.Strength01, request.SceneTime);
        }

        ParticleSystem SystemOf(SkyEffectKind kind)
        {
            switch (kind)
            {
                case SkyEffectKind.LightRing: return _skyRings;
                case SkyEffectKind.Seeds: return _skySeeds;
                default: return _skyStars;
            }
        }

        Texture2D TextureFor(string name) => name == "Fireflies" ? _glowDisc : (name == StarName ? _star : _disc);

        void ConfigureSky(SkyEffectsDto dto, MeadowQualityDto quality, MeadowCameraDto camera)
        {
            _sky = SkyEffectSettings.From(dto);
            _skyLow = SkyEffectSettings.IsLowTier(quality);
            _pendingSky.Clear();
            for (var i = 0; i < _emittedSky.Length; i++) _emittedSky[i] = 0;
            var lookDown = camera == null ? 0f : -camera.pitchDeg * Mathf.Deg2Rad;   // content: negative pitch = looking down
            _skyRight = Vector3.right;
            _skyUp = new Vector3(0f, Mathf.Cos(lookDown), Mathf.Sin(lookDown));   // up on the screen of the camera: the plane a ring spreads in faces the viewer
            _star = SoftDiscTexture.CreateStar();

            var seedColor = _settings.seedColor;
            _skyRings = Create(RingName, _sky.MaxParticles, seedColor, _sky.Glow, RingDotSize, _sky.LifeFor(SkyEffectKind.LightRing), true);
            _skySeeds = Create(SeedName, _sky.MaxParticles, seedColor, _sky.Glow, _settings.seedSize * SeedSizeFactor, _sky.LifeFor(SkyEffectKind.Seeds), true);
            _skyStars = Create(StarName, _sky.MaxParticles, seedColor, _sky.Glow, StarSize, _sky.LifeFor(SkyEffectKind.StarDust), true);
            foreach (var system in new[] { _skyRings, _skySeeds, _skyStars })
            {
                system.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("_HazeAmount", 0f);   // up in the sky nothing dissolves into the plate
            }
            ConfigureRings();
            ConfigureStars();
        }

        /// <summary>The dots of a ring leave fast and ease out (a drag, so the ring is wide and slows down), and fade at the end.</summary>
        void ConfigureRings()
        {
            var limit = _skyRings.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1000f);   // no speed limit: only the drag
            limit.drag = new ParticleSystem.MinMaxCurve(RingDrag);
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            var colorOverLife = _skyRings.colorOverLifetime;
            colorOverLife.color = fade;
        }

        /// <summary>Specks that swing between dim and bright, a few times in a life: they twinkle.</summary>
        void ConfigureStars()
        {
            var alphas = new GradientAlphaKey[StarTwinkles + 2];
            alphas[0] = new GradientAlphaKey(0f, 0f);
            for (var i = 0; i < StarTwinkles; i++)
            {
                var t = 0.1f + 0.8f * (i + 0.5f) / StarTwinkles;
                alphas[i + 1] = new GradientAlphaKey(i % 2 == 0 ? 1f : StarTwinkleLow, t);
            }
            alphas[StarTwinkles + 1] = new GradientAlphaKey(0f, 1f);
            var twinkle = new Gradient();
            twinkle.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            var colorOverLife = _skyStars.colorOverLifetime;
            colorOverLife.color = twinkle;
        }

        /// <summary>Once per frame: the effects that are due are made, and the seeds in the air drift with the wind of the meadow (and keep rising).</summary>
        void RunSky(double now)
        {
            if (_skyRings == null) return;
            for (var i = _pendingSky.Count - 1; i >= 0; i--)
            {
                if (_pendingSky[i].SceneTime > now) continue;
                ThrowSky(_pendingSky[i]);
                _pendingSky.RemoveAt(i);
            }
            Drift(_skySeeds, now, 1f, _settings.riseSpeed * SeedLiftFactor);
        }

        void ThrowSky(in SkyEffectRequest request)
        {
            var count = _sky.CountFor(request.Kind, request.Strength01, _skyLow);
            if (count <= 0) return;
            switch (request.Kind)
            {
                case SkyEffectKind.LightRing: ThrowRing(request, count); break;
                case SkyEffectKind.Seeds: ThrowSeeds(request, count); break;
                default: ThrowStars(request, count); break;
            }
            _emittedSky[(int)request.Kind] += count;
        }

        static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        /// <summary>The dots start in a small circle around the point, facing the viewer, and fly outwards; with the drag they stop at the radius that <see cref="SkyEffectSettings.RadiusFor"/> says.</summary>
        void ThrowRing(in SkyEffectRequest request, int count)
        {
            var life = _sky.LifeFor(SkyEffectKind.LightRing);
            var radius = _sky.RadiusFor(request.Strength01);
            var speed = radius * RingDecay / (1f - Mathf.Exp(-RingDecay * life));   // the distance a dot travels: speed / decay x (1 - e^(-decay x life))
            var phase = Random.value * Mathf.PI * 2f;
            var color = Opaque(request.Color);
            for (var i = 0; i < count; i++)
            {
                var angle = phase + Mathf.PI * 2f * i / count;
                var direction = _skyRight * Mathf.Cos(angle) + _skyUp * Mathf.Sin(angle);
                _skyRings.Emit(new ParticleSystem.EmitParams
                {
                    position = request.WorldPoint + direction * RingStartRadius,
                    velocity = direction * speed,
                    startLifetime = life,
                    startSize = RingDotSize,
                    startColor = color,
                }, 1);
            }
        }

        /// <summary>Seeds start within a hand's width of the point and climb; <see cref="Drift"/> carries them along with the wind.</summary>
        void ThrowSeeds(in SkyEffectRequest request, int count)
        {
            var life = _sky.LifeFor(SkyEffectKind.Seeds);
            var color = Opaque(request.Color);
            for (var i = 0; i < count; i++)
            {
                var offset = _skyRight * ((Random.value - 0.5f) * 2f * _settings.spawnSpread) + _skyUp * ((Random.value - 0.5f) * 2f * _settings.spawnSpread * 0.5f);
                _skySeeds.Emit(new ParticleSystem.EmitParams
                {
                    position = request.WorldPoint + offset,
                    velocity = new Vector3((Random.value - 0.5f) * 0.3f, _settings.riseSpeed * Mathf.Lerp(0.8f, 1.4f, Random.value), (Random.value - 0.5f) * 0.3f),
                    startLifetime = life * Mathf.Lerp(0.75f, 1.1f, Random.value),
                    startSize = _settings.seedSize * SeedSizeFactor * Mathf.Lerp(0.7f, 1.25f, Random.value),
                    startColor = color,
                    rotation = Random.value * 360f,
                }, 1);
            }
        }

        /// <summary>Specks start in a small patch around the point and come down slowly, each with its own life, so that they do not all twinkle together.</summary>
        void ThrowStars(in SkyEffectRequest request, int count)
        {
            var life = _sky.LifeFor(SkyEffectKind.StarDust);
            var color = Opaque(request.Color);
            for (var i = 0; i < count; i++)
            {
                var offset = _skyRight * ((Random.value - 0.5f) * 2f * StarSpread) + _skyUp * ((Random.value - 0.5f) * 2f * StarSpread);
                _skyStars.Emit(new ParticleSystem.EmitParams
                {
                    position = request.WorldPoint + offset,
                    velocity = new Vector3((Random.value - 0.5f) * 0.08f, -StarFallSpeed * Mathf.Lerp(0.6f, 1.2f, Random.value), (Random.value - 0.5f) * 0.08f),
                    startLifetime = life * Mathf.Lerp(0.75f, 1.15f, Random.value),
                    startSize = StarSize * Mathf.Lerp(0.6f, 1.3f, Random.value),
                    startColor = color,
                    rotation = Random.value * 90f,
                    angularVelocity = (Random.value - 0.5f) * 60f,
                }, 1);
            }
        }

        void ReleaseSky()
        {
            _pendingSky.Clear();
            if (_star != null) Destroy(_star);
            _star = null;
            _skyRings = _skySeeds = _skyStars = null;
        }
    }
}
