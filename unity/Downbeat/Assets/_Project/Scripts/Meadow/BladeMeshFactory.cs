using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The ribbon every grass blade is an instance of: <c>segments</c> quads along the blade and a pointed tip. A vertex is (side -1 or 1, height along the blade 0..1, 0);
    /// the grass shader turns that into a tapered, bent blade at the instance's place. The mesh's own bounds are meaningless (instances are far away).
    /// </summary>
    public static class BladeMeshFactory
    {
        public static Mesh Create(int segments)
        {
            if (segments < 1) throw new System.ArgumentOutOfRangeException(nameof(segments));
            var vertices = new Vector3[segments * 2 + 1];
            for (var level = 0; level < segments; level++)
            {
                var t = (float)level / segments;
                vertices[level * 2] = new Vector3(-1f, t, 0f);
                vertices[level * 2 + 1] = new Vector3(1f, t, 0f);
            }
            var tip = segments * 2;
            vertices[tip] = new Vector3(0f, 1f, 0f);

            var triangles = new int[(segments - 1) * 6 + 3];
            var next = 0;
            for (var level = 0; level < segments - 1; level++)
            {
                var left = level * 2;
                var right = left + 1;
                var upperLeft = left + 2;
                var upperRight = left + 3;
                triangles[next++] = left; triangles[next++] = upperLeft; triangles[next++] = right;
                triangles[next++] = right; triangles[next++] = upperLeft; triangles[next++] = upperRight;
            }
            var lastLeft = (segments - 1) * 2;
            triangles[next++] = lastLeft; triangles[next++] = tip; triangles[next++] = lastLeft + 1;

            var mesh = new Mesh { name = $"GrassBlade{segments}" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;
        }
    }
}
