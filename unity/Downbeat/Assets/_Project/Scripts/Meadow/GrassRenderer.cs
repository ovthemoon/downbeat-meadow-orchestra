using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The grass. A compute shader places the blades of each depth band once (on the ground of <see cref="MeadowTerrain"/>, leaving flower spots bare) into a GPU
    /// buffer; every camera that draws the meadow gets one indirect draw per band. Nothing is copied back to the CPU while the app runs. The density comes from the quality tier;
    /// <see cref="Build"/> again with another density (or other flower spots) replaces the grass.
    /// </summary>
    public sealed class GrassRenderer : MonoBehaviour
    {
        const float FieldMapCell = 0.25f;
        Vector2 _mapMin;
        Vector2 _mapSize;
        ushort[] _mapTexels;     // a copy of the baked heights (the texture is not readable on the CPU after it is uploaded)
        int _mapWidth;
        int _mapHeight;
        public static readonly int TipRimId = Shader.PropertyToID("_MeadowTipRim");
        const int ThreadGroup = 8;
        const int BladeStrideBytes = 52; // 13 floats: position, yaw, height, width, tint, phase, ground shade, hue, lean (x, z)
        const int UintBytes = sizeof(uint);
        const float BoundsHeight = 4f;

        sealed class Band
        {
            public GraphicsBuffer Blades;
            public GraphicsBuffer Args;
            public Material Material;
            public Mesh Mesh;
        }

        [SerializeField] ComputeShader placement;
        [SerializeField] Shader grassShader;

        readonly List<Band> _bands = new List<Band>();
        Texture2D _fieldMap;
        GraphicsBuffer _clearings;
        Bounds _bounds;
        bool _drawing;
        long _bladeCount;

        /// <summary>Blades placed by the last <see cref="Build"/> (all bands).</summary>
        public long BladeCount => _bladeCount;

        public void Build(MeadowGrassDto grass, MeadowQualityDto quality, MeadowTerrainDto terrain, MeadowCameraDto camera, float cameraY, TerrainView ground, IReadOnlyList<Vector3> clearings = null)
        {
            Release();
            Shader.SetGlobalVector("_MeadowGrassFade", new Vector4(grass.fadeFromRow, grass.fadeToRow, grass.fadeMinHeight, 0f)); // the flowers shrink into the plate at the same rows
            // the flowers are dimmed with depth the same way as the grass
            Shader.SetGlobalVector("_MeadowGrassDim", new Vector4(grass.farDim, grass.farDimFrom, grass.farDimTo, grass.nearDarkness));
            Shader.SetGlobalVector("_MeadowGrassDim2", new Vector4(grass.nearDarkStart, grass.nearDarkEnd, grass.sunColumn, 0f));
            var halfWidthPerDepth = MeadowFrustum.HalfWidthPerDepth(camera.fovDeg);
            var zEnd = grass.rings[grass.rings.Length - 1].zTo;
            var halfWidthAtEnd = zEnd * halfWidthPerDepth + terrain.xMargin;
            _mapMin = new Vector2(-halfWidthAtEnd, terrain.zMin);
            _mapSize = new Vector2(halfWidthAtEnd * 2f, zEnd + 1f - terrain.zMin);
            _fieldMap = BakeFieldMap(ground, terrain.seed, grass.hummockScale, -halfWidthAtEnd, terrain.zMin, halfWidthAtEnd * 2f, zEnd + 1f - terrain.zMin);
            _clearings = CreateClearings(clearings, out var clearingCount);

            var rootColor = MeadowColors.LinearVector(grass.rootColor);
            var midColor = MeadowColors.LinearVector(grass.midColor);
            var tipColor = MeadowColors.LinearVector(grass.tipColor);
            var yellowColor = MeadowColors.LinearVector(grass.yellowColor);
            var driedColor = MeadowColors.LinearVector(grass.driedColor);
            var backlightColor = MeadowColors.LinearVector(grass.backlightColor);
            var glowColor = MeadowColors.LinearVector(grass.glowColor);
            var kernel = placement.FindKernel("Place");

            _bladeCount = 0;
            var topOfBounds = terrain.maxHeight + grass.heightMax + BoundsHeight;
            _bounds = new Bounds(new Vector3(0f, topOfBounds * 0.5f, (terrain.zMin + zEnd) * 0.5f), new Vector3(halfWidthAtEnd * 2f + 20f, topOfBounds + 20f, zEnd - terrain.zMin + 20f));
            for (var i = 0; i < grass.rings.Length; i++)
            {
                var ring = grass.rings[i];
                var cell = 1f / Mathf.Sqrt(quality.grassPerSqm * ring.densityScale);
                var halfWidthMax = ring.zTo * halfWidthPerDepth + terrain.xMargin;
                var columns = Mathf.CeilToInt(halfWidthMax * 2f / cell);
                var rows = Mathf.CeilToInt((ring.zTo - ring.zFrom) / cell);

                var band = new Band
                {
                    Blades = new GraphicsBuffer(GraphicsBuffer.Target.Append, columns * rows, BladeStrideBytes),
                    Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size),
                    Mesh = BladeMeshFactory.Create(Mathf.Min(ring.segments, quality.grassMaxSegments)),
                    Material = new Material(grassShader) { name = $"Grass band {i}" },
                };
                band.Blades.SetCounterValue(0);

                placement.SetBuffer(kernel, "_Out", band.Blades);
                placement.SetBuffer(kernel, "_Clearings", _clearings);
                placement.SetTexture(kernel, "_Fields", _fieldMap);
                placement.SetVector("_Band", new Vector4(ring.zFrom, ring.zTo, cell, i * 17.31f));
                placement.SetVector("_Grid", new Vector4(columns, rows, halfWidthPerDepth, terrain.xMargin));
                placement.SetVector("_FieldRect", new Vector4(-halfWidthAtEnd, terrain.zMin, halfWidthAtEnd * 2f, zEnd + 1f - terrain.zMin));
                placement.SetVector("_GrassA", new Vector4(grass.heightMin, grass.heightMax, grass.width, grass.clumpScale));
                placement.SetVector("_GrassB", new Vector4(grass.clumpLean, clearingCount, grass.relief, grass.driedShare));
                placement.SetVector("_GrassC", new Vector4(grass.fadeFromRow, grass.fadeToRow, grass.fadeMinHeight, grass.hummockHeight));
                placement.SetVector("_Camera", new Vector4(cameraY, -camera.pitchDeg * Mathf.Deg2Rad, Mathf.Tan(camera.fovDeg * 0.5f * Mathf.Deg2Rad), 0f));
                placement.SetVector("_GrassD", new Vector4(grass.hummockDensity, grass.hummockDark, grass.lean, ring.heightScale));
                placement.Dispatch(kernel, Mathf.CeilToInt(columns / (float)ThreadGroup), Mathf.CeilToInt(rows / (float)ThreadGroup), 1);

                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                args[0].indexCountPerInstance = band.Mesh.GetIndexCount(0);
                band.Args.SetData(args);
                GraphicsBuffer.CopyCount(band.Blades, band.Args, UintBytes); // the number of blades placed becomes the instance count

                var material = band.Material;
                material.SetFloat("_WidthScale", ring.widthScale);
                material.SetVector("_RootColor", rootColor);
                material.SetVector("_MidColor", midColor);
                material.SetVector("_TipColor", tipColor);
                material.SetVector("_BacklightColor", backlightColor);
                material.SetVector("_GlowColor", glowColor);
                material.SetVector("_YellowColor", yellowColor);
                material.SetVector("_DriedColor", driedColor);
                material.SetFloat("_RingDark", ring.darkness);
                material.SetVector("_GrassParams", new Vector4(grass.backlight, grass.rim, grass.sparkle, grass.tipGlow));
                Shader.SetGlobalFloat(TipRimId, grass.tipRim);
                material.SetVector("_GrassParams2", new Vector4(grass.sunBody, grass.ambientMix, grass.sunColumn, 0f));
                material.SetVector("_GrassParams3", new Vector4(grass.nearDarkness, grass.nearDarkEnd, grass.nearDarkStart, 0f));
                material.SetVector("_GrassParams4", new Vector4(grass.farDim, grass.farDimFrom, grass.farDimTo, 0f));
                material.SetBuffer("_Blades", band.Blades);
                _bands.Add(band);

                var counts = new uint[5];
                band.Args.GetData(counts); // once, at start-up: how many blades this band holds
                _bladeCount += counts[1];
            }
            _drawing = true;
        }

        /// <summary>Blades in band <paramref name="index"/> (reads the GPU counter back; for logs and tests, not for the frame loop).</summary>
        public int BladesInBand(int index)
        {
            var counts = new uint[5];
            _bands[index].Args.GetData(counts);
            return (int)counts[1];
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Release(); // switched off: the GPU buffers are given back (MeadowStage builds the grass again when it is switched on)
        }

        void OnDestroy() => Release();

        /// <summary>One indirect draw per band for the camera that is about to render (also for an off-screen capture camera).</summary>
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_drawing || camera.cameraType != CameraType.Game) return;
            for (var i = 0; i < _bands.Count; i++)
            {
                var band = _bands[i];
                var parameters = new RenderParams(band.Material)
                {
                    worldBounds = _bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    camera = camera,
                };
                Graphics.RenderMeshIndirect(parameters, band.Mesh, band.Args);
            }
        }

        /// <summary>The ground height the blades are planted at, read from the baked map the placement uses (bilinear, as the shader reads it); for tests of the planting.</summary>
        public float PlantedHeightAt(float x, float z)
        {
            if (_mapTexels == null) return float.NaN;
            var u = Mathf.Clamp((x - _mapMin.x) / _mapSize.x * _mapWidth - 0.5f, 0f, _mapWidth - 1f);
            var v = Mathf.Clamp((z - _mapMin.y) / _mapSize.y * _mapHeight - 0.5f, 0f, _mapHeight - 1f);
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            int x1 = Mathf.Min(x0 + 1, _mapWidth - 1), y1 = Mathf.Min(y0 + 1, _mapHeight - 1);
            float fx = u - x0, fy = v - y0;
            float H(int column, int row) => Mathf.HalfToFloat(_mapTexels[(row * _mapWidth + column) * 2]);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x1, y0), fx), Mathf.Lerp(H(x0, y1), H(x1, y1), fx), fy);
        }

        /// <summary>Two-channel half-float map of the ground: red the height of <see cref="MeadowTerrain"/>, green the hummock field of <see cref="MeadowFields"/>.</summary>
        Texture2D BakeFieldMap(TerrainView ground, int seed, float hummockScale, float minX, float minZ, float sizeX, float sizeZ)
        {
            var width = Mathf.CeilToInt(sizeX / FieldMapCell);
            var height = Mathf.CeilToInt(sizeZ / FieldMapCell);
            var texels = new ushort[width * height * 2];
            for (var row = 0; row < height; row++)
            {
                var z = minZ + (row + 0.5f) * (sizeZ / height);
                for (var column = 0; column < width; column++)
                {
                    var x = minX + (column + 0.5f) * (sizeX / width);
                    var index = (row * width + column) * 2;
                    texels[index] = Mathf.FloatToHalf(ground.HeightAt(x, z));
                    texels[index + 1] = Mathf.FloatToHalf(MeadowFields.Hummock(x, z, seed, hummockScale));
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGHalf, false, true) { name = "GrassFieldMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _mapTexels = texels;
            _mapWidth = width;
            _mapHeight = height;
            texture.SetPixelData(texels, 0);
            texture.Apply(false, true);
            return texture;
        }

        static GraphicsBuffer CreateClearings(IReadOnlyList<Vector3> clearings, out int count)
        {
            count = clearings == null ? 0 : clearings.Count;
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, count), sizeof(float) * 4);
            var data = new Vector4[Mathf.Max(1, count)];
            for (var i = 0; i < count; i++) data[i] = new Vector4(clearings[i].x, clearings[i].y, clearings[i].z, 0f); // x, z, radius
            buffer.SetData(data);
            return buffer;
        }

        void Release()
        {
            _drawing = false;
            foreach (var band in _bands)
            {
                band.Blades.Release();
                band.Args.Release();
                if (band.Material != null) Destroy(band.Material);
                if (band.Mesh != null) Destroy(band.Mesh);
            }
            _bands.Clear();
            _clearings?.Release();
            _clearings = null;
            if (_fieldMap != null) Destroy(_fieldMap);
            _fieldMap = null;
        }
    }
}
