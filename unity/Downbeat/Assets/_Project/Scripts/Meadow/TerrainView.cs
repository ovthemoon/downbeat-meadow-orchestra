using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The ground: a grid mesh whose heights come from <see cref="MeadowTerrain"/>, so the grass and the flowers can stand on the very same surface by asking
    /// the same function. The mesh starts behind the camera and ends past the haze, where the backdrop plate takes over.
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        const float NormalSampleCells = 1f;

        [SerializeField] MeshFilter meshFilter;
        [SerializeField] MeshRenderer meshRenderer;

        MeadowTerrainDto _settings;
        TerrainRelief _relief;
        Mesh _mesh;

        public int VertexCount => _mesh == null ? 0 : _mesh.vertexCount;

        /// <summary>Height of the ground at (x, z) in world metres.</summary>
        public float HeightAt(float x, float z) => MeadowTerrain.Height(x, z, _settings.seed, _settings.maxHeight, _relief);

        /// <summary>The rolling ground without the near undulation: where the camera stands, so that the hummocks do not lift or lower the picture.</summary>
        public float BaseHeightAt(float x, float z) => MeadowTerrain.Height(x, z, _settings.seed, _settings.maxHeight);

        public void Configure(MeadowTerrainDto terrain, MeadowCameraDto camera)
        {
            _settings = terrain;
            _relief = new TerrainRelief(terrain.reliefHeight, terrain.reliefWavelength, terrain.reliefFromZ, terrain.reliefToZ);
            var halfWidth = terrain.zMax * MeadowFrustum.HalfWidthPerDepth(camera.fovDeg) + terrain.xMargin;
            if (_mesh != null) DestroyMesh(); // built again (F5 reloads the content): the ground of the last build must not stay in memory
            _mesh = BuildMesh(halfWidth);
            meshFilter.sharedMesh = _mesh;

            var material = meshRenderer.sharedMaterial;
            material.SetVector("_GroundDark", MeadowColors.LinearVector(terrain.groundDark));
            material.SetVector("_GroundMid", MeadowColors.LinearVector(terrain.groundMid));
            material.SetVector("_GroundLight", MeadowColors.LinearVector(terrain.groundLight));
            material.SetFloat("_PatchScale", terrain.patchScale);
        }

        void OnDestroy()
        {
            if (_mesh != null) DestroyMesh();
        }

        void DestroyMesh()
        {
            meshFilter.sharedMesh = null;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        Mesh BuildMesh(float halfWidth)
        {
            var cell = _settings.cellSize;
            var columns = Mathf.CeilToInt(halfWidth * 2f / cell);
            var rows = Mathf.CeilToInt((_settings.zMax - _settings.zMin) / cell);
            var vertexColumns = columns + 1;
            var vertices = new Vector3[vertexColumns * (rows + 1)];
            var normals = new Vector3[vertices.Length];

            for (var row = 0; row <= rows; row++)
            {
                var z = _settings.zMin + row * cell;
                for (var column = 0; column <= columns; column++)
                {
                    var x = -halfWidth + column * cell;
                    var index = row * vertexColumns + column;
                    vertices[index] = new Vector3(x, HeightAt(x, z), z);
                    normals[index] = NormalAt(x, z);
                }
            }

            var triangles = new int[columns * rows * 6];
            var t = 0;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var a = row * vertexColumns + column;
                    var b = a + 1;
                    var c = a + vertexColumns;
                    var d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "MeadowGround", indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        Vector3 NormalAt(float x, float z)
        {
            var step = _settings.cellSize * NormalSampleCells;
            var dx = HeightAt(x + step, z) - HeightAt(x - step, z);
            var dz = HeightAt(x, z + step) - HeightAt(x, z - step);
            return new Vector3(-dx, 2f * step, -dz).normalized;
        }
    }
}
