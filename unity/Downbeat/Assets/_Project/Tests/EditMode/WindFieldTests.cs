using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class WindFieldTests
    {
        const float Width = 1.8f;
        const float Tolerance = 1e-4f;

        static WindWave Sweep(double start, float amplitude = 1f, float speed = 10f, float life = 2f, float originX = -20f, float glow = 0f) =>
            new WindWave(originX, 0f, start, amplitude, speed, life, WindWaveKind.Sweep, glow);

        static WindWave Radial(double start, float originX = 0f, float originZ = 8f, float amplitude = 1f, float speed = 6f, float life = 2f, float glow = 0f) =>
            new WindWave(originX, originZ, start, amplitude, speed, life, WindWaveKind.Radial, glow);

        [Test]
        public void Sweep_FrontMovesWithSpeed()
        {
            var field = new WindField(4, Width);
            field.Add(Sweep(start: 10.0, speed: 10f, originX: -20f));

            // at age 1 s the front stands at x = -20 + 10 = -10; at age 2 s (still alive) at x = 0 ... use a long life so the fade does not move the peak
            var longLife = new WindField(4, Width);
            longLife.Add(Sweep(start: 10.0, speed: 10f, life: 100f, originX: -20f));

            Assert.AreEqual(longLife.SampleBend(-10f, 3f, 11.0), PeakOf(longLife, 11.0), Tolerance, "the peak is at x = origin + speed * age");
            Assert.Greater(longLife.SampleBend(-10f, 3f, 11.0), longLife.SampleBend(-5f, 3f, 11.0));
            Assert.Greater(longLife.SampleBend(-10f, 3f, 11.0), longLife.SampleBend(-15f, 3f, 11.0));
            Assert.AreEqual(longLife.SampleBend(-10f, 0f, 11.0), longLife.SampleBend(-10f, 25f, 11.0), Tolerance, "a sweep does not depend on z");
            Assert.AreEqual(longLife.SampleBend(0f, 3f, 12.0), PeakOf(longLife, 12.0), Tolerance, "one second later the front is 10 m further");
            Assert.AreEqual(1f - 1f / 100f, longLife.SampleBend(-10f, 3f, 11.0), Tolerance, "amplitude 1 at the front, faded by age / life");
        }

        static float PeakOf(WindField field, double now)
        {
            var peak = 0f;
            for (var x = -30f; x <= 30f; x += 0.01f) peak = System.Math.Max(peak, field.SampleBend(x, 3f, now));
            return peak;
        }

        [Test]
        public void Radial_PeaksAtRing()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 5.0, originX: 2f, originZ: 8f, speed: 6f, life: 100f));
            var now = 6.0; // ring radius 6 m

            var onRing = field.SampleBend(2f + 6f, 8f, now);
            var inside = field.SampleBend(2f + 3f, 8f, now);
            var outside = field.SampleBend(2f + 9f, 8f, now);
            var diagonal = field.SampleBend(2f + 6f * 0.6f, 8f + 6f * 0.8f, now);

            Assert.Greater(onRing, inside);
            Assert.Greater(onRing, outside);
            Assert.AreEqual(onRing, diagonal, Tolerance, "the ring is round");
            Assert.AreEqual(0f, field.SampleBend(2f, 8f, now), 1e-3f, "the centre is calm once the ring has left");
        }

        [Test]
        public void Wave_FadesToZeroAtLife()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 0.0, speed: 0f, life: 2f, originX: 0f, originZ: 5f)); // a stationary ring at radius 0: the peak stays at the origin

            var atStart = field.SampleBend(0f, 5f, 0.0);
            var atHalf = field.SampleBend(0f, 5f, 1.0);
            var atLife = field.SampleBend(0f, 5f, 2.0);
            var afterLife = field.SampleBend(0f, 5f, 3.0);

            Assert.AreEqual(1f, atStart, Tolerance);
            Assert.AreEqual(0.5f, atHalf, Tolerance);
            Assert.AreEqual(0f, atLife, Tolerance);
            Assert.AreEqual(0f, afterLife, Tolerance);
        }

        [Test]
        public void Wave_DoesNotActBeforeItStarts()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 10.0, speed: 0f, originX: 0f, originZ: 5f));

            Assert.AreEqual(0f, field.SampleBend(0f, 5f, 9.0), Tolerance);
        }

        [Test]
        public void Add_ReplacesOldest_WhenFull()
        {
            var field = new WindField(2, Width);
            field.Add(Radial(start: 1.0, originX: -10f, speed: 0f, life: 100f));
            field.Add(Radial(start: 2.0, originX: 0f, speed: 0f, life: 100f));
            field.Add(Radial(start: 3.0, originX: 10f, speed: 0f, life: 100f));

            Assert.AreEqual(2, field.Count);
            Assert.AreEqual(0f, field.SampleBend(-10f, 8f, 4.0), 1e-3f, "the oldest wave is gone");
            Assert.Greater(field.SampleBend(0f, 8f, 4.0), 0.9f);
            Assert.Greater(field.SampleBend(10f, 8f, 4.0), 0.9f);
        }

        [Test]
        public void CopyActive_SkipsExpired()
        {
            var field = new WindField(4, Width);
            field.Add(Sweep(start: 0.0, life: 1f));
            field.Add(Sweep(start: 5.0, life: 2f, originX: -3f, speed: 4f));
            var destination = new WindWaveGpu[4];

            var count = field.CopyActive(5.5, destination);

            Assert.AreEqual(1, count, "the first wave ended at t = 1");
            Assert.AreEqual(-3f, destination[0].OriginX, Tolerance);
            Assert.AreEqual(4f, destination[0].Speed, Tolerance);
            Assert.AreEqual(0.5f, destination[0].Age, Tolerance, "the shader gets the age, not the start time");
            Assert.AreEqual(2f, destination[0].LifeSec, Tolerance);
            Assert.AreEqual((float)WindWaveKind.Sweep, destination[0].Kind, Tolerance);
        }

        [Test]
        public void CopyActive_StopsAtTheSizeOfTheDestination()
        {
            var field = new WindField(4, Width);
            for (var i = 0; i < 4; i++) field.Add(Sweep(start: i, life: 100f));

            Assert.AreEqual(2, field.CopyActive(4.0, new WindWaveGpu[2]));
        }

        [Test]
        public void CopyActive_DoesNotCopyAWaveThatHasNotStarted()
        {
            var field = new WindField(4, Width);
            field.Add(Sweep(start: 10.0));

            Assert.AreEqual(0, field.CopyActive(9.0, new WindWaveGpu[4]));
        }

        [Test]
        public void Glow_IsZeroForNonGlowWaves()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 0.0, speed: 0f, life: 100f, originX: 0f, originZ: 5f, glow: 0f));

            Assert.AreEqual(0f, field.SampleGlow(0f, 5f, 1.0), Tolerance);
            Assert.Greater(field.SampleBend(0f, 5f, 1.0), 0.9f, "it still bends the grass");
        }

        [Test]
        public void Glow_FollowsTheRingOfAGlowWave()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 0.0, speed: 0f, life: 100f, originX: 0f, originZ: 5f, glow: 0.8f));

            Assert.AreEqual(0.8f * (1f - 1f / 100f), field.SampleGlow(0f, 5f, 1.0), Tolerance);
            Assert.AreEqual(0f, field.SampleGlow(9f, 5f, 1.0), 1e-3f);
        }

        [Test]
        public void SampleBend_AddsWavesUp()
        {
            var single = new WindField(4, Width);
            single.Add(Radial(start: 0.0, speed: 0f, life: 100f, originX: 0f, originZ: 5f, amplitude: 0.3f));
            var two = new WindField(4, Width);
            two.Add(Radial(start: 0.0, speed: 0f, life: 100f, originX: 0f, originZ: 5f, amplitude: 0.3f));
            two.Add(Radial(start: 0.0, speed: 0f, life: 100f, originX: 0f, originZ: 5f, amplitude: 0.2f));

            Assert.AreEqual(single.SampleBend(0f, 5f, 0.5) + 0.2f * (1f - 0.5f / 100f), two.SampleBend(0f, 5f, 0.5), Tolerance);
        }

        [Test]
        public void SampleVector_PointsAlongTheSweepAndAwayFromTheRingCentre()
        {
            var sweep = new WindField(4, Width);
            sweep.Add(Sweep(start: 10.0, speed: 10f, life: 100f, originX: -20f));
            var inSweep = sweep.SampleVector(-10f, 4f, 11.0);
            Assert.Greater(inSweep.X, 0.5f);
            Assert.AreEqual(0f, inSweep.Z, 1e-6f, "a sweep pushes along x");

            var ring = new WindField(4, Width);
            ring.Add(Radial(start: 0.0, originX: 0f, originZ: 5f, speed: 4f, life: 100f)); // radius 4 at t = 1
            Assert.Greater(ring.SampleVector(4f, 5f, 1.0).X, 0.5f, "to the right of the centre the ring pushes right");
            Assert.Less(ring.SampleVector(-4f, 5f, 1.0).X, -0.5f, "to the left, left");
            Assert.Greater(ring.SampleVector(0f, 9f, 1.0).Z, 0.5f, "behind it, away from the viewer");
            Assert.Less(ring.SampleVector(0f, 1f, 1.0).Z, -0.5f, "in front of it, towards the viewer");
        }

        [Test]
        public void SampleVector_HasTheLengthOfSampleBend_ForOneWave()
        {
            var field = new WindField(4, Width);
            field.Add(Radial(start: 0.0, originX: 1f, originZ: 6f, speed: 5f, life: 100f, amplitude: 0.7f));

            var v = field.SampleVector(3.5f, 7.5f, 0.6);

            Assert.AreEqual(field.SampleBend(3.5f, 7.5f, 0.6), Mathf.Sqrt(v.X * v.X + v.Z * v.Z), 1e-4f);
        }

        [Test]
        public void Constructor_RejectsNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WindField(0, Width));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WindField(4, 0f));
        }

        [Test]
        public void WindWaveGpu_IsThreeFloat4s()
        {
            // (originX, originZ, speed, kind) (amplitude, life, glow, age) (widthScale, 0, 0, 0): the third float4 came with the wide waves of the bottom row
            Assert.AreEqual(48, System.Runtime.InteropServices.Marshal.SizeOf<WindWaveGpu>(), "the shader's struct is 3 x float4");
            Assert.AreEqual(WindWaveGpu.SizeBytes, System.Runtime.InteropServices.Marshal.SizeOf<WindWaveGpu>());
        }
    }
}
