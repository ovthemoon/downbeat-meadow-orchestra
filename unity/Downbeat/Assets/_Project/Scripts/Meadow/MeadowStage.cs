using System;
using System.Collections.Generic;
using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The 3D meadow as one object: camera, ground, backdrop plates, sun, grass, flowers and wind. The scene holds them switched off with nothing in
    /// them; <see cref="Configure"/> fills them from the loaded content and switches them on, so every number comes from content.json. Every played note reaches the meadow
    /// through <see cref="OnNote"/> (flowers open, a ring of wind starts). Debug key (until the beat drives the wind): Space sends a sweep across the field.
    /// </summary>
    public sealed class MeadowStage : MonoBehaviour, IMeadowField, IHandScreen
    {
        const int HandSlots = 2;
        const float DemoSecondRingExtraDepth = 3f;
        const float GroundLevelShare = 0.5f;    // the ground plane the far plate is mapped onto is half way up the terrain's range of heights
        const float FlowerHeadAllowance = 1.6f; // the head of a flower rises above the end of its stem by up to this many head sizes
        const float HorizonClampTolerance = 1e-4f;   // the measured horizon counts as moved when the rows use a viewport y this far from it

        [SerializeField] MeadowCameraRig cameraRig;
        [SerializeField] BackdropView backdrop;
        [SerializeField] TerrainView terrain;
        [SerializeField] GrassRenderer grass;
        [SerializeField] FlowerField flowers;
        [SerializeField] ParticleDirector particles;
        [SerializeField] HandWindView handWind;
        [SerializeField] GameObject content;

        WindFieldGpu _windGpu;
        BreezeGpu _breezeGpu;
        BreezeField _breezeField;
        int _pitchSteps;
        readonly List<WakeBloom> _wakePlan = new List<WakeBloom>(32);
        readonly int[] _wakeOne = new int[1];
        double _configuredAt;
        bool _demoSweepSent;
        bool _demoRadialSent;

        /// <summary>The meadow was built from a tier: the grass, the flowers and the levers of the tier are in place (after a change of tier, a content reload, or switching the meadow off and on).</summary>
        public event Action QualityApplied;

        public bool IsConfigured { get; private set; }
        public MeadowLayout Layout { get; private set; }
        public Camera Camera => cameraRig.Camera;
        public BackdropView Backdrop => backdrop;
        public TerrainView Terrain => terrain;
        public GrassRenderer Grass => grass;
        public FlowerField Flowers => flowers;
        public ParticleDirector Particles => particles;
        public HandWindView HandWind => handWind;
        /// <summary>The layer over the picture, a child of the meadow camera, for things drawn on the screen itself (not on the ground); null before the meadow is built.</summary>
        public MeadowOverlayLayer Overlay { get; private set; }
        /// <summary>The note trail behind the hands and the names of the cells, on <see cref="Overlay"/>; null before the meadow is built.</summary>
        public TrailOverlay Trail { get; private set; }
        /// <summary>Where a hand is in the meadow, found from where it is on the screen: the ground under it, the air in front of it; null before the meadow is built.</summary>
        public HandPlacement Placement { get; private set; }
        /// <summary>Turns played notes into open flowers and rings of wind; null before the content is loaded.</summary>
        public MeadowNoteRouter Router { get; private set; }
        public MeadowDto Settings { get; private set; }
        public WindField Wind { get; private set; }
        /// <summary>Name of the quality tier in use (a tier of content <c>meadow.quality</c>).</summary>
        public string QualityName { get; private set; }
        /// <summary>The steady wind (lean, gusts, sway, cloud drift); null before the content is loaded.</summary>
        public BreezeModel Breeze => _breezeGpu?.Model;
        /// <summary>The clock the wind waves are timed with (the scene clock).</summary>
        public static double WindTime => Time.timeAsDouble;
        double IMeadowField.WindTime => WindTime;
        WindField IMeadowField.Wind => Wind;
        FieldPoint IMeadowField.WindDirection => Breeze != null ? Breeze.Direction : default;
        float IMeadowField.HeightAt(float x, float z) => terrain.HeightAt(x, z);
        float IHandScreen.OverlayFraction => Overlay != null ? Overlay.StripFraction : 0f;
        float IHandScreen.RowSplit => Overlay != null ? Overlay.RowSplit : MeadowOverlayLayer.DefaultRowSplit;
        float IHandScreen.HorizonViewport => Overlay != null ? Overlay.HorizonViewport : float.NaN;
        /// <summary>Send a sweep and a ring shortly after start-up, so a capture shows the wind without any input.</summary>
        public bool DemoWaves { get; set; }
        /// <summary>Parts of the wind that are switched off (<c>--wind-mute</c>, to find which one makes the grass do something); set before <see cref="Configure"/>.</summary>
        public WindTerm WindMuted { get; set; }

        void Awake()
        {
            if (content != null) content.SetActive(false);
        }

        /// <summary>The content the meadow was built from (kept so that it can be built again after being switched off and on).</summary>
        public ContentBundle Bundle { get; private set; }

        /// <summary>
        /// Builds the meadow from the content. Calling it again builds it again: what the first call made (the GPU buffer of the wind, the flower, grass and particle buffers, the
        /// subscription to the router) is released first, nothing is left behind.
        /// </summary>
        public void Configure(ContentBundle bundle, string qualityName = null)
        {
            _windGpu?.Dispose();
            _windGpu = null;
            _breezeGpu = null;
            Bundle = bundle;
            var meadow = bundle.Dto.meadow;
            Settings = meadow;
            Layout = new MeadowLayout(meadow.layout.fieldWidth, meadow.layout.nearZ, meadow.layout.farZ, meadow.layout.horizonScreenY);
            _pitchSteps = bundle.Dto.song.pitchSteps;
            Wind = new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth);
            _windGpu = new WindFieldGpu(meadow.wind, WindMuted);
            _breezeGpu = new BreezeGpu(meadow.breeze, meadow.sky.clouds);
            _breezeField?.Dispose();
            _breezeField = new BreezeField();

            terrain.Configure(meadow.terrain, meadow.camera);
            cameraRig.Configure(meadow.camera, terrain.BaseHeightAt(0f, 0f));   // the hummocks do not lift or lower the camera
            RenderLevers.SetWindowCamera(cameraRig.Camera);   // the render scale is for this camera alone
            if (Overlay == null || Overlay.Viewer != cameraRig.Camera) Overlay = MeadowOverlayLayer.Create(cameraRig.Camera);
            Placement = new HandPlacement(cameraRig.Camera, terrain.HeightAt, Overlay, Layout, meadow.terrain.maxHeight * GroundLevelShare);
            Overlay.RowSplit = CellSettings.From(bundle.Dto.cells).RowSplit;
            Overlay.HorizonViewport = Placement.FarEdgeViewport();   // where the sky row ends and the ground row begins, seen by this camera
            WarnIfHorizonWasClamped(Overlay.HorizonViewport, Mathf.Max(Overlay.StripFraction, bundle.Dto.display != null ? bundle.Dto.display.overlayFraction : 0f));
            Trail?.Dispose();
            Trail = new TrailOverlay(Overlay, bundle.Dto);
            backdrop.Configure(meadow.sky, meadow.haze, bundle, meadow.terrain.maxHeight * GroundLevelShare);
            if (content != null) content.SetActive(true);
            ApplyQuality(qualityName);
            _configuredAt = WindTime;
            IsConfigured = true;
        }

        // The rows meet at the horizon the camera measured. HandScreenMap keeps it inside the picture, above the record strip (the widest it is shown), which only a camera that does not look at the field would make it do: say so once.
        static void WarnIfHorizonWasClamped(float measured, float stripFraction)
        {
            var used = HandScreenMap.HorizonOf(measured, stripFraction);
            if (!float.IsNaN(measured) && Mathf.Abs(used - measured) <= HorizonClampTolerance) return;
            Debug.LogWarning($"[MeadowStage] the far edge of the field is at {measured:0.###} of the picture's height, which the rows cannot meet at (record strip {stripFraction:0.##}); they meet at {used:0.###} instead (check meadow.camera and meadow.layout)");
        }

        /// <summary>Switches to the named tier of <c>meadow.quality</c> (the first tier when the name is empty or unknown): the grass density and the matching Unity quality level.</summary>
        public void ApplyQuality(string name)
        {
            var tiers = Settings.quality;
            var chosen = tiers[0];
            foreach (var tier in tiers)
            {
                if (tier.name == name) chosen = tier;
            }
            if (!string.IsNullOrEmpty(name) && chosen.name != name) Debug.LogWarning($"[MeadowStage] quality '{name}' is not in content.json; using '{chosen.name}'");

            var levels = QualitySettings.names;
            for (var i = 0; i < levels.Length; i++)
            {
                if (levels[i] == chosen.name) QualitySettings.SetQualityLevel(i, true);
            }
            QualityName = chosen.name;
            Trail?.SetCheaperTier(chosen != tiers[0]);   // the notes are fewer on a cheaper tier (trail.lowCountScale)
            RenderLevers.Apply(chosen, Launch ?? LaunchArgs.Current);

            var blooms = flowers.BloomsNotYetHeard(_heardNow);   // notes that were scheduled but not heard yet: their flowers and particles must not be lost by a rebuild (F5)
            var cues = particles.PendingCues();
            var skyCues = particles.PendingSkyEffects();
            if (Router != null) Router.NoteRouted -= OnNoteRouted; // the router of an earlier build is gone: no second subscription
            var flowerSettings = Settings.flowers;
            var placed = FlowerPlacement.Generate(flowerSettings.seed, chosen.flowerCount, Layout, flowerSettings.blend);
            flowers.Build(flowerSettings, placed, terrain.HeightAt, Settings.camera, MaxFlowerHeight(flowerSettings));
            grass.Build(Settings.grass, chosen, Settings.terrain, Settings.camera, cameraRig.transform.position.y, terrain, flowers.ClearingsFor(flowerSettings.clearRadius));
            Placement.SkyDistance = SkyEffectSettings.From(Settings.skyEffects).Distance;
            Router = new MeadowNoteRouter(flowers.Instances, Layout, Wind, flowers, flowerSettings, Settings.wind, _pitchSteps, ToWindTime, Placement.TryGroundOfHand);
            particles.Configure(Settings.particles, chosen, this, Layout, Settings.camera, Settings.skyEffects);
            particles.SetFireflyLevel(_startFireflyLevel);
            flowers.BloomAgain(blooms);
            particles.EnqueueAgain(cues);
            particles.EnqueueSkyAgain(skyCues);
            handWind.Configure(Settings.ribbon);
            Router.NoteRouted += OnNoteRouted;
            Debug.Log($"[MeadowStage] quality {chosen.name}: {chosen.grassPerSqm:0} blades per m2, {grass.BladeCount} blades, {flowers.Count} flowers, up to {chosen.fireflyMax} fireflies");
            QualityApplied?.Invoke();
        }

        /// <summary>How high the tallest flower reaches (metres): the tallest stem of the three species at the tallest relative height, and its head.</summary>
        static float MaxFlowerHeight(MeadowFlowersDto f)
        {
            float tallest = 0f;
            foreach (var look in new[] { f.bellflower, f.dandelion, f.poppy }) tallest = Mathf.Max(tallest, look.stemHeight * FlowerPlacement.MaxHeight + look.headSize * FlowerHeadAllowance);
            return tallest;
        }

        /// <summary>The wind runs on the scene clock, notes on the audio clock: a note heard <c>audible - heardNow</c> seconds from now starts its ring that long after now.</summary>
        double ToWindTime(double audibleTime, double heardNow) => WindTime + (audibleTime - heardNow);

        /// <summary>Hands to show instead of the tracked ones (tests and captures); null = the real ones.</summary>
        public IHandMotionView HandsOverride { get; set; }
        /// <summary>The command line of this run (<c>--render-scale</c>, <c>--max-fps</c> go with the quality tier); the process's own when not set.</summary>
        public LaunchArgs Launch { get; set; }

        float _startFireflyLevel;
        double _noteHeardNow;
        double _heardNow;

        /// <summary>The level of the fireflies, 0..1 (the nature layers set it; <c>--fireflies L</c> sets the start level).</summary>
        public void SetFireflyLevel(float level)
        {
            _startFireflyLevel = Mathf.Clamp01(level);
            if (particles != null) particles.SetFireflyLevel(level);
        }

        /// <summary>A note was scheduled: the flowers of its instrument around the spot open when it is heard, a ring of wind starts then, and seeds or petals are thrown.</summary>
        public void OnNote(in ScheduledNote note, double heardNow)
        {
            if (Router == null) return;
            _noteHeardNow = heardNow;
            Router.OnNote(note, heardNow);
        }

        /// <summary>
        /// The piece was ended: notes that were heard after <paramref name="audibleAfter"/> were asked for but their sound is cancelled, so what they asked for is taken back - the flowers that were to open, the rings and waves
        /// of wind, the petals, seeds and star dust. Nothing blooms after the end. <paramref name="heardNow"/> is the audio time of the sample leaving the speakers now.
        /// </summary>
        public void CancelNotesHeardAfter(double audibleAfter, double heardNow)
        {
            if (!IsConfigured) return;
            var sceneTime = ToWindTime(audibleAfter, heardNow);
            flowers.CancelBloomsAfter(audibleAfter);
            Wind.RemoveStartingAfter(sceneTime);
            particles.CancelPendingAfter(sceneTime);
        }

        void OnNoteRouted(MeadowNoteEvent routed)
        {
            particles.Enqueue(ParticleCues.ForNote(routed), ToWindTime(routed.AudibleTime, _noteHeardNow));
            EmitSkyOfNote(routed);
        }

        /// <summary>
        /// A note of the upper row makes its effect in the air where the hand is on the screen (the ring of the harp, the seeds of the recorder, the star dust of the bell), in the colour of its cell, as hard as the
        /// note was played, when the note is heard. A note that does not know where the hand was (<see cref="MeadowNoteEvent.X01"/> or <see cref="MeadowNoteEvent.Y01"/> is NaN) makes it in the middle of its own cell.
        /// </summary>
        void EmitSkyOfNote(in MeadowNoteEvent routed)
        {
            var kind = SkyEffectSettings.KindFor(routed.Part);
            if (!kind.HasValue || Overlay == null) return;
            var cells = Bundle.Dto.cells;
            var x01 = float.IsNaN(routed.X01) ? SpeciesMap.CentreOf(routed.Part) : routed.X01;
            var y01 = float.IsNaN(routed.Y01) ? (cells != null ? cells.rowSplit : 0.5f) * 0.5f : routed.Y01;   // the middle of the upper row
            var (vx, vy) = Overlay.ViewportOfHand(x01, y01);
            EmitSky(kind.Value, vx, vy, MeadowColors.Srgb(CellColorHex(routed.Part)), routed.Velocity, routed.AudibleTime);
        }

        /// <summary>The colour of the cell of a part, <c>#RRGGBB</c> (<c>cells.colors</c>; the defaults of <see cref="CellsDto"/> when the content has none).</summary>
        string CellColorHex(Part part)
        {
            var colors = Bundle.Dto.cells?.colors;
            var index = (int)part;
            return colors != null && index < colors.Length ? colors[index] : CellsDto.DefaultColors[index];
        }

        /// <summary>Once per frame, with the audio time of the sample leaving the speakers: runs the blooms.</summary>
        public void Tick(double heardNow)
        {
            _heardNow = heardNow;
            if (IsConfigured) flowers.Tick(heardNow);
        }

        /// <summary>A sweep that crosses the field from the left edge on a beat. <paramref name="energy01"/> is the performance energy, <paramref name="downbeat"/> the first beat of a bar.</summary>
        public void AddBeatSweep(float energy01, bool downbeat)
        {
            var sweep = Settings.wind.beatSweep;
            var amplitude = sweep.amplitude * (1f + sweep.energyGain * Mathf.Clamp01(energy01)) * (downbeat ? sweep.downbeatGain : 1f);
            var startX = -(Settings.grass.rings[Settings.grass.rings.Length - 1].zTo * MeadowFrustum.HalfWidthPerDepth(Settings.camera.fovDeg) + Settings.terrain.xMargin);
            Wind.Add(new WindWave(startX, 0f, WindTime, amplitude, sweep.speed, sweep.lifeSec, WindWaveKind.Sweep, 0f));
        }

        /// <summary>A ring that spreads from a point on the ground, started by hand (tests and the demo; played notes get theirs from the router). <paramref name="glow"/> above 0 makes the tips it passes sparkle.</summary>
        public void AddNoteRadial(FieldPoint at, float velocity01, float glow = 0f)
        {
            var radial = Settings.wind.noteRadial;
            var amplitude = radial.amplitude * (1f + radial.velocityGain * Mathf.Clamp01(velocity01));
            Wind.Add(new WindWave(at.X, at.Z, WindTime, amplitude, radial.speed, radial.lifeSec, WindWaveKind.Radial, glow));
        }

        /// <summary>
        /// A long low wave that lays the grass towards the side of the field the hand is on (the low string; <c>meadow.wind.longWave</c>). Like <see cref="AddNoteRadial"/> it starts now; a note of the
        /// router builds the same wave with <see cref="WindWaves.Long"/> at the moment it is heard.
        /// </summary>
        public void AddLongWave(FieldPoint at, float velocity01) =>
            Wind.Add(WindWaves.Long(at, velocity01, WindTime, Settings.wind.longWave));

        /// <summary>A strong wide ring that spreads from where the drum was hit (the hand drum; <c>meadow.wind.drumRing</c>); starts now (see <see cref="AddLongWave"/>, <see cref="WindWaves.Drum"/>).</summary>
        public void AddDrumRing(FieldPoint at, float velocity01) =>
            Wind.Add(WindWaves.Drum(at, velocity01, WindTime, Settings.wind.drumRing));

        /// <summary>
        /// The point in the air that the screen point (<paramref name="vx"/>, <paramref name="vy"/>) looks at, in viewport coordinates (0..1, y up: <see cref="HandScreenMap"/> gives them), at the distance in
        /// front of the camera that <c>meadow.skyEffects.distance</c> says: over the meadow, not on the ground (a point that would be under a hill just above the horizon is brought nearer, on the same line of sight).
        /// </summary>
        public Vector3 SkyPoint(float vx, float vy) => Placement.SkyPoint(vx, vy);

        /// <summary>
        /// An effect of the top row in the air where the screen point (<paramref name="vx"/>, <paramref name="vy"/>) is, made when the note is heard (<paramref name="audibleTime"/>, on the audio clock; call it while the
        /// note is routed, so that the audio time of now is known). <paramref name="color"/> is as <c>MeadowColors.Srgb(hex)</c> gives it.
        /// </summary>
        public void EmitSky(SkyEffectKind kind, float vx, float vy, Color color, float strength01, double audibleTime)
        {
            if (!IsConfigured) return;
            particles.EmitSky(kind, SkyPoint(vx, vy), color, strength01, ToWindTime(audibleTime, _noteHeardNow));
        }

        /// <summary>The same, made at once (tests, captures, the demo): nothing to wait for.</summary>
        public void EmitSkyNow(SkyEffectKind kind, float vx, float vy, Color color, float strength01)
        {
            if (!IsConfigured) return;
            particles.EmitSky(kind, SkyPoint(vx, vy), color, strength01, WindTime);
        }

        /// <summary>
        /// The waking moment: a hand has come into an idle meadow. The buds around <paramref name="origin"/> glint in a wave that spreads from it (<c>meadow.wake</c>: each opens when the
        /// front reaches it, timed to the audio clock <paramref name="heardNow"/> like a played note), and one soft ring of wind leaves the same spot. Returns how many flowers will glint.
        /// </summary>
        public int Wake(FieldPoint origin, double heardNow)
        {
            if (!IsConfigured) return 0;
            var wake = Settings.wake;
            WakePlanner.Plan(flowers.Instances, origin, WakeSettings.From(wake), _wakePlan);
            foreach (var bloom in _wakePlan)
            {
                _wakeOne[0] = bloom.Flower;
                flowers.Bloom(_wakeOne, heardNow + bloom.DelaySec);
            }
            Wind.Add(new WindWave(origin.X, origin.Z, WindTime, wake.gustAmplitude, wake.speed, wake.gustLifeSec, WindWaveKind.Radial, wake.gustGlow));
            return _wakePlan.Count;
        }

        /// <summary>A bell of the nature score is heard (its pitch step and slot): the fireflies nearest to where it is heard glow for a moment. Returns how many.</summary>
        public int AnswerBell(int pitchStep, long slot) => IsConfigured && particles != null ? particles.AnswerBell(FireflyAnswer.BellSpot(pitchStep, _pitchSteps, slot, Layout)) : 0;

        /// <summary>
        /// Draws the hands where they are on the screen and lets the ones over the ground blow the grass: each tracked hand's ribbon (<see cref="HandWindView"/>) is on the line of sight through its screen point, and the wind
        /// is that of the ground the line meets. The place and the velocity are those of the ribbon (smoothed every drawn frame, so the wind of a hand glides between tracker frames instead of stepping with them), read a little ahead along the motion.
        /// A hand of the top row blows nothing. <paramref name="deltaSec"/> is the time of the drawn frame (tests set it; the real frame time otherwise).
        /// </summary>
        public void SetHands(IHandMotionView motion, float? deltaSec = null)
        {
            if (!IsConfigured) return;
            if (HandsOverride != null) motion = HandsOverride;
            var dt = deltaSec ?? Time.deltaTime;
            Trail?.Tick(motion, dt);   // first: it picks the cell of each hand, whose row the ribbon and the wind follow
            handWind.Tick(motion, Placement, Settings.wind.handLookAheadSec, dt, Trail?.Cells);
            for (var slot = 0; slot < HandSlots; slot++)
            {
                if (handWind.TryGetWind(slot, out var place, out var velocity)) _windGpu.SetHand(slot, true, place.X, place.Z, velocity.X, velocity.Z);
                else _windGpu.SetHand(slot, false, 0f, 0f, 0f, 0f);
            }
        }

        /// <summary>What a hand slot blows the grass with right now (see <see cref="SetHands"/>): whether it is there, its place on the ground and its velocity.</summary>
        public (bool active, float x, float z, float vx, float vz) WindHand(int slot) => _windGpu.HandAt(slot);

        void Update()
        {
            if (!IsConfigured) return;
            ReadDebugInput();
            RunDemo(WindTime);
            UploadWind();
        }

        /// <summary>Hands the current waves to the shaders (done every frame; a test that adds a wave and draws at once calls it itself).</summary>
        public void UploadWind()
        {
            _windGpu?.Upload(Wind, WindTime);
            if (_breezeGpu == null) return;
            _breezeGpu.Upload(WindTime);
            _breezeField.BakeAt(WindTime);
        }

        void ReadDebugInput()
        {
            if (Input.GetKeyDown(KeyCode.Space)) AddBeatSweep(0.5f, true);
        }

        void RunDemo(double now)
        {
            if (!DemoWaves) return;
            var age = now - _configuredAt;
            var demo = Settings.demo;
            if (!_demoSweepSent && age >= demo.startSec)
            {
                _demoSweepSent = true;
                AddBeatSweep(0.5f, true);
            }
            if (!_demoRadialSent && age >= demo.startSec)
            {
                _demoRadialSent = true;
                AddNoteRadial(new FieldPoint(-2f, demo.radialDepth), demo.radialVelocity);
                AddNoteRadial(new FieldPoint(5f, demo.radialDepth + DemoSecondRingExtraDepth), demo.radialVelocity, Settings.flowers.harpGlow);
            }
        }

        /// <summary>The ground point under a screen position (the ray meets the ground surface after a few refinements).</summary>
        public bool TryGroundUnder(Vector2 screenPosition, out FieldPoint point)
        {
            point = default;
            if (!Placement.TryRayAboveGround(Camera.ScreenPointToRay(screenPosition), 0f, out var hit)) return false;
            point = new FieldPoint(hit.x, hit.z);
            return true;
        }

        /// <summary>Switched off: every GPU buffer is given back (the wind's here, the grass', flowers', particles' and ribbons' in their own OnDisable).</summary>
        void OnDisable()
        {
            _windGpu?.Dispose();
            _windGpu = null;
            _breezeField?.Dispose();
            _breezeField = null;
            _breezeGpu = null;
        }

        /// <summary>Switched on again after having been built: built again, since the buffers were given back.</summary>
        void OnEnable()
        {
            if (Bundle != null && IsConfigured && _windGpu == null) Configure(Bundle, QualityName);
        }

        void OnDestroy()
        {
            RenderLevers.Restore(); // the pipeline assets are files in the editor: a run puts back what it changed
            _windGpu?.Dispose();
            _windGpu = null;
            _breezeField?.Dispose();
            _breezeField = null;
        }
    }
}
