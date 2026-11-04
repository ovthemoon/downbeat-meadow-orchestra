using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The small things in the air. Poppy petals (marimba notes) are thrown where the note was played, at the moment it is heard, and drift with the
    /// wind of the meadow (<see cref="WindField.SampleVector"/>: the rings of notes and the sweeps of the beat); fireflies appear and blink in proportion to <see cref="SetFireflyLevel"/>
    /// (the nature layers set it); a little pollen always hangs in the air. Three Unity particle systems (world space, additive or blended, a gradient texture made in code); the wind is applied
    /// to the live particles each frame from the CPU (a few hundred at most). Three more systems make the effects of the top row (harp, recorder, bell) in the air where the hand is (<see cref="EmitSky"/>, ParticleDirector.Sky.cs).
    /// </summary>
    public sealed partial class ParticleDirector : MonoBehaviour
    {
        const int PollenCapacityMargin = 2; // pollen lives a variable time: room for twice the average number

        struct Pending
        {
            public double SceneTime;
            public ParticleCue Cue;
        }

        /// <summary>A firefly that is glowing for a bell: which one (the particle's own random seed, which stays with it), and the scene time of the bell.</summary>
        struct Answer
        {
            public uint Seed;
            public double Start;
        }

        [SerializeField] Shader particleShader;

        readonly List<Pending> _pending = new List<Pending>(32);
        readonly List<Material> _materials = new List<Material>();
        readonly List<Answer> _answers = new List<Answer>(8);
        readonly List<FieldPoint> _positions = new List<FieldPoint>(512);
        readonly List<int> _picked = new List<int>(8);
        readonly List<Vector4> _custom = new List<Vector4>(512);
        FireflyAnswerSettings _answerSettings;
        bool _answerShown;
        ParticleSystem.Particle[] _scratch = new ParticleSystem.Particle[0];
        ParticleSystem _petals;
        ParticleSystem _fireflies;
        ParticleSystem _pollen;
        Texture2D _disc;
        Texture2D _glowDisc;
        MeadowParticlesDto _settings;
        IMeadowField _field;
        float _meanGround;
        int _fireflyMax;
        float _fireflyLevel;
        int _emittedPetals;

        public int AlivePetals => _petals == null ? 0 : _petals.particleCount;
        public int AliveFireflies => _fireflies == null ? 0 : _fireflies.particleCount;
        public int AlivePollen => _pollen == null ? 0 : _pollen.particleCount;
        public int EmittedPetals => _emittedPetals;
        public float FireflyLevel => _fireflyLevel;
        public int PendingCount => _pending.Count;
        /// <summary>Puts another size floor (pixels across) on the petals; 0 takes it off. For tests and the debug overlay: the content's value is set when the director is built.</summary>
        public void SetMinPixels(float pixels)
        {
            foreach (var material in _materials)
            {
                if (material.name == "Particle Petals") material.SetFloat("_MinPixels", pixels);
            }
        }

        /// <summary>Fireflies that are glowing for a bell right now.</summary>
        public int AnsweringFireflies => _answers.Count;

        /// <summary>The glow each firefly has for a bell now (0..1, in the order of the live particles); for tests.</summary>
        public void GetAnswerGlow(List<Vector4> into)
        {
            into.Clear();
            if (_fireflies != null) _fireflies.GetCustomParticleData(into, ParticleSystemCustomData.Custom1);
        }

        /// <param name="skyEffects">The numbers of the effects in the air (<c>meadow.skyEffects</c>); null = the defaults.</param>
        public void Configure(MeadowParticlesDto settings, MeadowQualityDto quality, IMeadowField field, MeadowLayout layout, MeadowCameraDto camera, SkyEffectsDto skyEffects = null)
        {
            Release();
            _settings = settings;
            _field = field;
            _layout = layout;
            _fireflyMax = quality.fireflyMax;
            _answerSettings = FireflyAnswerSettings.From(settings);
            _answers.Clear();
            _answerShown = false;
            _disc = SoftDiscTexture.Create();
            _glowDisc = SoftDiscTexture.CreateGlow();
            var halfWidthAtFar = layout.HalfWidthAt(layout.FarZ);
            _meanGround = MeanGround(field, layout);

            _petals = Create("Petals", settings.maxPetals, settings.petalColor, 1f, settings.petalSize, settings.petalLifeSec, false);
            _pollen = Create("Pollen", Mathf.Max(1, settings.pollenCount * PollenCapacityMargin), settings.pollenColor, 1.2f, settings.pollenSize, 12f, true);
            _fireflies = Create("Fireflies", Mathf.Max(1, _fireflyMax) + 8, settings.fireflyColor, settings.fireflyGlow, settings.fireflySize, settings.fireflyLifeSec, true);
            ConfigureFireflies(layout);
            ConfigurePollen(layout, halfWidthAtFar);
            ConfigureSky(skyEffects, quality, camera);
            _scratch = new ParticleSystem.Particle[settings.maxPetals];
            SetFireflyLevel(_fireflyLevel);
        }

        /// <summary>Fireflies wander in a box above the average ground (heights in content). 0 = no fireflies, 1 = <c>fireflyMax</c> of the tier; they fade in and out one by one, never all at once.</summary>
        public void SetFireflyLevel(float level)
        {
            _fireflyLevel = Mathf.Clamp01(level);
            if (_fireflies == null) return;
            _fireflyTarget = ParticleCues.FireflyTarget(_fireflyLevel, _fireflyMax);
        }

        int _fireflyTarget;

        /// <summary>A burst to throw at <paramref name="sceneTime"/> (the time of the wind clock at which the note is heard).</summary>
        /// <summary>The cues that were queued but are not due yet, with the scene time they are due at; a rebuild takes these along (see <see cref="Enqueue"/>).</summary>
        public List<(ParticleCue cue, double sceneTime)> PendingCues()
        {
            var waiting = new List<(ParticleCue, double)>(_pending.Count);
            foreach (var pending in _pending) waiting.Add((pending.Cue, pending.SceneTime));
            return waiting;
        }

        /// <summary>Queues again cues that <see cref="PendingCues"/> returned before a rebuild.</summary>
        public void EnqueueAgain(IReadOnlyList<(ParticleCue cue, double sceneTime)> cues)
        {
            foreach (var (cue, sceneTime) in cues) Enqueue(cue, sceneTime);
        }

        /// <summary>Takes back the cues (the ground's and the air's) that are due after <paramref name="sceneTime"/> (the notes were cancelled before they were heard); returns how many.</summary>
        public int CancelPendingAfter(double sceneTime)
        {
            var removed = _pending.RemoveAll(pending => pending.SceneTime > sceneTime);
            removed += _pendingSky.RemoveAll(request => request.SceneTime > sceneTime);
            return removed;
        }

        public void Enqueue(in ParticleCue cue, double sceneTime)
        {
            if (cue.Kind == ParticleKind.None || cue.Count <= 0 || _petals == null) return;
            _pending.Add(new Pending { SceneTime = sceneTime, Cue = cue });
        }

        void Update()
        {
            if (_petals == null) return;
            var now = _field.WindTime;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].SceneTime > now) continue;
                Throw(_pending[i].Cue);
                _pending.RemoveAt(i);
            }
            RunSky(now);
            Drift(_petals, now, 1f);
            Drift(_pollen, now, 0.3f);
            SpawnFireflies(Time.deltaTime);
            TendFireflies(now);
            // keep the number of fireflies near the target even when the level drops (the extra ones fade out at the end of their life; nothing pops)
        }

        void Throw(in ParticleCue cue)
        {
            var settings = _settings;
            var color = MeadowColors.Srgb(settings.petalColor);
            for (var i = 0; i < cue.Count; i++)
            {
                var x = cue.FieldPos.X + (Random.value - 0.5f) * 2f * settings.spawnSpread;
                var z = cue.FieldPos.Z + (Random.value - 0.5f) * 2f * settings.spawnSpread;
                var y = _field.HeightAt(x, z) + Mathf.Lerp(settings.spawnHeightMin, settings.spawnHeightMax, Random.value);
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, y, z),
                    velocity = new Vector3((Random.value - 0.5f) * 0.3f, settings.riseSpeed * Mathf.Lerp(0.6f, 1.2f, Random.value), (Random.value - 0.5f) * 0.3f),
                    startLifetime = settings.petalLifeSec * Mathf.Lerp(0.75f, 1.1f, Random.value),
                    startSize = settings.petalSize * Mathf.Lerp(0.7f, 1.25f, Random.value),
                    startColor = new Color(color.r, color.g, color.b, 1f),
                    rotation = Random.value * 360f,
                    angularVelocity = (Random.value - 0.5f) * 280f,
                };
                _petals.Emit(emit, 1);
            }
            _emittedPetals += cue.Count;
        }

        /// <summary>
        /// Moves the live particles of a system with the wind field and, once their first lift is over, eases their climb to <paramref name="settleUp"/> metres a second (the default is a slow sink; the seeds
        /// of the recorder in the air keep rising).
        /// </summary>
        void Drift(ParticleSystem system, double now, float follow, float? settleUp = null)
        {
            var count = system.particleCount;
            if (count == 0) return;
            if (_scratch.Length < count) _scratch = new ParticleSystem.Particle[count + 64];
            system.GetParticles(_scratch, count);
            var wind = _field.Wind;
            var steady = _field.WindDirection;
            var dt = Time.deltaTime;
            var settle = settleUp ?? -_settings.sinkSpeed;
            for (var i = 0; i < count; i++)
            {
                var p = _scratch[i];
                var position = p.position;
                var push = wind.SampleVector(position.x, position.z, now);
                var velocity = p.velocity;
                // the velocity eases towards the wind (in metres per second: a push of 1 is about windFollow m/s) and the particle sinks slowly
                var target = new Vector3(push.X, 0f, push.Z) * (_settings.windFollow * follow) + new Vector3(steady.X, 0f, steady.Z) * (_settings.windDrift * follow);   // the waves, and the steady wind
                var ease = 1f - Mathf.Exp(-3f * dt);
                velocity.x = Mathf.Lerp(velocity.x, target.x, ease);
                velocity.z = Mathf.Lerp(velocity.z, target.z, ease);
                var age = p.startLifetime - p.remainingLifetime;
                velocity.y = Mathf.Lerp(velocity.y, age < 0.5f ? velocity.y : settle, 1f - Mathf.Exp(-0.8f * dt));
                p.velocity = velocity;
                // never below the ground
                var ground = _field.HeightAt(position.x, position.z) + 0.03f;
                if (p.position.y < ground) { p.position = new Vector3(position.x, ground, position.z); p.velocity = new Vector3(velocity.x * 0.2f, 0f, velocity.z * 0.2f); }
                _scratch[i] = p;
            }
            system.SetParticles(_scratch, count);
        }

        /// <summary>
        /// Keeps about <c>target</c> fireflies alive: one lives <c>fireflyLifeSec</c> seconds, so that many per life-time are born, one by one, each a hand above the ground where it is born (the field
        /// rolls, so a box above the ground would put some into the hills and some into the sky).
        /// </summary>
        void SpawnFireflies(float dt)
        {
            if (_fireflyTarget <= 0) { _fireflyDebt = 0f; return; }
            _fireflyDebt += _fireflyTarget / Mathf.Max(_settings.fireflyLifeSec, 1f) * dt;
            while (_fireflyDebt >= 1f)
            {
                _fireflyDebt -= 1f;
                if (_fireflies.particleCount >= _fireflyTarget + 2) continue;
                var z = Mathf.Lerp(_layout.NearZ + _settings.fireflyEdgeMargin, _layout.FarZ - _settings.fireflyEdgeMargin, Mathf.Pow(Random.value, 0.8f));
                var x = (Random.value - 0.5f) * 2f * _layout.HalfWidthAt(z) * 0.98f;
                var y = _field.HeightAt(x, z) + Mathf.Lerp(_settings.fireflyHeightMin, _settings.fireflyHeightMax, Random.value);
                var life = _settings.fireflyLifeSec * Mathf.Lerp(0.7f, 1.2f, Random.value);
                _fireflies.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, y, z),
                    velocity = new Vector3((Random.value - 0.5f) * 0.1f, (Random.value - 0.5f) * 0.05f, (Random.value - 0.5f) * 0.1f),
                    startLifetime = life,
                    startSize = _settings.fireflySize * Mathf.Lerp(0.6f, 1.3f, Random.value),
                }, 1);
            }
        }

        float _fireflyDebt;
        MeadowLayout _layout;

        /// <summary>
        /// Once per frame: the noise that makes fireflies wander can push one into the ground of a hollow, and such a one is lifted back to a hand above it; the fireflies that are glowing for a bell get
        /// the strength of their glow written to the vertex stream (the shader makes them brighter by it).
        /// </summary>
        void TendFireflies(double now)
        {
            var system = _fireflies;
            var count = system.particleCount;
            if (count == 0)
            {
                _answers.Clear();
                return;
            }
            if (_scratch.Length < count) _scratch = new ParticleSystem.Particle[count + 64];
            system.GetParticles(_scratch, count);
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                var p = _scratch[i];
                var floor = _field.HeightAt(p.position.x, p.position.z) + _settings.fireflyFloor;
                if (p.position.y >= floor) continue;
                p.position = new Vector3(p.position.x, floor, p.position.z);
                _scratch[i] = p;
                changed = true;
            }
            if (changed)
            {
                system.SetParticles(_scratch, count);
                // SetParticles drops a particle that has no life left, so the list is read again: the glow is written for the fireflies that are there now, in the order they are there
                count = system.particleCount;
                system.GetParticles(_scratch, count);
            }
            if (_answers.Count > 0 || _answerShown) ShowAnswers(system, count, now);
        }

        void ShowAnswers(ParticleSystem system, int count, double now)
        {
            _custom.Clear();
            for (var i = 0; i < count; i++) _custom.Add(Vector4.zero);
            var settings = _answerSettings;
            for (var a = _answers.Count - 1; a >= 0; a--)
            {
                var age = (float)(now - _answers[a].Start);
                var index = age >= settings.AttackSec + settings.DecaySec ? -1 : IndexOfSeed(count, _answers[a].Seed);
                if (index < 0)
                {
                    _answers.RemoveAt(a);   // its glow is over, or the firefly is gone
                    continue;
                }
                _custom[index] = new Vector4(FireflyAnswer.Envelope(age, settings.AttackSec, settings.DecaySec), 0f, 0f, 0f);
            }
            system.SetCustomParticleData(_custom, ParticleSystemCustomData.Custom1);
            _answerShown = _answers.Count > 0;   // one more write after the last glow, so that nothing stays lit
        }

        /// <summary>The place of the answer of this firefly in the list, or -1. A plain loop: a lambda for FindIndex would allocate a closure at every bell, many thousands over a night.</summary>
        int IndexOfAnswer(uint seed)
        {
            for (var i = 0; i < _answers.Count; i++)
            {
                if (_answers[i].Seed == seed) return i;
            }
            return -1;
        }

        int IndexOfSeed(int count, uint seed)
        {
            for (var i = 0; i < count; i++)
            {
                if (_scratch[i].randomSeed == seed) return i;
            }
            return -1;
        }

        /// <summary>
        /// A bell of the nature score is heard at <paramref name="spot"/>: the nearest fireflies (as many as the content says, within its radius) glow for a moment. Returns how many.
        /// </summary>
        public int AnswerBell(FieldPoint spot)
        {
            if (_fireflies == null || _answerSettings == null || _answerSettings.Count == 0) return 0;
            var count = _fireflies.particleCount;
            if (count == 0) return 0;
            if (_scratch.Length < count) _scratch = new ParticleSystem.Particle[count + 64];
            _fireflies.GetParticles(_scratch, count);
            _positions.Clear();
            for (var i = 0; i < count; i++) _positions.Add(new FieldPoint(_scratch[i].position.x, _scratch[i].position.z));
            FireflyAnswer.Pick(_positions, spot, _answerSettings.Count, _answerSettings.Radius, _picked);
            var now = _field.WindTime;
            foreach (var index in _picked)
            {
                var seed = _scratch[index].randomSeed;
                var at = IndexOfAnswer(seed);
                var answer = new Answer { Seed = seed, Start = now };
                if (at >= 0) _answers[at] = answer; else _answers.Add(answer);
            }
            return _picked.Count;
        }

        /// <summary>
        /// The vertex layout MeadowParticle.shader reads. Unity packs the streams tightly, so a stream that is left out leaves zeros in the numbers behind it, which the shader treats as "nothing": the
        /// glow of a firefly that answers a bell (Custom1.x, <paramref name="answer"/>) comes along as one more number per particle, and the middle of the particle (Center, <paramref name="floor"/>) lets
        /// the vertex stage keep it at least <c>_MinPixels</c> across. A system that needs neither (the pollen) keeps the plain layout and its particles cost no more than they did.
        /// </summary>
        public static void ConfigureStreams(ParticleSystem system, ParticleSystemRenderer renderer, bool answer = true, bool floor = true)
        {
            if (!answer && !floor) return;
            var streams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X };
            if (floor) streams.Add(ParticleSystemVertexStream.Center);   // behind Custom1.x, which keeps its place: the shader's layout does not change
            renderer.SetActiveVertexStreams(streams);
            if (!answer) return;
            var custom = system.customData;
            custom.enabled = true;
            custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            custom.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(0f));
        }

        ParticleSystem Create(string name, int capacity, string colorHex, float glow, float size, float life, bool additive)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            main.startLifetime = life;
            main.startSize = size;
            var color = MeadowColors.Srgb(colorHex);
            main.startColor = new Color(color.r, color.g, color.b, 1f); // the glow is the material's gain: a vertex colour cannot be brighter than 1
            main.gravityModifier = 0f;
            main.startSpeed = 0f; // the default is 5 m/s along the shape's normal: these things drift, the wind and the noise move them
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.enabled = false;
            // fade in and out over the life, so nothing pops
            var colorOverLife = system.colorOverLifetime;
            colorOverLife.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = fade;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            ConfigureStreams(system, renderer, answer: name == "Fireflies", floor: name == "Petals");
            var material = new Material(particleShader) { name = "Particle " + name, mainTexture = TextureFor(name) }; // fireflies are points of light with a halo
            material.SetFloat("_Gain", glow);
            material.SetFloat("_AnswerGain", name == "Fireflies" ? _answerSettings.Gain : 0f);
            material.SetFloat("_MinPixels", name == "Petals" ? _settings.particleMinPixels : 0f);   // far petals stay a few points of light; pollen, fireflies and the effects in the air are as they are
            material.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); // the effects in the air, fireflies and pollen are light (added); petals are things (blended)
            _materials.Add(material);
            renderer.sharedMaterial = material;
            renderer.sortingFudge = 0f;
            system.Play();
            return system;
        }

        void ConfigureFireflies(MeadowLayout layout)
        {
            var main = _fireflies.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_settings.fireflyLifeSec * 0.7f, _settings.fireflyLifeSec * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(_settings.fireflySize * 0.6f, _settings.fireflySize * 1.3f);
            var noise = _fireflies.noise;
            noise.enabled = true;
            noise.strength = _settings.fireflyNoiseStrength;
            noise.frequency = _settings.fireflyNoiseFrequency;
            noise.scrollSpeed = 0.25f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            // a blink: the brightness breathes several times in a life
            var colorOverLife = _fireflies.colorOverLifetime;
            var blink = new Gradient();
            var blinkKeys = _settings.fireflyBlinkCount;
            var fadeFraction = _settings.fireflyFadeFraction;
            var alphas = new GradientAlphaKey[blinkKeys + 2];
            alphas[0] = new GradientAlphaKey(0f, 0f);
            for (var i = 0; i < blinkKeys; i++)
            {
                var t = fadeFraction + (1f - 2f * fadeFraction) * (i + 0.5f) / blinkKeys;
                alphas[i + 1] = new GradientAlphaKey(i % 2 == 0 ? 1f : _settings.fireflyBlinkLow, t);
            }
            alphas[blinkKeys + 1] = new GradientAlphaKey(0f, 1f);
            blink.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            colorOverLife.color = blink;
        }

        /// <summary>The average height of the ground over the field: the boxes of fireflies and pollen sit above it (the ground rolls by about half a metre).</summary>
        static float MeanGround(IMeadowField field, MeadowLayout layout)
        {
            var sum = 0f;
            const int Samples = 6;
            for (var i = 0; i < Samples; i++)
            {
                for (var j = 0; j < Samples; j++)
                {
                    var z = Mathf.Lerp(layout.NearZ, layout.FarZ, (i + 0.5f) / Samples);
                    var x = Mathf.Lerp(-1f, 1f, (j + 0.5f) / Samples) * layout.HalfWidthAt(z);
                    sum += field.HeightAt(x, z);
                }
            }
            return sum / (Samples * Samples);
        }

        void ConfigurePollen(MeadowLayout layout, float halfWidthAtFar)
        {
            var main = _pollen.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(_settings.pollenSize * 0.6f, _settings.pollenSize * 1.4f);
            var emission = _pollen.emission;
            emission.rateOverTime = _settings.pollenCount / 11f; // the average life is 11 s
            var shape = _pollen.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            var near = layout.NearZ + 0.3f;
            var far = layout.FarZ;
            shape.position = new Vector3(0f, _meanGround + 0.8f, (near + far) * 0.5f);
            shape.scale = new Vector3(layout.HalfWidthAt(far) * 2f, 1.6f, far - near);
            var noise = _pollen.noise;
            noise.enabled = true;
            noise.strength = _settings.pollenDrift;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;
            var steady = _field.WindDirection;   // the steady wind carries the pollen along (a constant velocity in world space)
            var carry = _pollen.velocityOverLifetime;
            carry.enabled = true;
            carry.space = ParticleSystemSimulationSpace.World;
            carry.x = steady.X * _settings.windDrift;
            carry.y = 0f;
            carry.z = steady.Z * _settings.windDrift;
            var colorOverLife = _pollen.colorOverLifetime;
            var twinkle = new Gradient();
            twinkle.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.2f), new GradientAlphaKey(0.25f, 0.4f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0.3f, 0.8f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = twinkle;
        }

        void OnDisable() => Release(); // switched off: nothing is kept (MeadowStage builds it again when it is switched on)

        void OnDestroy() => Release();

        void Release()
        {
            foreach (var material in _materials) { if (material != null) Destroy(material); }
            _materials.Clear();
            if (_disc != null) Destroy(_disc);
            if (_glowDisc != null) Destroy(_glowDisc);
            _glowDisc = null;
            _disc = null;
            _pending.Clear();
            ReleaseSky();
            foreach (Transform child in transform) Destroy(child.gameObject);
            _petals = _fireflies = _pollen = null;
        }
    }
}
