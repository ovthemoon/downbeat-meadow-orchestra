using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class FlowerMeshFactoryTests
    {
        static readonly FlowerSpecies[] All = { FlowerSpecies.Bellflower, FlowerSpecies.Dandelion, FlowerSpecies.Poppy };

        static FlowerLookDto Look(FlowerSpecies species)
        {
            var content = TestContent.LoadShipped().meadow.flowers;
            switch (species)
            {
                case FlowerSpecies.Bellflower: return content.bellflower;
                case FlowerSpecies.Dandelion: return content.dandelion;
                default: return content.poppy;
            }
        }

        static Mesh Make(FlowerSpecies species) => FlowerMeshFactory.Create(species, Look(species));

        static float MaxRadius(IReadOnlyList<Vector3> points, float aboveY)
        {
            var r = 0f;
            foreach (var p in points)
            {
                if (p.y >= aboveY) r = Mathf.Max(r, new Vector2(p.x, p.z).magnitude);
            }
            return r;
        }

        static List<Vector3> Uvs(Mesh mesh, int channel)
        {
            var list = new List<Vector3>();
            mesh.GetUVs(channel, list);
            return list;
        }

        [TestCaseSource(nameof(All))]
        public void Mesh_HasConsistentStreams_AndValidTriangles(FlowerSpecies species)
        {
            var mesh = Make(species);

            Assert.Greater(mesh.vertexCount, 20);
            Assert.AreEqual(mesh.vertexCount, Uvs(mesh, 1).Count, "the closed pose of every vertex");
            Assert.AreEqual(mesh.vertexCount, Uvs(mesh, 2).Count, "the open normal");
            Assert.AreEqual(mesh.vertexCount, Uvs(mesh, 3).Count, "the closed normal");
            Assert.AreEqual(mesh.vertexCount, mesh.colors.Length, "colour channel: gradient, region, occlusion");
            var triangles = mesh.triangles;
            Assert.AreEqual(0, triangles.Length % 3);
            foreach (var index in triangles) Assert.That(index, Is.InRange(0, mesh.vertexCount - 1));
            Assert.Less(triangles.Length / 3, 650, "a cheap mesh: a few hundred of them are drawn");
        }

        [TestCaseSource(nameof(All))]
        public void Mesh_StandsOnTheGround_AndTheHeadIsAtTheStemHeight(FlowerSpecies species)
        {
            var look = Look(species);
            var mesh = Make(species);

            Assert.AreEqual(0f, mesh.bounds.min.y, 0.01f, "the stem starts at y 0");
            Assert.GreaterOrEqual(mesh.bounds.max.y, look.stemHeight * 0.98f);
            Assert.LessOrEqual(mesh.bounds.max.y, look.stemHeight + look.headSize * 1.6f);
        }

        [TestCaseSource(nameof(All))]
        public void OpenHead_IsWiderThanTheClosedBud(FlowerSpecies species)
        {
            var look = Look(species);
            var mesh = Make(species);
            var open = new List<Vector3>();
            mesh.GetVertices(open);
            var closed = Uvs(mesh, 1);
            var aboveStem = look.stemHeight - look.headSize * 1.4f; // the head, also a bell that hangs below the top of the stem

            var openWidth = MaxRadius(open, aboveStem);
            var closedWidth = MaxRadius(closed, aboveStem);

            Assert.Greater(openWidth, closedWidth * 1.3f, $"{species}: a bud is narrow, an open flower is not");
            Assert.Greater(openWidth, look.headSize * 0.3f, "and the open head has about the size of the content");
        }

        [TestCaseSource(nameof(All))]
        public void Stem_IsTheSameInBothPoses(FlowerSpecies species)
        {
            var look = Look(species);
            var mesh = Make(species);
            var open = new List<Vector3>();
            mesh.GetVertices(open);
            var closed = Uvs(mesh, 1);
            var stemVertices = 0;

            for (var i = 0; i < open.Count; i++)
            {
                if (mesh.colors[i].g > 0.1f) continue; // region 0 is the stem
                stemVertices++;
                Assert.AreEqual(0f, Vector3.Distance(open[i], closed[i]), 1e-5f, "the stem does not change when the flower opens");
            }
            Assert.Greater(stemVertices, 8, $"{species} has a stem");
            Assert.AreEqual(look.stemHeight, MaxY(open, mesh, 0.1f), 0.02f, "the stem ends where the head starts");
        }

        static float MaxY(List<Vector3> points, Mesh mesh, float maxRegion)
        {
            var y = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                if (mesh.colors[i].g <= maxRegion) y = Mathf.Max(y, points[i].y);
            }
            return y;
        }

        [TestCaseSource(nameof(All))]
        public void Normals_AreUnitLength_InBothPoses(FlowerSpecies species)
        {
            var mesh = Make(species);

            foreach (var n in Uvs(mesh, 2)) Assert.AreEqual(1f, n.magnitude, 0.02f);
            foreach (var n in Uvs(mesh, 3)) Assert.AreEqual(1f, n.magnitude, 0.02f);
        }

        [TestCaseSource(nameof(All))]
        public void EveryFlowerHasGreenSepals_SoThatOnlyTheTipOfABudShowsColour(FlowerSpecies species)
        {
            var mesh = Make(species);
            var closed = Uvs(mesh, 1);
            var sepals = 0;
            float sepalTop = 0f, headTop = 0f;
            for (var i = 0; i < closed.Count; i++)
            {
                var region = mesh.colors[i].g;
                if (region > 0.1f && region < 0.4f) { sepals++; sepalTop = Mathf.Max(sepalTop, closed[i].y); }
                else if (region >= 0.4f) headTop = Mathf.Max(headTop, closed[i].y);
            }

            Assert.GreaterOrEqual(sepals, 6, $"{species} has sepals (region 0.25)");
            Assert.Less(sepalTop, headTop - 0.005f, "the sepals stop below the tip of the bud: only the tip shows colour");
        }

        [TestCaseSource(nameof(All))]
        public void ClosedBud_IsSmallAndElongated(FlowerSpecies species)
        {
            var look = Look(species);
            var mesh = Make(species);
            var closed = Uvs(mesh, 1);
            float top = 0f, minX = 9f, maxX = -9f, minZ = 9f, maxZ = -9f;
            for (var i = 0; i < closed.Count; i++)
            {
                if (mesh.colors[i].g < 0.4f) continue;
                top = Mathf.Max(top, closed[i].y);
                minX = Mathf.Min(minX, closed[i].x); maxX = Mathf.Max(maxX, closed[i].x);
                minZ = Mathf.Min(minZ, closed[i].z); maxZ = Mathf.Max(maxZ, closed[i].z);
            }
            var width = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f;
            var height = top - MaxY(new List<Vector3>(Uvs(mesh, 1)), mesh, 0.1f);

            Assert.Less(height, 0.065f, "a bud is a few centimetres");
            Assert.Greater(height, 0.025f);
            Assert.Greater(height / (2f * width), 1.2f, $"{species}: elongated, not a ball");
        }

        [Test]
        public void Dandelion_OpensIntoManyFilaments_NotASolidBall()
        {
            var mesh = Make(FlowerSpecies.Dandelion);
            var filamentVertices = 0;
            foreach (var color in mesh.colors) { if (color.g >= 0.4f && color.g < 0.75f && color.a < 0.999f) filamentVertices++; }

            Assert.Greater(filamentVertices, 150, "the puff is made of seed filaments with a feathered end (alpha below 1)");
        }

        [Test]
        public void Poppy_HasADarkHeartWithAnAngleAroundIt_ForTheRingOfStamens()
        {
            var mesh = Make(FlowerSpecies.Poppy);
            float minAngle = 2f, maxAngle = -1f;
            var heart = 0;
            foreach (var color in mesh.colors)
            {
                if (color.g < 0.9f) continue;
                heart++;
                minAngle = Mathf.Min(minAngle, color.a);
                maxAngle = Mathf.Max(maxAngle, color.a);
            }

            Assert.GreaterOrEqual(heart, 20);
            Assert.LessOrEqual(minAngle, 0.01f);
            Assert.GreaterOrEqual(maxAngle, 0.99f, "the angle channel goes once all the way round");
        }

        [Test]
        public void Bellflower_HasAScallopedLip_FiveLobes()
        {
            var mesh = Make(FlowerSpecies.Bellflower);
            var open = new List<Vector3>();
            mesh.GetVertices(open);
            var look = Look(FlowerSpecies.Bellflower);
            var rimPoints = new List<Vector3>();
            for (var i = 0; i < open.Count; i++)
            {
                var c = mesh.colors[i];
                if (c.g < 0.4f || c.g > 0.6f || c.r < 0.99f) continue;
                rimPoints.Add(open[i]);
            }
            var centre = Vector3.zero;
            foreach (var point in rimPoints) centre += point / rimPoints.Count;
            var rim = new List<float>();
            foreach (var point in rimPoints) rim.Add(new Vector2(point.x - centre.x, point.z - centre.z).magnitude);

            Assert.AreEqual(15, rim.Count, "five petals, three vertices along the lip each");
            Assert.Greater(Mathf.Max(rim.ToArray()) - Mathf.Min(rim.ToArray()), Mathf.Max(rim.ToArray()) * 0.12f, "the lip is lobed: its radius goes up and down");
        }

        [Test]
        public void Meshes_DifferByShape_NotJustBySize()
        {
            var bell = Make(FlowerSpecies.Bellflower);
            var dandelion = Make(FlowerSpecies.Dandelion);
            var poppy = Make(FlowerSpecies.Poppy);

            Assert.AreNotEqual(bell.vertexCount, dandelion.vertexCount);
            Assert.AreNotEqual(dandelion.vertexCount, poppy.vertexCount);
        }

        [Test]
        public void Bellflower_OpensDownwards_PoppyOpensSideways()
        {
            var bell = Make(FlowerSpecies.Bellflower);
            var open = new List<Vector3>();
            bell.GetVertices(open);
            var closed = Uvs(bell, 1);
            var head = Look(FlowerSpecies.Bellflower).stemHeight;

            float lowestOpen = float.MaxValue, highestClosed = 0f;
            for (var i = 0; i < open.Count; i++)
            {
                if (bell.colors[i].g < 0.4f) continue;
                lowestOpen = Mathf.Min(lowestOpen, open[i].y);
                highestClosed = Mathf.Max(highestClosed, closed[i].y);
            }

            Assert.Less(lowestOpen, head - 0.03f, "an open bell hangs below the top of the stem");
            Assert.Greater(highestClosed, head + 0.02f, "a bud points up above it");
        }

        [Test]
        public void Create_RejectsAnUnknownSpecies()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FlowerMeshFactory.Create((FlowerSpecies)7, Look(FlowerSpecies.Poppy)));
        }

        // ---- the poppy up close: cupped, overlapping petals, an edge that curls, shading inside ------------------------------------------------

        sealed class PoppyParts
        {
            public readonly List<Vector3> Open = new List<Vector3>();     // the open pose, in the frame of the head: y along its axis, around the middle of the heart
            public readonly List<Vector3> OpenNormals = new List<Vector3>();
            public readonly List<float> Occlusion = new List<float>();
            public readonly List<float> Across = new List<float>();
            public Vector3 Heart;
        }

        static PoppyParts PoppyPetalVertices(Mesh mesh, int petal)
        {
            var open = new List<Vector3>();
            mesh.GetVertices(open);
            var normals = Uvs(mesh, 2);
            var colours = mesh.colors;
            var toHead = Quaternion.Inverse(FlowerMeshFactory.PoppyFace);
            var centre = Vector3.zero;
            for (var i = 0; i < open.Count; i++)
            {
                if (colours[i].g > 0.9f && colours[i].r < 0.01f) { centre = open[i]; break; }
            }
            var parts = new PoppyParts { Heart = Vector3.zero };
            var perPetal = FlowerMeshFactory.PoppyRings * FlowerMeshFactory.PoppyWidthSteps;
            var seen = 0;
            for (var i = 0; i < open.Count; i++)
            {
                if (Mathf.Abs(colours[i].g - FlowerMeshFactory.PetalRegion) > 0.01f) continue;
                if (seen / perPetal == petal)
                {
                    parts.Open.Add(toHead * (open[i] - centre));
                    parts.OpenNormals.Add(toHead * normals[i]);
                    parts.Occlusion.Add(colours[i].b);
                    parts.Across.Add(colours[i].a);
                }
                seen++;
            }
            return parts;
        }

        static float Azimuth(Vector3 local) => Mathf.Atan2(local.z, local.x);

        static float Wrap(float angle)
        {
            while (angle > Mathf.PI) angle -= 2f * Mathf.PI;
            while (angle <= -Mathf.PI) angle += 2f * Mathf.PI;
            return angle;
        }

        [Test]
        public void Poppy_HasFourPetals_InOneMesh_AndStaysCheap()
        {
            var mesh = Make(FlowerSpecies.Poppy);

            var petalVertices = mesh.colors.Count(c => Mathf.Abs(c.g - FlowerMeshFactory.PetalRegion) < 0.01f);
            Assert.AreEqual(FlowerMeshFactory.PoppyPetals * FlowerMeshFactory.PoppyRings * FlowerMeshFactory.PoppyWidthSteps, petalVertices);
            Assert.AreEqual(1, mesh.subMeshCount, "one mesh, one draw call per species");
            Assert.Less(mesh.triangles.Length / 3, 450, "no heavier than a few hundred triangles: a few hundred poppies are drawn");
        }

        [Test]
        public void Poppy_PetalsOverlapTheirNeighbours()
        {
            var mesh = Make(FlowerSpecies.Poppy);

            for (var petal = 0; petal < FlowerMeshFactory.PoppyPetals; petal++)
            {
                var parts = PoppyPetalVertices(mesh, petal);
                var middle = Mathf.PI * 0.25f + petal * Mathf.PI * 2f / FlowerMeshFactory.PoppyPetals;
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (var v in parts.Open)
                {
                    if (new Vector2(v.x, v.z).magnitude < 1e-4f) continue;
                    var delta = Wrap(Azimuth(v) - middle);
                    lowest = Mathf.Min(lowest, delta);
                    highest = Mathf.Max(highest, delta);
                }
                Assert.Greater(highest - lowest, 2f * Mathf.PI / FlowerMeshFactory.PoppyPetals * 1.25f, $"petal {petal} reaches well past its own quarter, over the next petals");
            }
        }

        [Test]
        public void Poppy_PetalsAreLayered_SoOverlappingOnesDoNotCutThroughEachOther()
        {
            var mesh = Make(FlowerSpecies.Poppy);
            var look = Look(FlowerSpecies.Poppy);
            var rings = FlowerMeshFactory.PoppyRings;
            var width = FlowerMeshFactory.PoppyWidthSteps;
            var radii = new float[FlowerMeshFactory.PoppyPetals];

            for (var petal = 0; petal < radii.Length; petal++)
            {
                var parts = PoppyPetalVertices(mesh, petal);
                var middleRing = (rings - 1) / 2;
                var sum = 0f;
                for (var step = 0; step < width; step++)
                {
                    var v = parts.Open[middleRing * width + step];
                    sum += new Vector2(v.x, v.z).magnitude;
                }
                radii[petal] = sum / width;
            }

            Assert.Greater(Mathf.Abs(radii[0] - radii[1]), look.headSize * 0.01f, "an outer petal and an inner one are not at the same distance from the axis");
            Assert.AreEqual(radii[0], radii[2], 1e-4f, "the two outer petals are alike");
            Assert.AreEqual(radii[1], radii[3], 1e-4f, "and so are the two inner ones");
        }

        [Test]
        public void Poppy_IsACup_TheHeartLiesLowerThanTheRimOfThePetals()
        {
            var mesh = Make(FlowerSpecies.Poppy);
            var look = Look(FlowerSpecies.Poppy);
            var rim = 0f;
            var count = 0;
            for (var petal = 0; petal < FlowerMeshFactory.PoppyPetals; petal++)
            {
                var parts = PoppyPetalVertices(mesh, petal);
                for (var step = 0; step < FlowerMeshFactory.PoppyWidthSteps; step++)
                {
                    rim += parts.Open[(FlowerMeshFactory.PoppyRings - 1) * FlowerMeshFactory.PoppyWidthSteps + step].y;
                    count++;
                }
            }

            Assert.Greater(rim / count, look.headSize * 0.2f, "the rim stands well above the middle of the flower: a bowl, not a plate");
        }

        [Test]
        public void Poppy_EdgeCurlsBackAtTheRim_AndTheCornersCurlMore()
        {
            var mesh = Make(FlowerSpecies.Poppy);
            var look = Look(FlowerSpecies.Poppy);
            var width = FlowerMeshFactory.PoppyWidthSteps;
            var rimRow = (FlowerMeshFactory.PoppyRings - 1) * width;
            var lower = 0f;

            for (var petal = 0; petal < FlowerMeshFactory.PoppyPetals; petal++)
            {
                var parts = PoppyPetalVertices(mesh, petal);
                var middle = parts.Open[rimRow + width / 2];
                var left = parts.Open[rimRow];
                var right = parts.Open[rimRow + width - 1];
                lower += middle.y - (left.y + right.y) * 0.5f;
                Assert.Less(new Vector2(left.x, left.z).magnitude, new Vector2(middle.x, middle.z).magnitude * 1.02f, "the corners do not stick out past the middle: the edge is round");
            }

            Assert.Greater(lower / FlowerMeshFactory.PoppyPetals, look.headSize * 0.01f, "the corners of the rim are lower than the middle of it: the edge rolls back");
            Assert.Less(lower / FlowerMeshFactory.PoppyPetals, look.headSize * 0.3f, "a slight curl, not a frill");
        }

        [Test]
        public void Poppy_IsShadedInside_DarkAtTheBaseAndLightAtTheEdge_TheInnerPetalsDarker()
        {
            var mesh = Make(FlowerSpecies.Poppy);
            var width = FlowerMeshFactory.PoppyWidthSteps;
            var rings = FlowerMeshFactory.PoppyRings;

            var outer = PoppyPetalVertices(mesh, 0);
            var inner = PoppyPetalVertices(mesh, 1);

            Assert.Less(outer.Occlusion[0], 0.4f, "the base of a petal is in the shade of the cup");
            Assert.Greater(outer.Occlusion[(rings - 1) * width], 0.95f, "the edge is in the light");
            for (var ring = 1; ring < rings; ring++) Assert.GreaterOrEqual(outer.Occlusion[ring * width], outer.Occlusion[(ring - 1) * width], "lighter all the way out");
            Assert.Less(inner.Occlusion[(rings / 2) * width], outer.Occlusion[(rings / 2) * width] - 0.04f, "an inner petal lies under the outer ones: darker at the same distance out");
        }
    }
}
