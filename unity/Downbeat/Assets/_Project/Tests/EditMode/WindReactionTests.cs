using System;
using System.Collections.Generic;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The two grass reactions of the bottom row (the low string's long wave, the hand drum's ring) and what they need from the wind field: a wave that is wider than the others and a sweep that can run to the left.</summary>
    public class WindReactionTests
    {
        const float Width = 2.6f;                       // meadow.wind.waveWidth
        const float Tolerance = 1e-4f;
        const double SlotSec = 60.0 / 96.0 / 4.0;       // a quarter beat at 96 BPM: the finest the conducting play plays
        const int LowStringGapSlots = 2;                // song.partMinGapSlots of the low string and the hand drum (plan, track M)
        const int HandDrumGapSlots = 1;
        const int MarimbaGapSlots = 1;
        const double SimulatedSec = 30.0;

        static WindField Field() => new WindField(WindFieldGpu.Capacity, Width);

        static MeadowWindDto Shipped() => TestContent.LoadShipped().meadow.wind;

        // ---- what the wind field learned --------------------------------------------------------------------------------

        [Test]
        public void ASweepWithANegativeSpeed_RunsLeft_AndPushesLeft()
        {
            var field = Field();
            field.Add(new WindWave(0f, 0f, 0.0, 1f, -4f, 100f, WindWaveKind.Sweep, 0f));

            var front = field.SampleVector(-8f, 3f, 2.0);

            Assert.AreEqual(0.98f, -front.X, Tolerance, "after 2 s the front stands at x = -8 and pushes towards -x");
            Assert.AreEqual(0f, front.Z, 1e-6f);
            Assert.AreEqual(0f, field.SampleBend(8f, 3f, 2.0), 1e-3f, "and the other side stays calm");
        }

        [Test]
        public void AWiderWave_HasAWiderFront()
        {
            var plain = Field();
            plain.Add(new WindWave(0f, 5f, 0.0, 1f, 0f, 100f, WindWaveKind.Radial, 0f));
            var wide = Field();
            wide.Add(new WindWave(0f, 5f, 0.0, 1f, 0f, 100f, WindWaveKind.Radial, 0f, widthScale: 2f));

            const float Off = 3f;   // 3 m from the ring (a stationary ring at the origin: its front is the origin)
            Assert.AreEqual(Math.Exp(-Off * Off / (Width * Width)), plain.SampleBend(Off, 5f, 0.0), Tolerance);
            Assert.AreEqual(Math.Exp(-Off * Off / (4f * Width * Width)), wide.SampleBend(Off, 5f, 0.0), Tolerance, "twice the width: the exponent is a quarter of it");
            Assert.AreEqual(1f, wide.SampleBend(0f, 5f, 0.0), Tolerance, "the strength on the front itself is the amplitude, however wide it is");
        }

        [Test]
        public void TheWidthOfAWave_IsOne_UnlessSaid()
        {
            Assert.AreEqual(1f, new WindWave(0f, 0f, 0.0, 1f, 1f, 1f, WindWaveKind.Sweep, 0f).WidthScale);
        }

        [Test]
        public void AWaveWithoutWidth_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindWave(0f, 0f, 0.0, 1f, 1f, 1f, WindWaveKind.Sweep, 0f, widthScale: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindWave(0f, 0f, 0.0, 1f, 1f, 1f, WindWaveKind.Sweep, 0f, widthScale: -1f));
        }

        [Test]
        public void TheShaderGetsTheWidthOfTheWave()
        {
            var field = Field();
            field.Add(new WindWave(1f, 2f, 0.0, 0.5f, 3f, 5f, WindWaveKind.Radial, 0.2f, widthScale: 1.7f));
            var destination = new WindWaveGpu[2];

            Assert.AreEqual(1, field.CopyActive(1.0, destination));

            Assert.AreEqual(1.7f, destination[0].WidthScale, Tolerance);
        }

        // ---- the long wave of the low string ----------------------------------------------------------------------------

        [Test]
        public void TheLongWave_LaysTheGrassTowardsTheSideOfTheHand()
        {
            var settings = new WindLongWaveDto();
            var fromTheLeft = Field();
            fromTheLeft.Add(WindWaves.Long(new FieldPoint(-4f, 5f), 1f, 0.0, settings));
            var fromTheRight = Field();
            fromTheRight.Add(WindWaves.Long(new FieldPoint(4f, 5f), 1f, 0.0, settings));

            var left = fromTheLeft.SampleVector(-4f, 5f, 0.0);
            var right = fromTheRight.SampleVector(4f, 5f, 0.0);

            Assert.Less(left.X, -0.2f, "a hand on the left lays the grass towards the left edge");
            Assert.Greater(right.X, 0.2f, "a hand on the right, towards the right edge");
            Assert.AreEqual(0f, left.Z, 1e-6f, "along the field, not into the depth");
        }

        [Test]
        public void TheLongWave_RunsOutTowardsTheEdge_AndLeavesTheGrassBehindItCalm()
        {
            var settings = new WindLongWaveDto { lifeSec = 100f };   // a long life, so that the fade does not move the peak
            var field = Field();
            field.Add(WindWaves.Long(new FieldPoint(-2f, 5f), 0f, 0.0, settings));

            var travelled = settings.speed * 1.0f;
            var atTheFront = field.SampleBend(-2f - travelled, 5f, 1.0);
            var behindIt = field.SampleBend(-2f + 3f * Width * settings.widthScale, 5f, 1.0);

            Assert.Greater(atTheFront, 0.9f * settings.amplitude, "after a second the front stands speed metres further out");
            Assert.Less(behindIt, 0.02f * settings.amplitude, "the side it came from is calm again");
            Assert.AreEqual(field.SampleBend(-2f - travelled, 0.5f, 1.0), field.SampleBend(-2f - travelled, 12f, 1.0), Tolerance, "it is the same at every depth, like a sweep");
        }

        [Test]
        public void TheLongWave_IsLongerAndLowerThanTheRingOfANote()
        {
            var longWave = new WindLongWaveDto();
            var note = Shipped().noteRadial;
            var settings = new WindLongWaveDto { lifeSec = 100f };
            var field = Field();
            field.Add(WindWaves.Long(new FieldPoint(0f, 5f), 0f, 0.0, settings));
            var plainRing = Field();
            plainRing.Add(new WindWave(0f, 5f, 0.0, settings.amplitude, 0f, 100f, WindWaveKind.Radial, 0f));

            const float Off = 4f;
            Assert.Less(longWave.amplitude, note.amplitude, "low: softer than the ring of a note");
            Assert.Greater(field.SampleBend(Off, 5f, 0.0) / settings.amplitude, 0.5f, "long: 4 m from the front it still has more than half of its strength");
            Assert.Less(plainRing.SampleBend(Off, 5f, 0.0) / settings.amplitude, 0.2f, "where a wave of the usual width has hardly any");
        }

        [Test]
        public void ALouderHand_LaysTheLongWaveHarder()
        {
            var settings = new WindLongWaveDto();
            var soft = WindWaves.Long(new FieldPoint(-3f, 5f), 0f, 0.0, settings);
            var hard = WindWaves.Long(new FieldPoint(-3f, 5f), 1f, 0.0, settings);

            Assert.AreEqual(settings.amplitude, soft.Amplitude, Tolerance);
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), hard.Amplitude, Tolerance);
            Assert.AreEqual(hard.Amplitude, WindWaves.Long(new FieldPoint(-3f, 5f), 7f, 0.0, settings).Amplitude, Tolerance, "a velocity above 1 is 1");
        }

        [Test]
        public void TheLongWave_WithoutContent_UsesTheDefaults()
        {
            var fromNull = WindWaves.Long(new FieldPoint(-3f, 5f), 0.5f, 2.0, null);
            var fromDefaults = WindWaves.Long(new FieldPoint(-3f, 5f), 0.5f, 2.0, new WindLongWaveDto());

            Assert.AreEqual(fromDefaults.Amplitude, fromNull.Amplitude, Tolerance);
            Assert.AreEqual(fromDefaults.Speed, fromNull.Speed, Tolerance);
            Assert.AreEqual(fromDefaults.LifeSec, fromNull.LifeSec, Tolerance);
            Assert.AreEqual(2.0, fromNull.StartTime, "it starts when it is told to (the moment the note is heard)");
        }

        // ---- the ring of the hand drum ----------------------------------------------------------------------------------

        [Test]
        public void TheDrumRing_IsStrongerWiderAndQuickerThanTheRingOfANote()
        {
            var drum = new WindDrumRingDto();
            var note = Shipped().noteRadial;
            var loudDrum = WindWaves.Drum(new FieldPoint(0f, 5f), 1f, 0.0, drum);
            var loudNote = note.amplitude * (1f + note.velocityGain);

            Assert.Greater(loudDrum.Amplitude, 1.5f * loudNote, "strong: a hit on the drum lays the grass down much harder than a note of the marimba");
            Assert.Greater(loudDrum.WidthScale, 1.2f, "wide");
            Assert.Greater(loudDrum.Speed, note.speed, "and quick");
            Assert.Less(loudDrum.LifeSec, note.lifeSec, "and over sooner");
            Assert.AreEqual(WindWaveKind.Radial, loudDrum.Kind);
        }

        [Test]
        public void TheDrumRing_PushesAwayFromWhereTheDrumWasHit()
        {
            var field = Field();
            field.Add(WindWaves.Drum(new FieldPoint(1f, 5f), 1f, 0.0, new WindDrumRingDto { lifeSec = 100f }));
            var radius = new WindDrumRingDto().speed * 0.5f;   // after half a second

            Assert.Greater(field.SampleVector(1f + radius, 5f, 0.5).X, 0.3f, "to the right of the hit the grass goes right");
            Assert.Less(field.SampleVector(1f - radius, 5f, 0.5).X, -0.3f, "to the left, left");
            Assert.Greater(field.SampleVector(1f, 5f + radius, 0.5).Z, 0.3f, "behind it, away from the viewer");
        }

        [Test]
        public void TheDrumRing_WithoutContent_UsesTheDefaults()
        {
            var fromNull = WindWaves.Drum(new FieldPoint(0f, 5f), 0.5f, 1.0, null);
            var fromDefaults = WindWaves.Drum(new FieldPoint(0f, 5f), 0.5f, 1.0, new WindDrumRingDto());

            Assert.AreEqual(fromDefaults.Amplitude, fromNull.Amplitude, Tolerance);
            Assert.AreEqual(fromDefaults.WidthScale, fromNull.WidthScale, Tolerance);
        }

        // ---- the shipped numbers ----------------------------------------------------------------------------------------

        [Test]
        public void TheDtoDefaults_AreTheValuesOfTheShippedContent()
        {
            var shipped = Shipped();
            var longWave = new WindLongWaveDto();
            var drum = new WindDrumRingDto();

            Assert.IsNotNull(shipped.longWave, "meadow.wind.longWave is in the shipped content");
            Assert.IsNotNull(shipped.drumRing, "meadow.wind.drumRing is in the shipped content");
            Assert.AreEqual(longWave.amplitude, shipped.longWave.amplitude, 1e-6f, "tools/lib/content-build.mjs has the same defaults");
            Assert.AreEqual(longWave.velocityGain, shipped.longWave.velocityGain, 1e-6f);
            Assert.AreEqual(longWave.speed, shipped.longWave.speed, 1e-6f);
            Assert.AreEqual(longWave.lifeSec, shipped.longWave.lifeSec, 1e-6f);
            Assert.AreEqual(longWave.widthScale, shipped.longWave.widthScale, 1e-6f);
            Assert.AreEqual(drum.amplitude, shipped.drumRing.amplitude, 1e-6f);
            Assert.AreEqual(drum.velocityGain, shipped.drumRing.velocityGain, 1e-6f);
            Assert.AreEqual(drum.speed, shipped.drumRing.speed, 1e-6f);
            Assert.AreEqual(drum.lifeSec, shipped.drumRing.lifeSec, 1e-6f);
            Assert.AreEqual(drum.widthScale, shipped.drumRing.widthScale, 1e-6f);
        }

        [Test]
        public void AFrontPassesABladeSlowly_AsForEveryOtherWave()
        {
            // meadow.wind: a wave is over one blade for about 2 x width / speed seconds, and under half a second reads as a flick of the field
            var longWave = new WindLongWaveDto();
            var drum = new WindDrumRingDto();

            Assert.GreaterOrEqual(2f * Width * longWave.widthScale / longWave.speed, 0.5f);
            Assert.GreaterOrEqual(2f * Width * drum.widthScale / drum.speed, 0.5f);
        }

        // ---- the room in the wind field ---------------------------------------------------------------------------------

        sealed class Source
        {
            public string Name;
            public int GapSlots;
            public Func<int, double, WindWave> Make;   // (sequence number, start time) -> the wave; the sequence number is kept in the origin, so a dropped wave can be found
        }

        static Source LowString(WindLongWaveDto settings) => new Source
        {
            Name = "low string", GapSlots = LowStringGapSlots,
            Make = (n, t) => WithOriginZ(WindWaves.Long(new FieldPoint(-4f, 5f), 1f, t, settings), n),
        };

        static Source HandDrum(WindDrumRingDto settings) => new Source
        {
            Name = "hand drum", GapSlots = HandDrumGapSlots,
            Make = (n, t) => WithOriginZ(WindWaves.Drum(new FieldPoint(4f, 5f), 1f, t, settings), n),
        };

        static Source Marimba(WindRadialDto note) => new Source
        {
            Name = "marimba", GapSlots = MarimbaGapSlots,
            Make = (n, t) => new WindWave(0f, n, t, note.amplitude * (1f + note.velocityGain), note.speed, note.lifeSec, WindWaveKind.Radial, 0f),
        };

        static WindWave WithOriginZ(WindWave wave, int z) =>
            new WindWave(wave.OriginX, z, wave.StartTime, wave.Amplitude, wave.Speed, wave.LifeSec, wave.Kind, wave.Glow, wave.WidthScale);

        /// <summary>Every source plays a note on every gap-th slot for <see cref="SimulatedSec"/>. Returns the most waves alive at once and, for every wave that was dropped while it was still alive, how much of its life was left.</summary>
        static (int mostAlive, List<(string source, float lifeLeft)> dropped) Play(params Source[] sources)
        {
            var field = Field();
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            var owner = new Dictionary<int, Source>();
            var mostAlive = 0;
            var dropped = new List<(string, float)>();
            var number = 0;
            for (var slot = 0; slot * SlotSec < SimulatedSec; slot++)
            {
                var now = slot * SlotSec;
                foreach (var source in sources)
                {
                    if (slot % source.GapSlots != 0) continue;
                    var before = AliveNumbers(field, now, scratch, out var ageBefore, out var lifeBefore);
                    var wave = source.Make(++number, now);
                    owner[number] = source;
                    field.Add(wave);
                    var after = AliveNumbers(field, now, scratch, out _, out _);
                    mostAlive = Math.Max(mostAlive, after.Count);
                    foreach (var was in before)
                    {
                        if (after.Contains(was)) continue;
                        dropped.Add((owner[was].Name, 1f - ageBefore[was] / lifeBefore[was]));
                    }
                }
            }
            return (mostAlive, dropped);
        }

        static HashSet<int> AliveNumbers(WindField field, double now, WindWaveGpu[] scratch, out Dictionary<int, float> ageOf, out Dictionary<int, float> lifeOf)
        {
            var alive = new HashSet<int>();
            ageOf = new Dictionary<int, float>();
            lifeOf = new Dictionary<int, float>();
            var count = field.CopyActive(now, scratch);
            for (var i = 0; i < count; i++)
            {
                var number = (int)scratch[i].OriginZ;
                alive.Add(number);
                ageOf[number] = scratch[i].Age;
                lifeOf[number] = scratch[i].LifeSec;
            }
            return alive;
        }

        [Test]
        public void AtQuarterBeatDensity_TheTwoNewReactionsTogether_FitTheFieldWithoutDroppingALiveWave()
        {
            var (mostAlive, dropped) = Play(LowString(new WindLongWaveDto()), HandDrum(new WindDrumRingDto()));

            Assert.LessOrEqual(mostAlive, WindFieldGpu.Capacity, "never more than the field holds");
            Assert.IsEmpty(dropped, "so no wave is cut short: " + string.Join(", ", dropped));
            Assert.LessOrEqual(mostAlive, WindFieldGpu.Capacity - 4, "and at least four places stay free for the beat's sweeps and the notes' rings");
        }

        [Test]
        public void AtQuarterBeatDensity_OnEachOfTheSixParts_TheFieldHoldsAndTheNewWavesAreOnlyCutNearTheirEnd()
        {
            // The top row (harp, recorder, bell) makes no wave: its effects are in the air. The bottom row plays all three at its quickest, which two hands cannot do; it is the worst case.
            var settingsLong = new WindLongWaveDto();
            var settingsDrum = new WindDrumRingDto();

            var (mostAlive, dropped) = Play(LowString(settingsLong), Marimba(Shipped().noteRadial), HandDrum(settingsDrum));

            Assert.LessOrEqual(mostAlive, WindFieldGpu.Capacity, "the field never holds more than its capacity");
            foreach (var (source, lifeLeft) in dropped)
            {
                if (source == "marimba") continue;   // the ring of a note lives 2.6 s: three quick parts would drop it whatever the new waves do (it already does with two)
                Assert.LessOrEqual(lifeLeft, 0.35f, $"a wave of the {source} is dropped with {lifeLeft:0.00} of its life left; the oldest goes, and a new wave is that after about a second");
            }
        }

        [Test]
        public void TwoHands_OnTheBottomRow_NeverDropALiveWave_OfTheNewReactions()
        {
            // two hands: any two of the three parts at once
            var settingsLong = new WindLongWaveDto();
            var settingsDrum = new WindDrumRingDto();

            var (_, droppedWithMarimba) = Play(HandDrum(settingsDrum), Marimba(Shipped().noteRadial));
            var (_, droppedWithTheString) = Play(LowString(settingsLong), Marimba(Shipped().noteRadial));

            Assert.IsEmpty(droppedWithMarimba.FindAll(d => d.source == "hand drum"), "the drum's rings are not cut");
            Assert.IsEmpty(droppedWithTheString.FindAll(d => d.source == "low string"), "the long waves are not cut");
        }
    }
}
