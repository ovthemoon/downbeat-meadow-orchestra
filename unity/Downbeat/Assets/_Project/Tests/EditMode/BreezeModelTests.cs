using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class BreezeModelTests
    {
        const float DegToRad = (float)(Math.PI / 180.0);

        static MeadowDto Meadow() => TestContent.LoadShipped().meadow;

        static BreezeModel Model(Action<MeadowDto> change = null)
        {
            var meadow = Meadow();
            change?.Invoke(meadow);
            return new BreezeModel(meadow.breeze, meadow.sky.clouds);
        }

        static float Length(FieldPoint p) => (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);

        static float Dot(FieldPoint a, FieldPoint b) => a.X * b.X + a.Z * b.Z;

        static FieldPoint At(BreezeModel model, float x, float z, float height, float bladePhase, double time) => model.Bend(x, z, height, bladePhase, model.PhaseAt(time));

        // ---- hash: the same numbers on the GPU ----------------------------------------------------------------------------

        [Test]
        public void Hash_MatchesAnIndependentImplementation()
        {
            // reference values from a separate Python implementation of the same integer hash (lowbias32)
            Assert.AreEqual(0u, BreezeHash.Hash(0u));
            Assert.AreEqual(1753845952u, BreezeHash.Hash(1u));
            Assert.AreEqual(2435775735u, BreezeHash.Hash(12345u));
            Assert.AreEqual(583018996u, BreezeHash.Hash(4095u));
            Assert.AreEqual(0.40834903717041016f, BreezeHash.Unit(1u), 1e-7f);
        }

        [Test]
        public void Unit_IsEvenlySpreadOverZeroToOne()
        {
            double sum = 0;
            var buckets = new int[10];
            for (uint k = 0; k < 100000; k++)
            {
                var v = BreezeHash.Unit(k);
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
                sum += v;
                buckets[(int)(v * 10f)]++;
            }
            Assert.AreEqual(0.5, sum / 100000, 0.01);
            foreach (var count in buckets) Assert.AreEqual(10000, count, 400);
        }

        // ---- direction -----------------------------------------------------------------------------------------------

        [Test]
        public void Direction_IsAUnitVectorAtTheContentAngle()
        {
            var model = Model(m => m.breeze.directionDeg = 90f);
            Assert.AreEqual(0f, model.Direction.X, 1e-5f);
            Assert.AreEqual(1f, model.Direction.Z, 1e-5f);

            var diagonal = Model(m => m.breeze.directionDeg = 32f);
            Assert.AreEqual((float)Math.Cos(32 * DegToRad), diagonal.Direction.X, 1e-5f);
            Assert.AreEqual((float)Math.Sin(32 * DegToRad), diagonal.Direction.Z, 1e-5f);
            Assert.AreEqual(1f, Length(diagonal.Direction), 1e-5f);
            Assert.AreEqual(0f, Dot(diagonal.Direction, diagonal.Across), 1e-5f, "across the wind is at a right angle");
        }

        // ---- the shape of one gust -----------------------------------------------------------------------------------

        [Test]
        public void GustProfile_PeaksAtOne_HasASteeperFrontThanBack_AndSpringsBackBehind()
        {
            const float width = 2.6f;

            Assert.AreEqual(1f, BreezeModel.GustProfile(0f, width, 0.22f), 0.01f);
            Assert.Less(BreezeModel.GustProfile(0.5f * width, width, 0f), BreezeModel.GustProfile(-0.5f * width, width, 0f), "the front is steeper: the grass is hit quickly and eases slowly");
            Assert.AreEqual(0f, BreezeModel.GustProfile(8f * width, width, 0.22f), 1e-4f, "far ahead: calm");
            Assert.AreEqual(0f, BreezeModel.GustProfile(-8f * width, width, 0.22f), 1e-4f, "far behind: calm");

            var lowest = 0f;
            for (var x = -6f * width; x <= 0f; x += 0.01f) lowest = Math.Min(lowest, BreezeModel.GustProfile(x, width, 0.22f));
            Assert.Less(lowest, -0.15f, "behind the gust the grass swings back past upright");
            Assert.Greater(lowest, -0.3f, "by a small share of the push");

            var noSpring = 0f;
            for (var x = -6f * width; x <= 6f * width; x += 0.01f) noSpring = Math.Min(noSpring, BreezeModel.GustProfile(x, width, 0f));
            Assert.GreaterOrEqual(noSpring, 0f, "without spring-back the grass is never pushed the other way");
        }

        [Test]
        public void GustProfile_IsContinuousAtTheFront()
        {
            var left = BreezeModel.GustProfile(-1e-4f, 2.6f, 0.22f);
            var right = BreezeModel.GustProfile(1e-4f, 2.6f, 0.22f);

            Assert.AreEqual(left, right, 1e-3f);
        }

        [Test]
        public void AGust_IsSeveralMetresWide()
        {
            var model = Model(m => { m.breeze.gust.jitter = 0f; m.breeze.gust.weakest = 1f; m.breeze.gust.frontWobble = 0f; m.breeze.gust.lagMetres = 0f; });
            var phase = model.PhaseAt(0.0);
            var peak = 0f;
            for (var a = 0f; a < model.GustSpacing; a += 0.02f) peak = Math.Max(peak, model.Gust(model.Direction.X * a, model.Direction.Z * a, 0.5f, phase));

            var above = 0f;
            for (var a = 0f; a < model.GustSpacing; a += 0.02f)
            {
                if (model.Gust(model.Direction.X * a, model.Direction.Z * a, 0.5f, phase) >= 0.5f * peak) above += 0.02f;
            }

            Assert.GreaterOrEqual(above, 3f, "the grass is leaning at more than half the peak over several metres");
            Assert.LessOrEqual(above, 8f, "but it is a band, not the whole field");
        }

        // ---- gusts roll across the field -----------------------------------------------------------------------------

        [Test]
        public void TheGustPatternMovesRigidlyAlongTheWindAtTheContentSpeed()
        {
            var model = Model();
            var speed = Meadow().breeze.gust.speed;
            var rng = new Random(5);
            for (var i = 0; i < 400; i++)
            {
                var x = (float)(rng.NextDouble() * 14 - 7);
                var z = (float)(rng.NextDouble() * 10 + 2);
                var time = rng.NextDouble() * 500;
                var dt = rng.NextDouble() * 6;
                var h = (float)rng.NextDouble();

                var now = model.Gust(x, z, h, model.PhaseAt(time));
                var later = model.Gust(x + model.Direction.X * speed * (float)dt, z + model.Direction.Z * speed * (float)dt, h, model.PhaseAt(time + dt));

                Assert.AreEqual(now, later, 2e-4f, $"the same gust, {dt:0.0} s later, {speed * dt:0.0} m further along the wind (sample {i})");
            }
        }

        [Test]
        public void AFixedSpotMeetsAGustEveryFewSeconds_AndTheyAreNotAllTheSame()
        {
            var model = Model();
            var peaks = new System.Collections.Generic.List<(double time, float value)>();
            float previous2 = 0, previous1 = 0;
            const double dt = 1.0 / 30.0;
            for (var t = 0.0; t < 240.0; t += dt)
            {
                var value = model.Gust(1f, 5f, 0.5f, model.PhaseAt(t));
                if (previous1 > previous2 && previous1 > value && previous1 > 0.25f) peaks.Add((t - dt, previous1));
                previous2 = previous1;
                previous1 = value;
            }

            Assert.GreaterOrEqual(peaks.Count, 20, "gusts keep coming");
            var seconds = (peaks[peaks.Count - 1].time - peaks[0].time) / (peaks.Count - 1);
            Assert.GreaterOrEqual(seconds, 2.0, "a few seconds apart");
            Assert.LessOrEqual(seconds, 8.0);
            var lowest = float.MaxValue;
            var highest = 0f;
            foreach (var peak in peaks)
            {
                lowest = Math.Min(lowest, peak.value);
                highest = Math.Max(highest, peak.value);
            }
            Assert.Greater(highest - lowest, 0.15f, "some gusts are stronger than others");
        }

        [Test]
        public void TheFrontIsNotAStraightLine()
        {
            var model = Model(m => { m.breeze.gust.jitter = 0f; m.breeze.gust.weakest = 1f; m.breeze.gust.lagMetres = 0f; });
            var phase = model.PhaseAt(3.0);
            // walk across the wind and find where, along the wind, the strongest point of the nearest gust lies
            var along = new float[40];
            for (var i = 0; i < along.Length; i++)
            {
                var u = i * 1.0f;
                var best = -1f;
                var bestA = 0f;
                var middle = phase.GustShift + 0.5f * model.GustSpacing;   // with no jitter every gust is in the middle of its cell: scan only this one
                for (var a = middle - 0.4f * model.GustSpacing; a < middle + 0.4f * model.GustSpacing; a += 0.02f)
                {
                    var x = model.Direction.X * a + model.Across.X * u;
                    var z = model.Direction.Z * a + model.Across.Z * u;
                    var g = model.Gust(x, z, 0.5f, phase);
                    if (g > best) { best = g; bestA = a; }
                }
                along[i] = bestA;
            }
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var a in along) { min = Math.Min(min, a); max = Math.Max(max, a); }

            Assert.Greater(max - min, 0.8f, "the front waves");
            Assert.Less(max - min, 2f * Meadow().breeze.gust.frontWobble + 0.5f, "within the wobble of the content");
        }

        // ---- one direction, neighbours together, nothing flickers -----------------------------------------------------

        [Test]
        public void TheGrassLeansTheWayTheWindBlows_NotThisWayAndThat()
        {
            var model = Model();
            var rng = new Random(11);
            double along = 0, across = 0;
            var leaning = 0;
            const int n = 4000;
            for (var i = 0; i < n; i++)
            {
                var bend = At(model, (float)(rng.NextDouble() * 14 - 7), (float)(rng.NextDouble() * 10 + 2), (float)rng.NextDouble(), (float)rng.NextDouble(), rng.NextDouble() * 600);
                var a = Dot(bend, model.Direction);
                along += a;
                across += Dot(bend, model.Across);
                if (a > 0f) leaning++;
            }

            Assert.Greater(along / n, 0.07, "on average it leans along the wind");
            Assert.Less(Math.Abs(across / n), 0.1 * (along / n), "and not to the side");
            Assert.Greater(leaning / (double)n, 0.9, "in nearly every place at nearly every time it leans with the wind");
        }

        [Test]
        public void NeighbouringBlades_MoveTogether_WhateverTheirOwnNumbers()
        {
            // The statistic the old breeze (a different random offset for every blade) was measured with offline from its shader formula: for blades 10 cm apart the median of
            // |difference of the two bends| / |bend| was 0.36 and its 90th percentile 1.54, i.e. a blade next to one at rest could be leaning as far as its own whole bend.
            var model = Model();
            var rng = new Random(3);
            var relative = new System.Collections.Generic.List<float>();
            var together = 0;
            for (var i = 0; i < 6000; i++)
            {
                var x = (float)(rng.NextDouble() * 14 - 7);
                var z = (float)(rng.NextDouble() * 10 + 2);
                var h = 0.4f + 0.6f * (float)rng.NextDouble();
                var time = rng.NextDouble() * 400;
                var a = At(model, x, z, h, (float)rng.NextDouble(), time);
                var b = At(model, x + 0.1f, z, h, (float)rng.NextDouble(), time);   // a blade 10 cm away with a completely different hash
                var size = (Length(a) + Length(b)) * 0.5f;
                if (size < 0.05f) continue;
                relative.Add(Length(new FieldPoint(a.X - b.X, a.Z - b.Z)) / size);
                if (Dot(a, b) / (Length(a) * Length(b)) > 0.9f) together++;
            }
            relative.Sort();
            UnityEngine.Debug.Log($"[BreezeModelTests] blades 10 cm apart: median |dBend|/|bend| {relative[relative.Count / 2]:0.000}, 90th percentile {relative[relative.Count * 9 / 10]:0.000}, same direction (cos > 0.9) {together / (float)relative.Count:0.000} of {relative.Count} pairs");

            Assert.Greater(relative.Count, 3000, "enough samples");
            Assert.Less(relative[relative.Count / 2], 0.2f, "median difference (old breeze: 0.36)");
            Assert.Less(relative[relative.Count * 9 / 10], 0.5f, "90th percentile difference (old breeze: 1.54)");
            Assert.Greater(together / (float)relative.Count, 0.95f, "nearly all neighbours lean the same way");
        }

        [Test]
        public void TheBend_IsSmoothInTime_AtSixtyFramesASecond()
        {
            var model = Model();
            const double dt = 1.0 / 60.0;
            var worstSecondDifference = 0f;
            var worstStep = 0f;
            var positions = new[] { (-5f, 2.5f), (0f, 5f), (4f, 8f), (-2f, 11f) };
            foreach (var (x, z) in positions)
            {
                for (var t = 1.0; t < 90.0; t += dt)
                {
                    var before = At(model, x, z, 1f, 0.37f, t - dt);
                    var now = At(model, x, z, 1f, 0.37f, t);
                    var after = At(model, x, z, 1f, 0.37f, t + dt);
                    worstSecondDifference = Math.Max(worstSecondDifference, Length(new FieldPoint(after.X - 2f * now.X + before.X, after.Z - 2f * now.Z + before.Z)));
                    worstStep = Math.Max(worstStep, Length(new FieldPoint(after.X - now.X, after.Z - now.Z)));
                }
            }

            Assert.Less(worstSecondDifference, 0.0015f, "no jolt from one frame to the next (the change of the change)");
            Assert.Less(worstStep, 0.012f, "a tip never moves more than about 1 % of the blade height in a frame");
        }

        [Test]
        public void TheBend_HasNoJumpWhereTheGustPatternStartsANewCell()
        {
            var model = Model();
            var gust = Meadow().breeze.gust;
            var cellSeconds = gust.spacing / gust.speed;
            const double dt = 1.0 / 240.0;
            var worst = 0f;
            for (var cell = 1; cell <= 6; cell++)
            {
                var edge = cell * cellSeconds;
                var previous = At(model, 1f, 4f, 1f, 0.5f, edge - 0.25);
                for (var t = edge - 0.25 + dt; t < edge + 0.25; t += dt)
                {
                    var now = At(model, 1f, 4f, 1f, 0.5f, t);
                    worst = Math.Max(worst, Length(new FieldPoint(now.X - previous.X, now.Z - previous.Z)));
                    previous = now;
                }
            }

            Assert.Less(worst, 0.004f, "the tiny steps of 1/240 s stay tiny across the moment the pattern wraps");
        }

        [Test]
        public void ATipWhipsLaterThanItsFoot()
        {
            var model = Model(m => { m.breeze.gust.jitter = 0f; m.breeze.gust.weakest = 1f; m.breeze.gust.frontWobble = 0f; m.breeze.sway.amplitude = 0f; m.breeze.flutter.amplitude = 0f; m.breeze.meanVariation = 0f; });
            double PeakNear(float height, double from, double to)
            {
                var bestTime = from;
                var best = float.MinValue;
                for (var t = from; t < to; t += 0.005)
                {
                    var along = Dot(At(model, 0f, 0f, height, 0.5f, t), model.Direction);
                    if (along > best) { best = along; bestTime = t; }
                }
                return bestTime;
            }

            var foot = PeakNear(0f, 0.0, 6.0);
            var tip = PeakNear(1f, foot - 1.2, foot + 1.2);   // the same gust

            Assert.Greater(tip - foot, 0.3, "the tip is bent most a little after the foot");
            Assert.Less(tip - foot, 1.0);
        }

        // ---- long uptime -----------------------------------------------------------------------------------------------

        [Test]
        public void ThePhases_StayExactAfterDaysOfUptime()
        {
            var model = Model();
            var gust = Meadow().breeze.gust;
            const double dt = 1.0 / 60.0;
            foreach (var start in new[] { 0.0, 86400.0 * 3, 86400.0 * 400 })
            {
                var a = model.PhaseAt(start);
                var b = model.PhaseAt(start + dt);

                Assert.That(a.GustShift, Is.InRange(0f, gust.spacing));
                Assert.That(a.GustCell, Is.InRange(0f, 4095f));
                Assert.AreEqual(Math.Floor(a.GustCell), a.GustCell, "a whole number of cells");
                Assert.That(a.SwayPhase, Is.InRange(0f, 1f));
                Assert.That(a.FlutterPhase, Is.InRange(0f, 1f));
                Assert.That(a.MeanPhase, Is.InRange(0f, 1f));

                var travelled = (b.GustCell - a.GustCell) * gust.spacing + (b.GustShift - a.GustShift);
                if (b.GustCell < a.GustCell) travelled += 4096f * gust.spacing;   // the cell number wrapped
                Assert.AreEqual(gust.speed * dt, travelled, 1e-4, $"a frame's travel of the gusts is exact at {start} s");
            }
        }

        [Test]
        public void TheGustsRepeatOnlyAfterHours()
        {
            var model = Model();
            var gust = Meadow().breeze.gust;
            var repeatSeconds = 4096.0 * gust.spacing / gust.speed;

            Assert.Greater(repeatSeconds, 3600.0, "an exhibition never sees the same gusts twice within the hour");
            Assert.AreEqual(model.Gust(1f, 5f, 0.5f, model.PhaseAt(100.0)), model.Gust(1f, 5f, 0.5f, model.PhaseAt(100.0 + repeatSeconds)), 1e-4f);
        }

        // ---- the validator keeps the gust window honest ---------------------------------------------------------------------

        static float LargestStepAtCellEdges(BreezeModel model, float spacing)
        {
            const float delta = 1e-4f;
            var worst = 0f;
            for (var time = 1.0; time < 1.0 + 6 * spacing / Meadow().breeze.gust.speed; time += 0.37)
            {
                var phase = model.PhaseAt(time);
                for (var edge = -3; edge <= 3; edge++)
                {
                    var along = phase.GustShift + edge * spacing;   // where (along - shift) / spacing is a whole number: the window of gusts changes there
                    foreach (var height in new[] { 0f, 0.5f, 1f })
                    {
                        float G(float a) => model.Gust(model.Direction.X * a, model.Direction.Z * a, height, phase);
                        var expected = 2f * G(along - delta) - G(along - 3f * delta);   // the line through the two samples before the edge
                        worst = Math.Max(worst, Math.Abs(G(along + delta) - expected));
                    }
                }
            }
            return worst;
        }

        [Test]
        public void TheWidestGustTheValidatorAccepts_HasNoStepWhereTheWindowOfGustsChanges()
        {
            // on the very edge of what meadow.breeze.gust allows: the most jitter and spring-back, the longest lag and wobble the proportions leave room for
            var content = TestContent.LoadShipped();
            var meadow = content.meadow;
            var g = meadow.breeze.gust;
            g.spacing = 10f; g.jitter = 0.35f; g.springBack = 0.6f; g.weakest = 1f; g.lagMetres = 3f; g.frontWobble = 2f;
            g.width = (MeadowContentValidator.GustReachShare * g.spacing - g.lagMetres - g.frontWobble) / MeadowContentValidator.GustWidthFactor;
            var errors = ContentValidator.Validate(content);
            Assert.IsFalse(System.Linq.Enumerable.Any(errors, e => e.Contains("gust")), "these settings are on the edge of what is accepted: " + string.Join("; ", errors));

            var step = LargestStepAtCellEdges(new BreezeModel(meadow.breeze, meadow.sky.clouds), g.spacing);

            Assert.Less(step, 1.5e-3f, "a step this size (of a gust that is 1) would be a pop in the grass at every cell edge");
        }

        [Test]
        public void AGustWiderThanItsWindow_WouldReallyStepAtTheCellEdge_Which_IsWhyTheValidatorRefusesIt()
        {
            var meadow = Meadow();
            var g = meadow.breeze.gust;
            g.spacing = 2f; g.width = 5f; g.jitter = 0.35f; g.springBack = 0.6f; g.weakest = 1f; g.lagMetres = 0f; g.frontWobble = 0f;

            var step = LargestStepAtCellEdges(new BreezeModel(meadow.breeze, meadow.sky.clouds), g.spacing);

            Assert.Greater(step, 0.02f, "the old limits (width up to 30 with spacing down to 2) allowed this");
        }

        [Test]
        public void ASpacingTooNarrowForTheWidth_IsRefusedByTheValidator()
        {
            var content = TestContent.LoadShipped();
            content.meadow.breeze.gust.spacing = 2f;
            content.meadow.breeze.gust.width = 5f;

            var errors = ContentValidator.Validate(content);

            Assert.IsTrue(System.Linq.Enumerable.Any(errors, e => e.Contains("meadow.breeze.gust") && e.Contains("width") && e.Contains("spacing")), string.Join("; ", errors));
        }

        [Test]
        public void ALagOrAWobbleThatShiftsTheGustPastItsWindow_IsRefused()
        {
            var content = TestContent.LoadShipped();
            content.meadow.breeze.gust.lagMetres = 10f;
            content.meadow.breeze.gust.frontWobble = 20f;

            var errors = ContentValidator.Validate(content);

            Assert.IsTrue(System.Linq.Enumerable.Any(errors, e => e.Contains("lagMetres") && e.Contains("frontWobble") && e.Contains("spacing")), string.Join("; ", errors));
        }

        [Test]
        public void TheShippedGusts_FitTheirWindowWithRoomToSpare()
        {
            var g = Meadow().breeze.gust;

            Assert.LessOrEqual(MeadowContentValidator.GustWidthFactor * g.width + g.lagMetres + g.frontWobble, 0.8f * MeadowContentValidator.GustReachShare * g.spacing);
        }

        // ---- cloud shadows ---------------------------------------------------------------------------------------------

        [Test]
        public void CloudShadows_SlideAlongTheWindAtTheDriftSpeed()
        {
            var model = Model();
            var clouds = Meadow().sky.clouds;
            var rng = new Random(2);
            for (var i = 0; i < 300; i++)
            {
                var x = (float)(rng.NextDouble() * 40 - 20);
                var z = (float)(rng.NextDouble() * 60);
                var time = rng.NextDouble() * 5000;
                var dt = rng.NextDouble() * 30;
                var now = model.CloudShadow(x, z, model.PhaseAt(time));
                var later = model.CloudShadow(x + model.Direction.X * clouds.driftMps * (float)dt, z + model.Direction.Z * clouds.driftMps * (float)dt, model.PhaseAt(time + dt));

                Assert.AreEqual(now, later, 2e-3f, $"the same patch of shadow {dt:0.0} s later at {clouds.driftMps} m/s along the wind (sample {i})");
            }
        }

        [Test]
        public void CloudShadows_AreSoftPatches_NotEverywhereAndNotNowhere()
        {
            var model = Model();
            var phase = model.PhaseAt(42.0);
            var inside = 0;
            var darkish = 0;
            const int n = 100;
            for (var ix = 0; ix < n; ix++)
            {
                for (var iz = 0; iz < n; iz++)
                {
                    var s = model.CloudShadow(-50f + ix, iz, phase);
                    Assert.That(s, Is.InRange(0f, 1f));
                    if (s > 0.5f) inside++;
                    if (s > 0.02f && s < 0.98f) darkish++;
                }
            }
            var share = inside / (float)(n * n);

            Assert.Greater(share, 0.1f, "there are shadows");
            Assert.Less(share, 0.6f, "and there is light");
            Assert.Greater(darkish / (float)(n * n), 0.04f, "the edges are soft, not a hard line");
        }

        [Test]
        public void CloudShadows_StayPreciseAfterDaysOfUptime()
        {
            var model = Model();
            var clouds = Meadow().sky.clouds;
            var period = 256f * clouds.shadow.cellMetres;
            const double dt = 1.0 / 60.0;
            var rng = new Random(9);
            foreach (var start in new[] { 86400.0 * 3, 86400.0 * 400 })
            {
                var phase = model.PhaseAt(start);
                Assert.That(phase.ShadowShiftX, Is.InRange(0f, period), "the shift that reaches the GPU stays small");
                Assert.That(phase.ShadowShiftZ, Is.InRange(0f, period));
                for (var i = 0; i < 100; i++)
                {
                    var x = (float)(rng.NextDouble() * 30 - 15);
                    var z = (float)(rng.NextDouble() * 40);
                    var now = model.CloudShadow(x, z, phase);
                    var next = model.CloudShadow(x + model.Direction.X * clouds.driftMps * (float)dt, z + model.Direction.Z * clouds.driftMps * (float)dt, model.PhaseAt(start + dt));

                    Assert.AreEqual(now, next, 2e-4f, $"a frame's slide of the shadows is exact at {start} s");
                }
            }
        }

        [Test]
        public void TheCloudLayers_MoveAlongTheWindAtTheDriftSpeed()
        {
            var model = Model();
            var clouds = Meadow().sky.clouds;
            var a = model.PhaseAt(10.0);
            var b = model.PhaseAt(10.5);

            var nearX = b.NearShiftX - a.NearShiftX;
            var nearZ = b.NearShiftZ - a.NearShiftZ;
            Assert.AreEqual(clouds.driftMps * 0.5f, (float)Math.Sqrt(nearX * nearX + nearZ * nearZ), 1e-3f);
            Assert.AreEqual(model.Direction.X, nearX / (clouds.driftMps * 0.5f), 1e-3f, "in the direction of the wind");
            Assert.AreEqual(model.Direction.Z, nearZ / (clouds.driftMps * 0.5f), 1e-3f);
            var farX = b.FarShiftX - a.FarShiftX;
            var farZ = b.FarShiftZ - a.FarShiftZ;
            Assert.AreEqual(nearX, farX, 1e-3f, "both layers move at the same speed on their planes; the parallax comes from their heights");
            Assert.AreEqual(nearZ, farZ, 1e-3f);
        }
    }
}
