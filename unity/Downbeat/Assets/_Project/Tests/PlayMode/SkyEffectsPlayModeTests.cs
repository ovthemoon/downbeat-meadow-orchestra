using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    /// <summary>
    /// The effects of the top row in the air, on a particle director of its own (no scene, no audio): a ring of light, rising seeds and star dust are made at the point they are asked for, on time, in the colour asked
    /// for, and the Low tier makes half of them.
    /// </summary>
    public class SkyEffectsPlayModeTests
    {
        static readonly Vector3 Point = new Vector3(1.5f, 3.2f, 6.1f);
        static readonly SkyEffectKind[] Kinds = { SkyEffectKind.LightRing, SkyEffectKind.Seeds, SkyEffectKind.StarDust };

        sealed class Field : IMeadowField
        {
            public WindField Wind { get; } = new WindField(WindFieldGpu.Capacity, 2.6f);
            public double WindTime => Time.timeAsDouble;
            public FieldPoint WindDirection => default;
            public float HeightAt(float x, float z) => 0f;
        }

        sealed class Rig
        {
            public GameObject Host;
            public ParticleDirector Director;
            public Field Field = new Field();
            public MeadowDto Meadow;

            public ParticleSystem System(string name) => Director.transform.Find(name).GetComponent<ParticleSystem>();
        }

        static ContentDto Shipped() =>
            JsonUtility.FromJson<ContentDto>(System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        static Rig Build(MeadowQualityDto tier = null)
        {
            var content = Shipped();
            var meadow = content.meadow;
            var rig = new Rig { Meadow = meadow };
            rig.Host = new GameObject("SkyEffectsTestDirector");
            rig.Director = rig.Host.AddComponent<ParticleDirector>();
            typeof(ParticleDirector).GetField("particleShader", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rig.Director, Shader.Find("Downbeat/Meadow/Particle"));
            var layout = new MeadowLayout(meadow.layout.fieldWidth, meadow.layout.nearZ, meadow.layout.farZ, meadow.layout.horizonScreenY);
            rig.Director.Configure(meadow.particles, tier ?? meadow.quality[0], rig.Field, layout, meadow.camera, meadow.skyEffects);
            return rig;
        }

        static void Destroy(Rig rig) => Object.Destroy(rig.Host);

        static float DistanceFrom(Vector3 centre, IEnumerable<Vector3> points, out int count)
        {
            var sum = 0f;
            count = 0;
            foreach (var p in points) { sum += Vector3.Distance(p, centre); count++; }
            return count == 0 ? 0f : sum / count;
        }

        static Vector3 Centre(List<Vector3> points)
        {
            var sum = Vector3.zero;
            foreach (var p in points) sum += p;
            return sum / Mathf.Max(1, points.Count);
        }

        // ---- made at the point ------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EachEffect_IsMadeAtThePointItWasAskedFor()
        {
            var rig = Build();
            var settings = SkyEffectSettings.From(rig.Meadow.skyEffects);
            var positions = new List<Vector3>();
            foreach (var kind in Kinds)
            {
                rig.Director.EmitSky(kind, Point, Color.white, 1f, 0.0);   // in the system at once: they have not moved yet
                rig.Director.GetSkyPositions(kind, positions);
                Assert.AreEqual(settings.CountFor(kind, 1f, false), rig.Director.EmittedSky(kind), kind + ": as many as the content says");
                Assert.AreEqual(rig.Director.EmittedSky(kind), positions.Count, kind + ": all of them are alive");
                var reach = kind == SkyEffectKind.LightRing ? 0.1f : 0.6f;   // a ring starts as a small circle round the point; seeds and dust within a hand of it
                foreach (var p in positions) Assert.Less(Vector3.Distance(p, Point), reach, $"{kind} at {p}, asked for {Point}");
                Assert.Less(Vector3.Distance(Centre(positions), Point), 0.25f, kind + ": round the point, not beside it");
                yield return null;
            }
            Destroy(rig);
        }

        [UnityTest]
        public IEnumerator TheEffectIsInTheColourOfTheCell()
        {
            var rig = Build();
            var red = new Color(0.9f, 0.1f, 0.1f, 1f);
            foreach (var kind in Kinds) rig.Director.EmitSky(kind, Point, red, 1f, 0.0);
            yield return null;

            foreach (var kind in Kinds)
            {
                var name = kind == SkyEffectKind.LightRing ? "SkyRings" : (kind == SkyEffectKind.Seeds ? "SkySeeds" : "SkyStars");
                var live = new ParticleSystem.Particle[64];
                var n = rig.System(name).GetParticles(live);
                Assert.Greater(n, 0, name);
                for (var i = 0; i < n; i++)
                {
                    Assert.Greater(live[i].startColor.r, live[i].startColor.g + 100, name + ": a red particle (the colour is a Color32)");
                    Assert.Greater(live[i].startColor.r, live[i].startColor.b + 100, name);
                }
            }
            Destroy(rig);
        }

        [UnityTest]
        public IEnumerator AnEffectWaitsForItsTime_AndSurvivesARebuild()
        {
            var rig = Build();
            rig.Director.EmitSky(SkyEffectKind.Seeds, Point, Color.white, 1f, rig.Field.WindTime + 0.6);
            yield return null;
            Assert.AreEqual(1, rig.Director.PendingSkyCount, "not due yet");
            Assert.AreEqual(0, rig.Director.EmittedSky(SkyEffectKind.Seeds));

            var waiting = rig.Director.PendingSkyEffects();
            var meadow = rig.Meadow;
            var layout = new MeadowLayout(meadow.layout.fieldWidth, meadow.layout.nearZ, meadow.layout.farZ, meadow.layout.horizonScreenY);
            rig.Director.Configure(meadow.particles, meadow.quality[0], rig.Field, layout, meadow.camera, meadow.skyEffects);   // a rebuild (F5, a change of tier)
            Assert.AreEqual(0, rig.Director.PendingSkyCount, "the rebuild forgot it ...");
            rig.Director.EnqueueSkyAgain(waiting);
            Assert.AreEqual(1, rig.Director.PendingSkyCount, "... and it was asked for again");

            yield return new WaitForSeconds(0.9f);

            Assert.AreEqual(0, rig.Director.PendingSkyCount);
            Assert.Greater(rig.Director.EmittedSky(SkyEffectKind.Seeds), 0, "made when its time came");
            Destroy(rig);
        }

        [Test]
        public void NothingIsMade_BeforeTheDirectorIsConfigured()
        {
            var host = new GameObject("Unconfigured");
            var director = host.AddComponent<ParticleDirector>();
            Assert.DoesNotThrow(() => director.EmitSky(SkyEffectKind.LightRing, Point, Color.white, 1f, 0.0));
            Assert.AreEqual(0, director.EmittedSky(SkyEffectKind.LightRing));
            Assert.AreEqual(0, director.PendingSkyCount);
            Object.Destroy(host);
        }

        // ---- what they do -----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheRing_SpreadsFast_ThenSlowsDown_ToItsRadius()
        {
            var rig = Build();
            var settings = SkyEffectSettings.From(rig.Meadow.skyEffects);
            var life = settings.LifeFor(SkyEffectKind.LightRing);
            const float Decay = 2.2f;   // ParticleDirector.RingDecay: the speed of a dot falls as e^(-decay x time)
            rig.Director.EmitSky(SkyEffectKind.LightRing, Point, Color.white, 1f, 0.0);
            var live = new ParticleSystem.Particle[128];
            var system = rig.System("SkyRings");

            yield return new WaitForSeconds(0.45f);
            var n = system.GetParticles(live);
            Assert.Greater(n, 0);
            var worst = 0f;
            var radii = 0f;
            var seen = "";
            for (var i = 0; i < n; i++)
            {
                var age = live[i].startLifetime - live[i].remainingLifetime;
                var expected = settings.RingRadius * (1f - Mathf.Exp(-Decay * age)) / (1f - Mathf.Exp(-Decay * life));
                var radius = Vector3.Distance(live[i].position, Point);
                radii += radius;
                worst = Mathf.Max(worst, Mathf.Abs(radius - expected));
                if (i < 3) seen += $" [age {age:0.000} radius {radius:0.000} expected {expected:0.000} speed {live[i].velocity.magnitude:0.000}]";
            }
            Assert.Greater(radii / n, 0.35f, "after about half a second the ring is wide already");
            Assert.Less(radii / n, settings.RingRadius, "and has not reached its radius yet");
            Assert.Less(worst, 0.2f, "it spreads as a drag says: " + worst + seen);
            Destroy(rig);
        }

        [UnityTest]
        public IEnumerator TheRing_FacesTheViewer()
        {
            var rig = Build();
            rig.Director.EmitSky(SkyEffectKind.LightRing, Point, Color.white, 1f, 0.0);
            yield return new WaitForSeconds(0.3f);

            var positions = new List<Vector3>();
            rig.Director.GetSkyPositions(SkyEffectKind.LightRing, positions);
            var lookDown = -rig.Meadow.camera.pitchDeg * Mathf.Deg2Rad;
            var forward = new Vector3(0f, -Mathf.Sin(lookDown), Mathf.Cos(lookDown));   // the way the camera looks
            Assert.Greater(positions.Count, 8);
            foreach (var p in positions) Assert.AreEqual(0f, Vector3.Dot(p - Point, forward), 0.05f, "every dot is in the plane across the line of sight");
            Destroy(rig);
        }

        [UnityTest]
        public IEnumerator TheSeedsRise_AndTheWindCarriesThem()
        {
            var rig = Build();
            rig.Director.EmitSky(SkyEffectKind.Seeds, Point, Color.white, 1f, 0.0);
            var positions = new List<Vector3>();
            yield return null;
            rig.Director.GetSkyPositions(SkyEffectKind.Seeds, positions);
            var start = Centre(positions);

            yield return new WaitForSeconds(0.9f);
            rig.Director.GetSkyPositions(SkyEffectKind.Seeds, positions);
            var calm = Centre(positions);
            Assert.Greater(calm.y, start.y + 0.2f, "seeds in the air rise");
            Assert.Less(Mathf.Abs(calm.x - start.x), 0.4f, "and without wind they hardly drift");

            // a steady push towards +x over the point
            rig.Field.Wind.Add(new WindWave(Point.x, 0f, rig.Field.WindTime, 2f, 0f, 100f, WindWaveKind.Sweep, 0f, 4f));
            rig.Director.EmitSky(SkyEffectKind.Seeds, Point, Color.white, 1f, 0.0);
            yield return new WaitForSeconds(0.9f);
            rig.Director.GetSkyPositions(SkyEffectKind.Seeds, positions);
            Assert.Greater(Centre(positions).x, calm.x + 0.5f, "the wind of the meadow carries them like the seeds on the ground");
            Destroy(rig);
        }

        [UnityTest]
        public IEnumerator TheStarDust_FallsSlowly_AndTwinkles()
        {
            var rig = Build();
            rig.Director.EmitSky(SkyEffectKind.StarDust, Point, Color.white, 1f, 0.0);
            var positions = new List<Vector3>();
            yield return null;
            rig.Director.GetSkyPositions(SkyEffectKind.StarDust, positions);
            var start = Centre(positions);

            yield return new WaitForSeconds(0.8f);
            rig.Director.GetSkyPositions(SkyEffectKind.StarDust, positions);
            var fell = start.y - Centre(positions).y;

            Assert.Greater(fell, 0.06f, "it comes down");
            Assert.Less(fell, 0.4f, "slowly");
            var gradient = rig.System("SkyStars").colorOverLifetime.color.gradient;
            var alphas = new List<float>();
            foreach (var key in gradient.alphaKeys) alphas.Add(key.alpha);
            Assert.GreaterOrEqual(gradient.alphaKeys.Length, 8, "dim, bright, dim ...");
            Assert.Less(Mathf.Min(alphas.ToArray()), 0.01f, "it fades in from nothing");
            Assert.Less(alphas[2], alphas[1], "and swings between bright and dim");
            Destroy(rig);
        }

        // ---- the Low tier -----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheLowTier_MakesHalfTheParticles()
        {
            var meadow = Shipped().meadow;
            var low = System.Array.Find(meadow.quality, t => SkyEffectSettings.IsLowTier(t));
            Assert.IsNotNull(low, "the shipped content has a Low tier");
            var high = Build(meadow.quality[0]);
            var weak = Build(low);
            Assert.IsFalse(high.Director.SkyIsLowTier);
            Assert.IsTrue(weak.Director.SkyIsLowTier);

            foreach (var kind in Kinds)
            {
                high.Director.EmitSky(kind, Point, Color.white, 1f, 0.0);
                weak.Director.EmitSky(kind, Point, Color.white, 1f, 0.0);
            }
            yield return null;

            foreach (var kind in Kinds)
            {
                var full = high.Director.EmittedSky(kind);
                var half = weak.Director.EmittedSky(kind);
                Assert.AreEqual(Mathf.Round(full * meadow.skyEffects.lowCountScale), half, kind + ": " + full + " on High, " + half + " on Low");
                Assert.AreEqual(half, weak.Director.AliveSky(kind), kind + ": and that many are alive");
                Assert.Less(half, full, kind.ToString());
            }
            Destroy(high);
            Destroy(weak);
        }

        // ---- how they are drawn -----------------------------------------------------------------------------------------

        [Test]
        public void TheSkyMaterials_AreAdded_AndNotHazedAway()
        {
            var rig = Build();

            foreach (var name in new[] { "SkyRings", "SkySeeds", "SkyStars" })
            {
                var material = rig.System(name).GetComponent<ParticleSystemRenderer>().sharedMaterial;
                Assert.AreEqual(0f, material.GetFloat("_HazeAmount"), name + ": up in the sky nothing dissolves into the plate (the haze of the shader hides everything above row 0.52)");
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, material.GetFloat("_DstBlend"), name + ": light is added");
                Assert.AreEqual(rig.Meadow.skyEffects.glow, material.GetFloat("_Gain"), 1e-5f, name);
            }
            var petals = rig.System("Petals").GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.AreEqual(1f, petals.GetFloat("_HazeAmount"), "the petals on the ground are hazed as before");
            Destroy(rig);
        }

        [Test]
        public void TheEffectsHaveSystemsOfTheirOwn_SoThePetalsOnTheGroundAreLeftAlone()
        {
            var rig = Build();
            rig.Director.EmitSky(SkyEffectKind.Seeds, Point, Color.white, 1f, 0.0);
            rig.Director.EmitSky(SkyEffectKind.LightRing, Point, Color.white, 1f, 0.0);

            Assert.AreEqual(0, rig.Director.EmittedPetals, "the petals of the marimba count apart");
            Assert.AreEqual(0, rig.Director.AlivePetals);
            Assert.IsNull(rig.Director.transform.Find("Seeds"), "no seeds are thrown on the ground: the recorder's seeds rise in the air");
            Destroy(rig);
        }
    }
}
