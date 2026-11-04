using System;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>Where a line of sight meets rolling ground, also for lines that fall only a little per metre (near the horizon), where hills are as steep along the line as the line is.</summary>
    public class GroundRayTests
    {
        const float CameraHeight = 1.7f;
        const float GuessLevel = 0.6f;
        const float Epsilon = 1e-4f;

        // rolling ground with slopes up to about 0.24, as steep as a line of sight that falls 1 m in 4 m: three re-aims at the height of the last guess do not settle on it near the horizon
        static float Hills(float x, float z) => 0.6f + 0.25f * Mathf.Sin(z * 0.5f) * Mathf.Cos(x * 0.3f) + 0.1f * Mathf.Sin(1.1f * z + x);

        // one wide hill across the field at 12 m, 0.9 m high, over a plain at 0.3 m
        static float OneHill(float x, float z) => 0.3f + 0.9f * Mathf.Exp(-(z - 12f) * (z - 12f) / 8f);

        static float Flat(float x, float z) => 0.3f;

        static Ray Falling(float slope, float lateral = 0f) =>
            new Ray(new Vector3(0f, CameraHeight, 0f), new Vector3(lateral, -slope, 1f));

        static bool Meet(Ray ray, float above, float guessLevel, Func<float, float, float> height, out Vector3 point) =>
            GroundRay.TryMeet(ray, above, guessLevel, height, out point);

        [Test]
        public void OnRollingGround_ThePointFoundLiesOnTheSurface_ForEveryLineFromSteepToNearTheHorizon()
        {
            // Arrange: slopes 0.012 .. 0.309 (a line that falls 1.2 cm per metre meets the ground 100 m away), to both sides, for the ground itself and the ribbon 0.4 m over it
            var checkedLines = 0;
            for (var i = 0; i < 100; i++)
            {
                foreach (var lateral in new[] { -0.3f, 0f, 0.4f })
                {
                    foreach (var above in new[] { 0f, 0.4f })
                    {
                        var slope = 0.012f + 0.003f * i;

                        // Act
                        var found = Meet(Falling(slope, lateral), above, GuessLevel, Hills, out var point);

                        // Assert
                        Assert.IsTrue(found, $"slope {slope}, lateral {lateral}: the line meets the ground");
                        Assert.AreEqual(Hills(point.x, point.z) + above, point.y, GroundRay.Tolerance, $"slope {slope}, lateral {lateral}, above {above}: the point is on the surface");
                        checkedLines++;
                    }
                }
            }
            Assert.AreEqual(600, checkedLines);
        }

        [Test]
        public void ALineThatCrossesAHill_MeetsTheHillAndNotTheGroundBehindIt()
        {
            foreach (var slope in new[] { 0.06f, 0.08f, 0.1f, 0.12f })
            {
                // Act
                Assert.IsTrue(Meet(Falling(slope), 0f, GuessLevel, OneHill, out var point));

                // Assert: the front of the hill, 8 to 12 m out, not the plain it would reach beyond the hill
                Assert.That(point.z, Is.InRange(8f, 12f), $"slope {slope}");
                Assert.AreEqual(OneHill(point.x, point.z), point.y, GroundRay.Tolerance);
            }
        }

        [Test]
        public void ALineThatMissesTheHill_MeetsThePlain()
        {
            Assert.IsTrue(Meet(Falling(0.25f), 0f, GuessLevel, OneHill, out var point));

            Assert.AreEqual((CameraHeight - 0.3f) / 0.25f, point.z, 0.2f, "it is on the ground before the hill begins");
            Assert.AreEqual(0.3f, point.y, 0.05f);
        }

        [Test]
        public void TheSameLineOfSight_MeetsTheGroundAtTheSamePoint_WhateverWasGuessed()
        {
            // Arrange: the old way began at the height of the plane half way up the hills; here the guess only says how far to look
            var line = Falling(0.08f);

            // Act
            Meet(line, 0f, 0.1f, OneHill, out var low);
            Meet(line, 0f, 0.6f, OneHill, out var middle);
            Meet(line, 0f, 3f, OneHill, out var high);
            Meet(line, 0f, 0.6f, OneHill, out var again);

            // Assert
            Assert.AreEqual(middle.z, low.z, 2f * GroundRay.Tolerance);
            Assert.AreEqual(middle.z, high.z, 2f * GroundRay.Tolerance);
            Assert.AreEqual(middle, again, "and asking twice gives the same answer");
        }

        [Test]
        public void ALineOfSightThatMovesALittle_MeetsTheGroundALittleFurtherOn_NotAFrameAheadOfOrBehindIt()
        {
            // Arrange: a hand held still is seen by the tracker with a jitter of a ten-thousandth of the picture
            Meet(Falling(0.08f), 0f, GuessLevel, OneHill, out var before);

            foreach (var jitter in new[] { -2e-4f, -1e-4f, 1e-4f, 2e-4f })
            {
                // Act
                Meet(Falling(0.08f + jitter), 0f, GuessLevel, OneHill, out var after);

                // Assert
                Assert.Less(Vector3.Distance(before, after), 0.1f, $"jitter {jitter}");
            }
        }

        [Test]
        public void OnFlatGround_ThePointIsWhereThePlaneIs()
        {
            Assert.IsTrue(Meet(Falling(0.05f), 0.4f, GuessLevel, Flat, out var point));

            Assert.AreEqual(0.7f, point.y, GroundRay.Tolerance);
            Assert.AreEqual((CameraHeight - 0.7f) / 0.05f, point.z, 0.02f + Epsilon, "1 m of height at a fall of 1 in 20 is 20 m out");
        }

        [Test]
        public void ALineThatDoesNotFall_OrFallsLessThanAThousandth_NeverMeetsTheGround()
        {
            Assert.IsFalse(Meet(new Ray(new Vector3(0f, CameraHeight, 0f), Vector3.forward), 0f, GuessLevel, Hills, out _), "level");
            Assert.IsFalse(Meet(new Ray(new Vector3(0f, CameraHeight, 0f), new Vector3(0f, 0.2f, 1f)), 0f, GuessLevel, Hills, out _), "rising");
            Assert.IsFalse(Meet(Falling(0.0005f), 0f, GuessLevel, Hills, out _), "so shallow that it is the horizon");
        }

        [Test]
        public void ALineThatStartsUnderTheSurface_HasNoGroundToMeet()
        {
            var inTheGround = new Ray(new Vector3(0f, 0.1f, 0f), new Vector3(0f, -0.1f, 1f));

            Assert.IsFalse(Meet(inTheGround, 0f, GuessLevel, Flat, out _));
        }
    }
}
