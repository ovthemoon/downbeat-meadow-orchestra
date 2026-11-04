using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The flowers of the meadow: one indirect draw per species per camera, the places and sizes in a GPU buffer written once, and one number per flower (its bloom) rewritten each frame.
    /// A note opens flowers through <see cref="Bloom"/>: the bloom of each runs by <see cref="BloomTimeline"/> on the audio clock of what is HEARD (<see cref="Tick"/>), so a flower
    /// opens when its sound arrives, not when the note was scheduled.
    /// </summary>
    public sealed class FlowerField : MonoBehaviour, IBloomTarget
    {
        const int InstanceStrideBytes = 36; // 9 floats: position (3), yaw, scale, tint, phase, lean, height
        const int UintBytes = sizeof(uint);
        const float LeanMax = 0.28f;
        const float YawSpread = 0.7f; // the open heads face the viewer, a little to either side
        const float HalfOpen = 0.5f;
        const int MaxLagSamples = 4096;

        struct Active
        {
            public int Flower;
            public BloomRun Run;
            public bool WaitingForHalfOpen;
        }

        sealed class Group
        {
            public FlowerSpecies Species;
            public int Count;
            public float[] Bloom;
            public GraphicsBuffer Instances;
            public GraphicsBuffer BloomBuffer;
            public GraphicsBuffer Args;
            public Material Material;
            public Mesh Mesh;
        }

        [SerializeField] Shader flowerShader;

        readonly Group[] _groups = new Group[SpeciesMap.Count];
        readonly List<Active> _active = new List<Active>(64);
        readonly List<float> _halfOpenLagSec = new List<float>(256);
        FlowerInstance[] _flowers = new FlowerInstance[0];
        int[] _group;           // per flower: the species group it is in (the species number)
        int[] _slot;            // per flower: its index inside the group
        float[] _level;         // per flower: its bloom as of the last Tick
        int[] _activeIndex;     // per flower: its place in _active, or -1
        BloomSettings _bloomSettings;
        Bounds _bounds;
        bool _drawing;
        bool _dirty;

        /// <summary>False stops drawing the flowers without giving anything back (a capture of the meadow without them, a test).</summary>
        public bool Visible { get; set; } = true;

        public FlowerInstance[] Instances => _flowers;
        public int Count => _flowers.Length;
        public int ActiveBlooms => _active.Count;
        /// <summary>For every flower that opened: how long after the moment its sound is heard it first was half open, in seconds (negative = before). The sync probe of Task 7.</summary>
        public IReadOnlyList<float> HalfOpenLagSec => _halfOpenLagSec;

        /// <summary>Current bloom of a flower (0 bud .. 1 open) as of the last <see cref="Tick"/>.</summary>
        public float LevelOf(int flower) => _level[flower];

        public void Build(MeadowFlowersDto settings, FlowerInstance[] flowers, Func<float, float, float> groundHeight, MeadowCameraDto camera, float maxFlowerHeight)
        {
            Release();
            _flowers = flowers;
            _bloomSettings = new BloomSettings(settings.bloom);
            _group = new int[flowers.Length];
            _slot = new int[flowers.Length];
            _level = new float[flowers.Length];
            _activeIndex = new int[flowers.Length];
            for (var i = 0; i < _activeIndex.Length; i++) _activeIndex[i] = -1;
            _active.Clear();
            _halfOpenLagSec.Clear();

            var counts = new int[SpeciesMap.Count];
            for (var i = 0; i < flowers.Length; i++)
            {
                var species = (int)flowers[i].Species;
                _group[i] = species;
                _slot[i] = counts[species]++;
            }

            var instanceData = new float[SpeciesMap.Count][];
            for (var species = 0; species < SpeciesMap.Count; species++) instanceData[species] = new float[counts[species] * (InstanceStrideBytes / sizeof(float))];
            float minX = 0f, maxX = 0f, minZ = float.MaxValue, maxZ = 0f;
            for (var i = 0; i < flowers.Length; i++)
            {
                var flower = flowers[i];
                var data = instanceData[_group[i]];
                var at = _slot[i] * (InstanceStrideBytes / sizeof(float));
                var h = Hash01(i * 0.7319f + 1.3f);
                var h2 = Hash01(i * 1.913f + 7.1f);
                var h3 = Hash01(i * 3.371f + 2.9f);
                data[at + 0] = flower.X;
                data[at + 1] = groundHeight(flower.X, flower.Z) - 0.01f;
                data[at + 2] = flower.Z;
                data[at + 3] = (h - 0.5f) * 2f * YawSpread;               // yaw
                data[at + 4] = flower.Scale * Mathf.Lerp(0.88f, 1.12f, h2); // scale
                data[at + 5] = h3;                                          // tint
                data[at + 6] = h2;                                          // phase
                data[at + 7] = (h3 - 0.5f) * 2f * LeanMax;                  // lean
                data[at + 8] = flower.Height;                               // how tall the stem stands
                minX = Mathf.Min(minX, flower.X); maxX = Mathf.Max(maxX, flower.X); minZ = Mathf.Min(minZ, flower.Z); maxZ = Mathf.Max(maxZ, flower.Z);
            }
            _bounds = new Bounds(new Vector3((minX + maxX) * 0.5f, maxFlowerHeight * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(maxX - minX + 4f, maxFlowerHeight + 4f, maxZ - minZ + 4f));

            for (var species = 0; species < SpeciesMap.Count; species++)
            {
                var look = LookOf(settings, (FlowerSpecies)species);
                var group = new Group
                {
                    Species = (FlowerSpecies)species,
                    Count = counts[species],
                    Bloom = new float[Mathf.Max(1, counts[species])],
                    Mesh = FlowerMeshFactory.Create((FlowerSpecies)species, look),
                    Material = new Material(flowerShader) { name = "Flower " + (FlowerSpecies)species },
                };
                var material = group.Material;
                material.SetVector("_BudColor", MeadowColors.LinearVector(look.budColor));
                material.SetVector("_OpenColor", MeadowColors.LinearVector(look.openColor));
                material.SetVector("_CenterColor", MeadowColors.LinearVector(look.centerColor));
                material.SetVector("_StemColor", MeadowColors.LinearVector(settings.stemColor));
                material.SetVector("_FlowerParams", new Vector4(look.stemHeight, species, settings.budGlow, settings.openGlow));
                material.SetVector("_FlowerLight", new Vector4(settings.ambientMix, settings.sunBody, settings.translucency, 0f));
                if (group.Count > 0)
                {
                    group.Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, group.Count, InstanceStrideBytes);
                    group.Instances.SetData(instanceData[species]);
                    group.BloomBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, group.Count, sizeof(float));
                    group.BloomBuffer.SetData(group.Bloom);
                    group.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                    args[0].indexCountPerInstance = group.Mesh.GetIndexCount(0);
                    args[0].instanceCount = (uint)group.Count;
                    group.Args.SetData(args);
                    material.SetBuffer("_Flowers", group.Instances);
                    material.SetBuffer("_Bloom", group.BloomBuffer);
                }
                _groups[species] = group;
            }
            _drawing = flowers.Length > 0;
            _dirty = false;
        }

        static FlowerLookDto LookOf(MeadowFlowersDto settings, FlowerSpecies species)
        {
            switch (species)
            {
                case FlowerSpecies.Bellflower: return settings.bellflower;
                case FlowerSpecies.Dandelion: return settings.dandelion;
                default: return settings.poppy;
            }
        }

        /// <summary>The bare spots the grass leaves around the flowers: x, z and the radius.</summary>
        public List<Vector3> ClearingsFor(float radius)
        {
            var list = new List<Vector3>(_flowers.Length);
            foreach (var flower in _flowers) list.Add(new Vector3(flower.X, flower.Z, radius));
            return list;
        }

        /// <summary>Opens the flowers of <paramref name="flowerIndices"/> for a note heard at <paramref name="audibleTime"/> (audio seconds). A flower that is still open rises again from where it is.</summary>
        public void Bloom(IReadOnlyList<int> flowerIndices, double audibleTime)
        {
            for (var i = 0; i < flowerIndices.Count; i++)
            {
                var flower = flowerIndices[i];
                if (_activeIndex[flower] >= 0)
                {
                    // struck again while still blooming: the old bloom goes on until the new one starts (no freeze), and the new one rises from there
                    var existing = _active[_activeIndex[flower]];
                    existing.Run = existing.Run.Retrigger(audibleTime, _bloomSettings);
                    existing.WaitingForHalfOpen = _level[flower] < HalfOpen;
                    _active[_activeIndex[flower]] = existing;
                    continue;
                }
                _activeIndex[flower] = _active.Count;
                _active.Add(new Active { Flower = flower, Run = BloomRun.First(audibleTime, _level[flower]), WaitingForHalfOpen = _level[flower] < HalfOpen });
            }
        }

        /// <summary>
        /// Takes back the blooms that were asked for for notes heard after <paramref name="audibleAfter"/> (the sound of those notes was cancelled: the piece was ended before they were heard). A first bloom is dropped,
        /// and one that came on top of an earlier one (the flower was struck again) goes back to the earlier. Returns how many flowers were touched.
        /// </summary>
        public int CancelBloomsAfter(double audibleAfter)
        {
            var touched = 0;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var active = _active[i];
                if (!(active.Run.AudibleTime > audibleAfter)) continue;
                touched++;
                var run = active.Run;
                while (run.AudibleTime > audibleAfter && run.HasEarlier) run = run.Earlier();
                if (run.AudibleTime > audibleAfter)
                {
                    RemoveActive(i);   // it never started: the flower stays as it is
                    continue;
                }
                active.Run = run;
                active.WaitingForHalfOpen = _level[active.Flower] < HalfOpen;
                _active[i] = active;
            }
            return touched;
        }

        /// <summary>The blooms that were asked for but whose note is not heard yet at <paramref name="heardNow"/>: the flower and the audio time its sound arrives. A rebuild takes these along (see <see cref="Bloom"/>).</summary>
        public List<(int flower, double audibleTime)> BloomsNotYetHeard(double heardNow)
        {
            var waiting = new List<(int, double)>();
            foreach (var active in _active)
            {
                if (active.Run.AudibleTime > heardNow) waiting.Add((active.Flower, active.Run.AudibleTime));
            }
            return waiting;
        }

        /// <summary>Asks again for blooms that <see cref="BloomsNotYetHeard"/> returned before a rebuild (flowers that the new build does not have are left out).</summary>
        public void BloomAgain(IReadOnlyList<(int flower, double audibleTime)> blooms)
        {
            var one = new int[1];
            foreach (var (flower, audibleTime) in blooms)
            {
                if (flower >= Count) continue;
                one[0] = flower;
                Bloom(one, audibleTime);
            }
        }

        /// <summary>Moves every running bloom to the audio time of the sample that is leaving the speakers now and writes the numbers to the GPU.</summary>
        public void Tick(double heardNow)
        {
            if (!_drawing || (_active.Count == 0 && !_dirty)) return;
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var active = _active[i];
                var value = active.Run.Value(heardNow, _bloomSettings);
                var settled = active.Run.IsSettled(heardNow, _bloomSettings);
                if (settled) value = _bloomSettings.RestLevel;
                if (active.WaitingForHalfOpen && value >= HalfOpen)
                {
                    active.WaitingForHalfOpen = false;
                    _active[i] = active;
                    if (_halfOpenLagSec.Count < MaxLagSamples) _halfOpenLagSec.Add((float)(heardNow - active.Run.AudibleTime));
                }
                _level[active.Flower] = value;
                _groups[_group[active.Flower]].Bloom[_slot[active.Flower]] = value;
                if (settled) RemoveActive(i);
            }
            foreach (var group in _groups)
            {
                if (group != null && group.Count > 0) group.BloomBuffer.SetData(group.Bloom);
            }
            _dirty = _active.Count > 0;
        }

        void RemoveActive(int index)
        {
            var last = _active.Count - 1;
            _activeIndex[_active[index].Flower] = -1;
            if (index != last)
            {
                _active[index] = _active[last];
                _activeIndex[_active[index].Flower] = index;
            }
            _active.RemoveAt(last);
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Release();
        }

        void OnDestroy() => Release();

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_drawing || !Visible || camera.cameraType != CameraType.Game) return;
            foreach (var group in _groups)
            {
                if (group == null || group.Count == 0) continue;
                var parameters = new RenderParams(group.Material)
                {
                    worldBounds = _bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    camera = camera,
                };
                Graphics.RenderMeshIndirect(parameters, group.Mesh, group.Args);
            }
        }

        void Release()
        {
            _drawing = false;
            for (var i = 0; i < _groups.Length; i++)
            {
                var group = _groups[i];
                if (group == null) continue;
                group.Instances?.Release();
                group.BloomBuffer?.Release();
                group.Args?.Release();
                if (group.Material != null) Destroy(group.Material);
                if (group.Mesh != null) Destroy(group.Mesh);
                _groups[i] = null;
            }
        }

        static float Hash01(float x)
        {
            var s = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }
    }
}
