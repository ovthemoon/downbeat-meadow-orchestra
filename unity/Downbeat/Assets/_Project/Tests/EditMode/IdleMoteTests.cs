using System;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Downbeat.Tests
{
    /// <summary>The slow motes of the record screen (item 11 of the polish list): fireflies and petals that drift behind the panels while nobody plays.</summary>
    public class IdleMoteTests
    {
        const int Count = 24;
        const uint Seed = 7u;
        const float Speed = 0.02f;
        const float Peak = 0.4f;
        const float Dimmed = 0.3f;
        const float Aspect = 16f / 9f;
        const double Frame = 1.0 / 60.0;

        /// <summary>A few percent of the screen height per second: the fastest a mote may ever move (drift, sway and the glide together).</summary>
        const float MaxSpeedPerSecond = 0.06f;
        /// <summary>Two pictures one frame apart: no mote moves more than about a pixel of 1080 (0.001 of the height).</summary>
        const float MaxStepPerFrame = MaxSpeedPerSecond * (float)Frame;
        /// <summary>Less than half a percent of full brightness per frame: about one 8-bit level, which no eye reads as a flicker.</summary>
        const float MaxAlphaStepPerFrame = 0.005f;
        const float MaxAngleStepPerFrame = 0.004f;

        static IdleMoteField Field(int count = Count, uint seed = Seed, float speed = Speed) => new IdleMoteField(count, seed, speed, Peak, Dimmed);

        /// <summary>The smaller turn between two angles (a whole turn looks the same).</summary>
        static float TurnBetween(float a, float b)
        {
            var twoPi = 2f * (float)Math.PI;
            var d = (b - a) % twoPi;
            if (d > Math.PI) d -= twoPi;
            if (d < -Math.PI) d += twoPi;
            return Math.Abs(d);
        }

        static MoteState[] Evaluate(IdleMoteField field, double clock, float aspect = Aspect)
        {
            var states = new MoteState[field.Count];
            field.Evaluate(clock, aspect, states);
            return states;
        }

        [Test]
        public void TheSameSeed_GivesTheSameMotes_AndAnotherSeedGivesOtherOnes()
        {
            var a = Evaluate(Field(), 123.4);
            var b = Evaluate(Field(), 123.4);
            var other = Evaluate(Field(seed: 8u), 123.4);

            for (var i = 0; i < Count; i++)
            {
                Assert.AreEqual(a[i].X, b[i].X, $"mote {i} x");
                Assert.AreEqual(a[i].Y, b[i].Y, $"mote {i} y");
                Assert.AreEqual(a[i].Alpha, b[i].Alpha, $"mote {i} alpha");
                Assert.AreEqual(a[i].Kind, b[i].Kind);
            }
            var different = 0;
            for (var i = 0; i < Count; i++) if (Math.Abs(a[i].X - other[i].X) > 0.01f || Math.Abs(a[i].Y - other[i].Y) > 0.01f) different++;
            Assert.Greater(different, Count / 2, "another seed puts most motes somewhere else");
        }

        [Test]
        public void EveryMote_StaysOnTheScreen_AtEveryTime_AndEveryShapeOfScreen()
        {
            foreach (var aspect in new[] { 16f / 9f, 4f / 3f, 1f, 21f / 9f })
            {
                var field = Field();
                var states = new MoteState[Count];
                for (var t = 0.0; t < 900.0; t += 0.37)
                {
                    field.Evaluate(t, aspect, states);
                    for (var i = 0; i < Count; i++)
                    {
                        var s = states[i];
                        var half = s.Size * 0.5f;
                        Assert.GreaterOrEqual(s.X - half, 0f, $"aspect {aspect:0.00}, t {t:0.0}, mote {i}: left edge");
                        Assert.LessOrEqual(s.X + half, aspect, $"aspect {aspect:0.00}, t {t:0.0}, mote {i}: right edge");
                        Assert.GreaterOrEqual(s.Y - half, 0f, $"aspect {aspect:0.00}, t {t:0.0}, mote {i}: bottom edge");
                        Assert.LessOrEqual(s.Y + half, 1f, $"aspect {aspect:0.00}, t {t:0.0}, mote {i}: top edge");
                        Assert.That(s.Size, Is.InRange(0.01f, 0.06f), "small");
                    }
                }
            }
        }

        [Test]
        public void NoMoteMovesFasterThanAFewPercentOfTheHeightPerSecond_ButTheyDoMove()
        {
            var field = Field();
            var before = new MoteState[Count];
            var after = new MoteState[Count];
            double sum = 0;
            var samples = 0;
            var fastest = 0f;
            field.Evaluate(0.0, Aspect, before);
            for (var step = 1; step <= 60 * 240; step++)
            {
                field.Evaluate(step * Frame, Aspect, after);
                for (var i = 0; i < Count; i++)
                {
                    if (before[i].Alpha < 0.001f || after[i].Alpha < 0.001f) continue;   // a mote that is not there is moved to its next place unseen
                    var dx = after[i].X - before[i].X;
                    var dy = after[i].Y - before[i].Y;
                    var speed = (float)(Math.Sqrt(dx * dx + dy * dy) / Frame);
                    fastest = Math.Max(fastest, speed);
                    sum += speed;
                    samples++;
                }
                Array.Copy(after, before, Count);
            }

            Assert.Greater(samples, 1000, "most of the time most motes are there");
            Assert.LessOrEqual(fastest, MaxSpeedPerSecond, "no mote is ever faster than 6 % of the screen height per second");
            Assert.Greater(sum / samples, 0.005, "but a mote that is there does drift (more than 0.5 % of the height per second on average)");
        }

        [Test]
        public void TwoNeighbouringFrames_DifferVeryLittle_InPlace_Brightness_AndTurn()
        {
            var field = Field();
            var a = new MoteState[Count];
            var b = new MoteState[Count];
            var worstMove = 0f;
            var worstAlpha = 0f;
            var worstAngle = 0f;
            for (var step = 0; step < 60 * 300; step++)
            {
                field.Evaluate(step * Frame, Aspect, a);
                field.Evaluate((step + 1) * Frame, Aspect, b);
                for (var i = 0; i < Count; i++)
                {
                    if (a[i].Alpha >= 0.001f && b[i].Alpha >= 0.001f)
                        worstMove = Math.Max(worstMove, (float)Math.Sqrt((b[i].X - a[i].X) * (b[i].X - a[i].X) + (b[i].Y - a[i].Y) * (b[i].Y - a[i].Y)));
                    worstAlpha = Math.Max(worstAlpha, Math.Abs(b[i].Alpha - a[i].Alpha));
                    worstAngle = Math.Max(worstAngle, TurnBetween(a[i].Angle, b[i].Angle));
                    Assert.AreEqual(a[i].Size, b[i].Size, 1e-6f, "a mote keeps its size");
                    Assert.AreEqual(a[i].Kind, b[i].Kind);
                }
            }

            Assert.LessOrEqual(worstMove, MaxStepPerFrame, "no jump of place from one frame to the next");
            Assert.LessOrEqual(worstAlpha, MaxAlphaStepPerFrame, "no flicker: the brightness changes by less than half a percent per frame");
            Assert.LessOrEqual(worstAngle, MaxAngleStepPerFrame, "a petal turns slowly");
        }

        [Test]
        public void AfterDaysOfUptime_TheMotesStillMoveInEvenSteps()
        {
            var field = Field();
            var a = new MoteState[Count];
            var b = new MoteState[Count];
            foreach (var days in new[] { 3.0, 30.0, 3000.0 })
            {
                var start = days * 86400.0 + 0.123;
                for (var step = 0; step < 600; step++)
                {
                    field.Evaluate(start + step * Frame, Aspect, a);
                    field.Evaluate(start + (step + 1) * Frame, Aspect, b);
                    for (var i = 0; i < Count; i++)
                    {
                        var dx = b[i].X - a[i].X;
                        var dy = b[i].Y - a[i].Y;
                        var hidden = a[i].Alpha < 0.001f || b[i].Alpha < 0.001f;   // a mote that is not there is moved to its next place unseen
                        if (!hidden) Assert.LessOrEqual(Math.Sqrt(dx * dx + dy * dy), MaxStepPerFrame, $"{days} days, mote {i}: even steps (time is wrapped in double precision before it becomes a float)");
                        Assert.LessOrEqual(Math.Abs(b[i].Alpha - a[i].Alpha), MaxAlphaStepPerFrame, $"{days} days, mote {i}: brightness");
                    }
                }
            }
        }

        [Test]
        public void TheLifeOfAMote_FadesInAndOutSoftly_AtItsEnds()
        {
            Assert.AreEqual(0f, IdleMoteField.LifeEnvelope(0f), 1e-6f, "starts invisible");
            Assert.AreEqual(0f, IdleMoteField.LifeEnvelope(1f), 1e-6f, "ends invisible");
            Assert.AreEqual(1f, IdleMoteField.LifeEnvelope(0.5f), 1e-6f, "full in the middle of its life");
            Assert.AreEqual(1f, IdleMoteField.LifeEnvelope(IdleMoteField.FadeFraction), 1e-6f, "the fade-in is over at FadeFraction");
            Assert.AreEqual(0.5f, IdleMoteField.LifeEnvelope(IdleMoteField.FadeFraction * 0.5f), 1e-6f, "halfway through the fade-in is half bright (smooth step)");
            var previous = 0f;
            for (var u = 0f; u <= IdleMoteField.FadeFraction; u += 0.005f)
            {
                var value = IdleMoteField.LifeEnvelope(u);
                Assert.GreaterOrEqual(value, previous - 1e-6f, "never dips while it fades in");
                Assert.AreEqual(value, IdleMoteField.LifeEnvelope(1f - u), 1e-5f, "the fade-out mirrors the fade-in");
                previous = value;
            }
            Assert.Greater(IdleMoteField.FadeFraction, 0.1f, "a fade of at least a tenth of a life each side (seconds, not frames)");
        }

        [Test]
        public void EveryMote_IsInvisibleAtSomeTime_NeverBrighterThanThePeak_AndIsShownAtItsPeakAtOtherTimes()
        {
            var field = Field();
            var states = new MoteState[Count];
            var minAlpha = new float[Count];
            var maxAlpha = new float[Count];
            for (var i = 0; i < Count; i++) minAlpha[i] = float.MaxValue;
            for (var t = 0.0; t < 400.0; t += 0.05)
            {
                field.Evaluate(t, Aspect, states);
                for (var i = 0; i < Count; i++)
                {
                    Assert.That(states[i].Alpha, Is.InRange(0f, Peak + 1e-6f), $"mote {i} at {t:0.00}");
                    minAlpha[i] = Math.Min(minAlpha[i], states[i].Alpha);
                    maxAlpha[i] = Math.Max(maxAlpha[i], states[i].Alpha);
                }
            }
            for (var i = 0; i < Count; i++)
            {
                Assert.Less(minAlpha[i], 0.002f, $"mote {i} fades out completely at the end of a life (so it can appear somewhere else unseen)");
                Assert.Greater(maxAlpha[i], Peak * 0.75f, $"mote {i} is clearly there in the middle of its life");
            }
        }

        [Test]
        public void SomeMotesArePetals_InAllThreeFlowerColours_AndTheRestAreFireflies()
        {
            var states = Evaluate(Field(), 10.0);

            var petals = 0;
            var variants = new bool[3];
            foreach (var s in states)
            {
                if (s.Kind == MoteKind.Petal) { petals++; variants[s.Variant] = true; }
                else Assert.AreEqual(0, s.Variant);
            }
            Assert.That(petals, Is.InRange(Count / 6, Count / 2), "a few petals among the fireflies");
            CollectionAssert.AreEqual(new[] { true, true, true }, variants, "bellflower, dandelion and poppy colours all appear");
            Assert.AreEqual(Count, states.Length);
            Assert.IsTrue(Array.Exists(states, s => s.Kind == MoteKind.Firefly), "and fireflies");
        }

        [Test]
        public void TheFirefliesRise_AndThePetalsFall()
        {
            var field = Field();
            var a = Evaluate(field, 50.0);
            var b = Evaluate(field, 50.5);

            for (var i = 0; i < Count; i++)
            {
                if (a[i].Alpha < 0.01f || b[i].Alpha < 0.01f) continue;
                if (Math.Abs(b[i].Y - a[i].Y) > 0.05f) continue;       // wrapped to a new life in between
                if (a[i].Kind == MoteKind.Firefly) Assert.GreaterOrEqual(b[i].Y, a[i].Y - 0.004f, $"firefly {i} rises ");
            }
            var risen = 0;
            var fallen = 0;
            for (var t = 0.0; t < 60.0; t += 1.0)
            {
                var p = Evaluate(field, t);
                var q = Evaluate(field, t + 1.0);
                for (var i = 0; i < Count; i++)
                {
                    if (p[i].Alpha < 0.01f || q[i].Alpha < 0.01f || Math.Abs(q[i].Y - p[i].Y) > 0.1f) continue;
                    if (p[i].Kind == MoteKind.Firefly && q[i].Y > p[i].Y) risen++;
                    if (p[i].Kind == MoteKind.Petal && q[i].Y < p[i].Y) fallen++;
                }
            }
            Assert.Greater(risen, 100);
            Assert.Greater(fallen, 20);
        }

        [Test]
        public void WhilePerforming_TheMotesFadeDownToTheLevel_AndComeBackWhenTheMeadowIsIdleAgain()
        {
            var field = Field();
            field.Advance(0.0, false);
            Assert.AreEqual(1f, field.Level, 1e-6f, "idle: full");

            var t = 0.0;
            var previous = field.Level;
            var biggestStep = 0f;
            for (; t < 6.0; t += 0.016)
            {
                field.Advance(t, true);
                biggestStep = Math.Max(biggestStep, previous - field.Level);
                Assert.LessOrEqual(field.Level, previous + 1e-6f, "only goes down while performing");
                previous = field.Level;
            }
            Assert.AreEqual(Dimmed, field.Level, 1e-4f, "a hand is in: the motes keep 30 % of their brightness");
            Assert.Less(biggestStep, 0.03f, "and get there smoothly (no step bigger than 3 % in a frame)");

            var up = field.Level;
            for (t = 6.0; t < 16.0; t += 0.016)
            {
                field.Advance(t, false);
                Assert.GreaterOrEqual(field.Level, up - 1e-6f, "only goes up when idle");
                up = field.Level;
            }
            Assert.AreEqual(1f, field.Level, 1e-4f, "the meadow is idle again: they come back");
        }

        [Test]
        public void TheLevelScalesTheBrightnessOfEveryMote()
        {
            var idle = Field();
            var performing = Field();
            performing.Advance(0.0, true);
            for (var t = 0.0; t < 10.0; t += 0.05) performing.Advance(t, true);
            idle.Advance(10.0, false);
            var a = Evaluate(idle, 10.0);
            var b = Evaluate(performing, 10.0);

            var compared = 0;
            for (var i = 0; i < Count; i++)
            {
                if (a[i].Alpha < 0.05f) continue;
                Assert.AreEqual(Dimmed, b[i].Alpha / a[i].Alpha, 0.01f, $"mote {i}");
                Assert.AreEqual(a[i].X, b[i].X, 1e-6f, "dimming does not move them");
                compared++;
            }
            Assert.Greater(compared, 5);
        }

        [Test]
        public void ALongPause_DoesNotMakeTheLevelJump()
        {
            var field = Field();
            field.Advance(0.0, false);
            field.Advance(600.0, true);   // the process was suspended for ten minutes

            Assert.Greater(field.Level, 0.9f, "a stall of one frame is not a fade of ten minutes");
        }

        [Test]
        public void NoMotes_IsAllowed_AndTooManyOrNonsenseIsRefused()
        {
            Assert.AreEqual(0, new IdleMoteField(0, Seed, Speed, Peak, Dimmed).Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleMoteField(IdleMoteField.MaxCount + 1, Seed, Speed, Peak, Dimmed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleMoteField(-1, Seed, Speed, Peak, Dimmed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleMoteField(4, Seed, 0f, Peak, Dimmed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleMoteField(4, Seed, Speed, 1.5f, Dimmed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleMoteField(4, Seed, Speed, Peak, -0.1f));
            Assert.Throws<ArgumentException>(() => Field().Evaluate(0.0, Aspect, new MoteState[3]), "the caller's array must be big enough");
        }

        [Test]
        public void AFrameOfMotes_AllocatesNothing()
        {
            var field = Field();
            var states = new MoteState[Count];
            for (var i = 0; i < 50; i++) { field.Advance(i * Frame, i % 2 == 0); field.Evaluate(i * Frame, Aspect, states); }   // warm up

            // GC.GetAllocatedBytesForCurrentThread() counts nothing in the editor's Mono (it always says 0): the engine's own count of GC allocations is used
            Assert.That(() =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    field.Advance(100.0 + i * Frame, i % 500 < 250);
                    field.Evaluate(100.0 + i * Frame, Aspect, states);
                }
            }, Is.Not.AllocatingGCMemory(), "2000 frames of 24 motes: no garbage");
        }
    }
}
