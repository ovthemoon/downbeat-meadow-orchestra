using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The three flowers, built in code. One mesh per species holds two poses of the same vertices: the open flower (the vertex positions) and the closed bud (uv1); the flower
    /// shader blends them by the bloom of each instance. Streams: position = open pose, uv1 = closed position, uv2 = open normal, uv3 = closed normal, colour = (gradient
    /// from base 0 to tip 1, region 0 stem / 0.5 petal / 1 heart, occlusion, 1). The mesh stands on y = 0 with its head at the stem height of the content; the stem
    /// is the same in both poses.
    /// </summary>
    public static class FlowerMeshFactory
    {
        public const float StemRegion = 0f;
        public const float SepalRegion = 0.25f;
        public const float PetalRegion = 0.5f;
        public const float HeartRegion = 1f;

        const int StemRings = 6;
        const int StemSides = 3;
        const float StemBaseRadius = 0.0034f;
        const float StemTopRadius = 0.0018f;
        /// <summary>How far an open poppy turns its cup towards the viewer (degrees): it faces the people who look at the meadow.</summary>
        const float PoppyFaceTiltDeg = -52f;

        public static Mesh Create(FlowerSpecies species, FlowerLookDto look)
        {
            var builder = new Builder();
            Vector3 top;
            switch (species)
            {
                case FlowerSpecies.Bellflower:
                    top = AddStem(builder, look.stemHeight, 0.085f, -0.035f, 0.09f);   // a nodding stem: bowed sideways, arching towards the viewer, drooping at the top
                    AddBellflower(builder, top, look.headSize);
                    break;
                case FlowerSpecies.Dandelion:
                    top = AddStem(builder, look.stemHeight, 0.05f, 0f, 0f);
                    AddDandelion(builder, top, look.headSize);
                    break;
                case FlowerSpecies.Poppy:
                    top = AddStem(builder, look.stemHeight, 0.06f, -0.02f, 0.03f);
                    AddPoppy(builder, top, look.headSize);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(species));
            }
            return builder.ToMesh($"Flower{species}");
        }

        // ---- the parts ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A thin, tapered stem that bows sideways (<paramref name="bow"/> metres at the top), arches towards the viewer (<paramref name="arch"/>) and droops a little at the
        /// top (<paramref name="droop"/>, a fraction of the height); returns the point where the head starts.
        /// </summary>
        static Vector3 AddStem(Builder b, float height, float bow, float arch, float droop)
        {
            var first = b.Count;
            Vector3 Path(float t) => new Vector3(bow * t * t, height * (t - droop * t * t * t * t) / (1f - droop), arch * t * t * t);
            for (var ring = 0; ring < StemRings; ring++)
            {
                var t = (float)ring / (StemRings - 1);
                var centre = Path(t);
                var radius = Mathf.Lerp(StemBaseRadius, StemTopRadius, t);
                for (var side = 0; side < StemSides; side++)
                {
                    var angle = side * Mathf.PI * 2f / StemSides + 0.5f;
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    b.Add(centre + offset * radius, centre + offset * radius, offset, offset, new Color(t, StemRegion, Mathf.Lerp(0.3f, 1f, t), 0f));
                }
            }
            for (var ring = 0; ring < StemRings - 1; ring++)
            {
                for (var side = 0; side < StemSides; side++)
                {
                    var a = first + ring * StemSides + side;
                    var c = first + ring * StemSides + (side + 1) % StemSides;
                    b.Quad(a, c, a + StemSides, c + StemSides);
                }
            }
            return Path(1f);
        }

        /// <summary>
        /// Narrow green leaves round the foot of the head. Closed they hug the bud up to <paramref name="coverage"/> of its length (so only the tip of the bud shows colour); open
        /// they fold back and down. <paramref name="budRadius"/> and <paramref name="budLength"/> are those of the closed bud.
        /// </summary>
        static void AddSepals(Builder b, Vector3 top, int count, float budLength, float budRadius, float coverage, float openOut, float openDrop, float width)
        {
            const int rings = 3;
            for (var leaf = 0; leaf < count; leaf++)
            {
                var azimuth = (leaf + 0.5f) * Mathf.PI * 2f / count;
                var first = b.Count;
                var dir = new Vector3(Mathf.Cos(azimuth), 0f, Mathf.Sin(azimuth));
                var side = new Vector3(-Mathf.Sin(azimuth), 0f, Mathf.Cos(azimuth));
                for (var ring = 0; ring < rings; ring++)
                {
                    var s = (float)ring / (rings - 1);
                    var shape = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.1f + 0.8f * s * coverage)), 0.85f);
                    var closedCentre = top + dir * (budRadius * shape * 1.12f + 0.0008f) + Vector3.up * (budLength * s * coverage);
                    var openCentre = top + dir * (0.003f + openOut * s) + Vector3.down * (openDrop * s);
                    var half = width * (1f - 0.55f * s);
                    for (var across = -1; across <= 1; across += 2)
                    {
                        b.Add(openCentre + side * (across * half), closedCentre + side * (across * half * 0.8f), Vector3.zero, Vector3.zero, new Color(s, SepalRegion, Mathf.Lerp(0.55f, 1f, s), across * 0.5f + 0.5f));
                    }
                }
                for (var ring = 0; ring < rings - 1; ring++)
                {
                    var a = first + ring * 2;
                    b.Quad(a, a + 1, a + 2, a + 3);
                }
            }
        }

        /// <summary>
        /// Five petals that make a bell: a bud that points up and a bell that hangs down from the top of the stem, narrow at the neck, belled out, with a flared lip of five lobes.
        /// A petal is a strip of rings along its length (s from the stem to the lip) with three vertices across (the lobe is the middle one).
        /// </summary>
        static void AddBellflower(Builder b, Vector3 top, float size)
        {
            const int petals = 5;
            const int rings = 5;
            var length = size * 1.15f;
            var rim = size * 0.4f;
            var halfAngle = Mathf.PI / petals * 0.98f;
            var budLength = size * 0.45f;
            var budRadius = size * 0.1f;
            for (var petal = 0; petal < petals; petal++)
            {
                var azimuth = petal * Mathf.PI * 2f / petals;
                var first = b.Count;
                for (var ring = 0; ring < rings; ring++)
                {
                    var s = (float)ring / (rings - 1);
                    for (var across = -1; across <= 1; across++)
                    {
                        var phi = azimuth + across * halfAngle;
                        var lobe = across == 0;
                        // open: hangs down; narrow neck, a belly, and a lip that flares out; between two lobes the lip is notched (pulled in and up)
                        var flare = 0.12f + 0.55f * Mathf.Pow(s, 0.8f) + 0.33f * s * s * s * s + 0.3f * Mathf.Pow(s, 8f);
                        var rOpen = rim * flare * (lobe ? 1.1f : Mathf.Lerp(1f, 0.74f, s * s * s));
                        var yOpen = -length * s * (1f - 0.12f * s) + (lobe ? 0f : length * 0.16f * s * s * s);
                        // closed: a slim, elongated bud pointing up
                        var rClosed = budRadius * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.08f + 0.9f * s)), 0.85f) * (lobe ? 1.06f : 1f);
                        var yClosed = budLength * s;
                        var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                        b.Add(top + dir * rOpen + Vector3.up * yOpen, top + dir * rClosed + Vector3.up * yClosed, Vector3.zero, Vector3.zero, new Color(s, PetalRegion, Mathf.Lerp(0.5f, 1f, s), (across + 1) * 0.5f));
                    }
                }
                for (var ring = 0; ring < rings - 1; ring++)
                {
                    for (var across = 0; across < 2; across++)
                    {
                        var a = first + ring * 3 + across;
                        b.Quad(a, a + 1, a + 3, a + 4);
                    }
                }
            }
            AddSepals(b, top, 5, budLength, budRadius, 0.6f, size * 0.12f, size * 0.1f, size * 0.07f);
        }

        /// <summary>
        /// A bud of folded seed filaments that opens into a soft sphere of filaments. Each filament is a thin ribbon from a small core to the rim of the puff, fading towards its end (the
        /// shader dissolves it with dither, so the edge is feathery); closed, they gather into a pointed tuft.
        /// </summary>
        static void AddDandelion(Builder b, Vector3 top, float size)
        {
            const int filaments = 72;
            const int rings = 3;
            var radius = size * 0.5f;
            var centreOpen = top + Vector3.up * radius * 0.95f;
            var budLength = size * 0.42f;
            var budRadius = size * 0.085f;
            var golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (var i = 0; i < filaments; i++)
            {
                var y = 1f - 2f * (i + 0.5f) / filaments;
                var ringRadius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var phi = i * golden;
                var dir = new Vector3(Mathf.Cos(phi) * ringRadius, y, Mathf.Sin(phi) * ringRadius);
                var reach = 0.82f + 0.18f * Hash(i, 3);
                var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var closedDir = (dir * 0.16f + Vector3.up).normalized;
                var closedReach = budLength * (0.55f + 0.45f * (0.5f + 0.5f * Hash(i, 7)));
                var first = b.Count;
                for (var ring = 0; ring < rings; ring++)
                {
                    var s = (float)ring / (rings - 1);
                    var open = centreOpen + dir * (radius * (0.16f + 0.84f * s) * reach);
                    var closed = top + Vector3.up * (budLength * 0.08f) + closedDir * (closedReach * s) + new Vector3(closedDir.z, 0f, -closedDir.x) * 0f;
                    var half = radius * Mathf.Lerp(0.1f, 0.05f, s);
                    var closedHalf = budRadius * Mathf.Lerp(0.55f, 0.1f, s) * 0.5f;
                    for (var across = -1; across <= 1; across += 2)
                    {
                        b.Add(open + side * (across * half), closed + side * (across * closedHalf), dir, closedDir, new Color(s, PetalRegion, Mathf.Lerp(0.6f, 1f, s), 1f - s * s));
                    }
                }
                for (var ring = 0; ring < rings - 1; ring++)
                {
                    var a = first + ring * 2;
                    b.Quad(a, a + 1, a + 2, a + 3);
                }
            }

            // the core of the puff: a small green-white ball the filaments grow from (the seed head)
            const int latitudes = 5;
            const int longitudes = 7;
            var core = b.Count;
            for (var lat = 0; lat <= latitudes; lat++)
            {
                var theta = Mathf.PI * lat / latitudes;
                for (var lon = 0; lon <= longitudes; lon++)
                {
                    var phi = Mathf.PI * 2f * lon / longitudes;
                    var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    b.Add(centreOpen + dir * radius * 0.2f, top + Vector3.up * budLength * 0.12f + new Vector3(dir.x * budRadius * 0.8f, dir.y * budLength * 0.12f, dir.z * budRadius * 0.8f), dir, dir, new Color(0f, PetalRegion, 0.6f, 1f));
                }
            }
            for (var lat = 0; lat < latitudes; lat++)
            {
                for (var lon = 0; lon < longitudes; lon++)
                {
                    var a = core + lat * (longitudes + 1) + lon;
                    b.Quad(a, a + 1, a + longitudes + 1, a + longitudes + 2);
                }
            }
            AddSepals(b, top, 7, budLength, budRadius, 0.55f, size * 0.2f, size * 0.12f, size * 0.05f);
        }

        /// <summary>The four petals of a poppy; one mesh, one draw (see <see cref="AddPoppy"/>).</summary>
        public const int PoppyPetals = 4;
        public const int PoppyRings = 7;
        public const int PoppyWidthSteps = 7;
        /// <summary>The turn that makes the open cup face the viewer; the head of the poppy is built upright and turned by this.</summary>
        public static readonly Quaternion PoppyFace = Quaternion.AngleAxis(PoppyFaceTiltDeg, Vector3.right);
        /// <summary>How far a petal reaches round the axis, as a multiple of its own quarter: more than 1 makes it lie over its neighbours.</summary>
        const float PoppyPetalOverlap = 1.4f;

        /// <summary>
        /// Four broad petals that make a cup: each wraps round the axis further than its own quarter, so that it lies over its neighbours, and the two outer and the two inner petals sit at slightly
        /// different distances (so overlapping surfaces do not cut through each other). The rim rolls back and out a little, more at the corners than in the middle. A closed egg-shaped bud
        /// opens into it; a dark heart with a ring where the stamens are, and two green sepals. The petals are darker at the base and in the shade of the outer ones.
        /// </summary>
        static void AddPoppy(Builder b, Vector3 top, float size)
        {
            var rOpen = size * 0.55f;
            var budLength = size * 0.42f;
            var budRadius = size * 0.12f;
            var face = PoppyFace;
            var halfSpan = Mathf.PI / PoppyPetals * PoppyPetalOverlap;
            for (var petal = 0; petal < PoppyPetals; petal++)
            {
                var azimuth = Mathf.PI * 0.25f + petal * Mathf.PI * 2f / PoppyPetals;
                var outer = (petal & 1) == 0;                                  // the two outer petals are lower and flare more, the two inner ones stand higher and are deeper in the cup
                var layer = outer ? 1f : -1f;
                var first = b.Count;
                for (var ring = 0; ring < PoppyRings; ring++)
                {
                    var s = (float)ring / (PoppyRings - 1);
                    var tilt = Mathf.Lerp(0.22f, 0.85f, Mathf.Pow(s, 1.1f));        // from steep to flared, in radians from the vertical: a cup
                    var reach = rOpen * s * 1.15f;
                    var radius = reach * Mathf.Sin(tilt) + size * 0.04f + layer * size * 0.018f * s;
                    var lift = reach * Mathf.Cos(tilt) * 0.95f - layer * size * 0.012f * s;
                    var widthProfile = 0.3f + 0.7f * Mathf.Sqrt(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.8f)));   // narrow at the base, broad towards the rim: a petal is a fan with a rounded edge
                    var rim = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.65f) / 0.35f));
                    for (var step = 0; step < PoppyWidthSteps; step++)
                    {
                        var w = step / (float)(PoppyWidthSteps - 1) * 2f - 1f;
                        var edge = Mathf.Pow(Mathf.Abs(w), 1.5f);
                        var curl = size * 0.10f * rim * rim * (0.35f + 0.65f * edge);   // the edge rolls back and out, the corners more than the middle
                        var ripple = size * 0.016f * Mathf.Sin((w * 1.6f + petal) * Mathf.PI) * s * s;   // tissue paper is not smooth
                        var scallop = 1f - 0.1f * rim * Mathf.Pow(Mathf.Abs(w), 2f);   // the corners of the rim come a little short: the edge of a petal is round
                        var r = (radius + curl * 0.6f) * scallop;
                        var h = lift - curl * 0.5f + ripple;
                        var phi = azimuth + w * halfSpan * widthProfile;
                        var open = top + face * (new Vector3(Mathf.Cos(phi) * r, h, Mathf.Sin(phi) * r));
                        // closed: the four petals wrap an egg
                        var wrapAngle = azimuth + w * Mathf.PI * 0.25f;
                        var eggRadius = budRadius * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.06f + 0.88f * s)), 0.85f);
                        var closed = top + new Vector3(Mathf.Cos(wrapAngle) * eggRadius, budLength * s, Mathf.Sin(wrapAngle) * eggRadius);
                        var shade = Mathf.Lerp(0.3f, 1f, Mathf.Pow(s, 0.8f)) * (outer ? 1f : 0.88f);   // darker at the base, and in the shade of the outer petals
                        b.Add(open, closed, Vector3.zero, Vector3.zero, new Color(s, PetalRegion, shade, (w + 1f) * 0.5f));
                    }
                }
                for (var ring = 0; ring < PoppyRings - 1; ring++)
                {
                    for (var step = 0; step < PoppyWidthSteps - 1; step++)
                    {
                        var a = first + ring * PoppyWidthSteps + step;
                        b.Quad(a, a + 1, a + PoppyWidthSteps, a + PoppyWidthSteps + 1);
                    }
                }
            }

            // the heart: a dark disc with a ring where the stamens stand (colour.r = radius 0..1 across the disc, colour.a = the angle round it 0..1; the seam vertices are doubled)
            const int spokes = 12;
            var centre = b.Count;
            b.Add(top + face * (Vector3.up * size * 0.09f), top + Vector3.up * budLength * 0.3f, face * Vector3.up, Vector3.up, new Color(0f, HeartRegion, 1f, 0f));
            for (var layer = 1; layer <= 2; layer++)
            {
                var r = layer * 0.5f;
                for (var i = 0; i <= spokes; i++)
                {
                    var angle = i * Mathf.PI * 2f / spokes;
                    var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var lift = size * (0.09f - 0.025f * layer);
                    b.Add(top + face * (dir * size * 0.2f * r + Vector3.up * lift), top + dir * budRadius * 0.3f * r + Vector3.up * budLength * 0.28f, face * Vector3.up, Vector3.up, new Color(r, HeartRegion, 1f, i / (float)spokes));
                }
            }
            for (var i = 0; i < spokes; i++) b.Triangle(centre, centre + 1 + i, centre + 2 + i);
            for (var i = 0; i < spokes; i++)
            {
                var inner = centre + 1 + i;
                var outer = centre + 1 + (spokes + 1) + i;
                b.Quad(inner, inner + 1, outer, outer + 1);
            }
            AddSepals(b, top, 2, budLength, budRadius, 0.62f, size * 0.16f, size * 0.12f, size * 0.1f);
        }

        /// <summary>A repeatable pseudo-random number in [-1, 1] for a lattice point.</summary>
        static float Hash(int a, int b)
        {
            unchecked
            {
                var h = (uint)(a * 374761393 + b * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 32767.5f - 1f;
            }
        }

        // ---- the two-pose mesh ----------------------------------------------------------------------------------------------

        sealed class Builder
        {
            readonly List<Vector3> _open = new List<Vector3>();
            readonly List<Vector3> _closed = new List<Vector3>();
            readonly List<Vector3> _openNormal = new List<Vector3>();
            readonly List<Vector3> _closedNormal = new List<Vector3>();
            readonly List<Color> _colors = new List<Color>();
            readonly List<int> _triangles = new List<int>();

            public int Count => _open.Count;

            /// <summary>A vertex of both poses. A zero normal means "work it out from the triangles".</summary>
            public void Add(Vector3 open, Vector3 closed, Vector3 openNormal, Vector3 closedNormal, Color color)
            {
                _open.Add(open);
                _closed.Add(closed);
                _openNormal.Add(openNormal);
                _closedNormal.Add(closedNormal);
                _colors.Add(color);
            }

            public void Triangle(int a, int b, int c)
            {
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
            }

            /// <summary>Two triangles for the quad whose corners are a b (one row) and c d (the next row).</summary>
            public void Quad(int a, int b, int c, int d)
            {
                Triangle(a, c, b);
                Triangle(b, c, d);
            }

            public Mesh ToMesh(string name)
            {
                var openNormals = Normals(_open, _openNormal);
                var closedNormals = Normals(_closed, _closedNormal);
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_open);
                mesh.SetTriangles(_triangles, 0);
                mesh.SetColors(_colors);
                mesh.SetUVs(1, _closed);
                mesh.SetUVs(2, openNormals);
                mesh.SetUVs(3, closedNormals);
                mesh.SetNormals(openNormals);
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>Normals of a pose: the given ones where there are some, else the average of the faces around the vertex.</summary>
            List<Vector3> Normals(List<Vector3> positions, List<Vector3> given)
            {
                var sums = new Vector3[positions.Count];
                for (var i = 0; i < _triangles.Count; i += 3)
                {
                    int a = _triangles[i], b = _triangles[i + 1], c = _triangles[i + 2];
                    var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    sums[a] += face; sums[b] += face; sums[c] += face;
                }
                var result = new List<Vector3>(positions.Count);
                for (var i = 0; i < positions.Count; i++)
                {
                    var n = given[i] != Vector3.zero ? given[i] : sums[i];
                    var sqr = n.sqrMagnitude;
                    result.Add(sqr > 1e-16f ? n / Mathf.Sqrt(sqr) : Vector3.up); // (Vector3.normalized gives zero for tiny vectors)
                }
                return result;
            }
        }
    }
}
