using System;
using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// The wind of a hand, as the grass shader draws it (MeadowWind.hlsl), read back through a probe shader: the whole wind at a point with the hand there, minus the same point with no hand.
    /// A hand is a soft local gust: the grass is parted around it and nothing is pushed at the very spot of the hand, the push is feathered to nothing at the radius, and a moving hand
    /// leans the grass its way with the swing back behind it that the breeze's gusts have.
    /// </summary>
    public class HandWindPlayModeTests
    {
        const float HandX = 0f;
        const float HandZ = 4f;
        const float Foot = 0.5f;          // the height up the blade of the probe: the middle
        const float StiffBlade = 0.62f;   // two blades with different stiffness (MeadowStiffness: frac(phase * 7.31 + 0.5))
        const float SoftBlade = 0.11f;
        const float Exact = 1e-5f;

        [SetUp]
        public void StrongTier()
        {
            // the global is left at the weak tier's value by any test that started the app at Low: these tests draw the full wind unless they say otherwise
            Shader.SetGlobalFloat(RenderLevers.SkyCheapId, 0f);
        }

        sealed class Rig : IDisposable
        {
            readonly BreezeGpu _breeze;
            readonly BreezeField _field = new BreezeField();
            readonly WindFieldGpu _wind;
            readonly WindField _waves;
            readonly Material _material;
            readonly Texture2D _input;
            readonly RenderTexture _target;
            readonly Texture2D _output;
            readonly int _samples;
            Vector3[] _places;

            public MeadowWindDto Settings { get; }
            /// <summary>How much later the tip of a blade meets a gust than its foot (the breeze's lagMetres): the probe reads the middle of the blade.</summary>
            public float GustLag { get; }

            public Rig(int samples)
            {
                var meadow = JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json")))).meadow;
                Settings = meadow.wind;
                GustLag = meadow.breeze.gust.lagMetres;
                _samples = samples;
                _breeze = new BreezeGpu(meadow.breeze, meadow.sky.clouds);
                _wind = new WindFieldGpu(meadow.wind);
                _waves = new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth);   // no waves: only the breeze and the hands
                var shader = Shader.Find("Hidden/Downbeat/WindProbe");
                Assert.IsNotNull(shader, "the probe shader is in the project");
                _material = new Material(shader);
                _input = new Texture2D(samples, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
                _target = RenderTexture.GetTemporary(samples, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _output = new Texture2D(samples, 1, TextureFormat.RGBAFloat, false, true);
            }

            /// <summary>Where to read the wind: offsets from the hand (x, z) and the number of the blade.</summary>
            public Vector2[] Offsets;
            public float Blade = StiffBlade;

            /// <summary>The wind at the offsets with one hand at the place and moving at (vx, vz); the wind without the hand is taken off, which leaves the hand's own part.</summary>
            public Vector2[] HandPart(float vx, float vz)
            {
                var without = Run(false, 0f, 0f);
                var with = Run(true, vx, vz);
                var part = new Vector2[_samples];
                for (var i = 0; i < _samples; i++) part[i] = with[i] - without[i];
                return part;
            }

            Vector2[] Run(bool hand, float vx, float vz)
            {
                _wind.SetHand(0, hand, HandX, HandZ, vx, vz);
                _breeze.Upload(0.0);
                _field.Bake();
                _wind.Upload(_waves, 0.0);
                var colours = new Color[_samples];
                for (var i = 0; i < _samples; i++) colours[i] = new Color(HandX + Offsets[i].x, HandZ + Offsets[i].y, Foot, Blade);
                _input.SetPixels(colours);
                _input.Apply();
                Shader.SetGlobalTexture("_ProbeIn", _input);
                var previous = RenderTexture.active;
                Graphics.Blit(null, _target, _material);
                RenderTexture.active = _target;
                _output.ReadPixels(new Rect(0, 0, _samples, 1), 0, 0);
                _output.Apply();
                RenderTexture.active = previous;
                var read = _output.GetPixels();
                var bends = new Vector2[_samples];
                for (var i = 0; i < _samples; i++) bends[i] = new Vector2(read[i].r, read[i].g);
                return bends;
            }

            public void Dispose()
            {
                _field.Dispose();
                _wind.Dispose();
                RenderTexture.ReleaseTemporary(_target);
                UnityEngine.Object.DestroyImmediate(_material);
                UnityEngine.Object.DestroyImmediate(_input);
                UnityEngine.Object.DestroyImmediate(_output);
            }
        }

        [Test]
        public void TheGrassIsClearWhereAStillHandIs()
        {
            using (var rig = new Rig(3) { Offsets = new[] { Vector2.zero, new Vector2(0.01f, 0f), new Vector2(0f, -0.01f) } })
            {
                var part = rig.HandPart(0f, 0f);

                Assert.Less(part[0].magnitude, 1e-4f, "nothing pushes the grass at the very spot of the hand");
                Assert.Less(part[1].magnitude, 0.02f, "and next to it hardly");
                Assert.Less(part[2].magnitude, 0.02f);
            }
        }

        [Test]
        public void AStillHandPartsTheGrassAwayFromItself_AndNothingReachesBeyondItsRadius()
        {
            using (var rig = new Rig(8))
            {
                var r = rig.Settings.handRadius;
                rig.Offsets = new[]
                {
                    new Vector2(0.45f * r, 0f), new Vector2(-0.45f * r, 0f), new Vector2(0f, 0.45f * r), new Vector2(0f, -0.45f * r),
                    new Vector2(r * 1.001f, 0f), new Vector2(-1.5f * r, 0f), new Vector2(0f, 2f * r), new Vector2(0.9f * r, 0.9f * r),
                };

                var part = rig.HandPart(0f, 0f);

                Assert.Greater(part[0].x, 0.25f * rig.Settings.handPush, "to the right of the hand the grass is pushed right");
                Assert.Less(part[1].x, -0.25f * rig.Settings.handPush, "to the left, left");
                Assert.Greater(part[2].y, 0.25f * rig.Settings.handPush, "beyond it, away from the camera");
                Assert.Less(part[3].y, -0.25f * rig.Settings.handPush, "and nearer to it, toward the camera");
                Assert.Less(Mathf.Abs(part[0].y), 0.02f, "straight out from the hand, not sideways");
                for (var i = 4; i < 8; i++) Assert.Less(part[i].magnitude, Exact, $"nothing at {rig.Offsets[i]}: the hand's wind has ended and the breeze goes on");
            }
        }

        [Test]
        public void TheHandsWindIsWideAndFeathered_StillStrongNearTheRimAndSmoothAllTheWayOut()
        {
            const int Steps = 400;
            using (var rig = new Rig(Steps))
            {
                var r = rig.Settings.handRadius;
                rig.Offsets = new Vector2[Steps];
                for (var i = 0; i < Steps; i++) rig.Offsets[i] = new Vector2(1.2f * r * i / (Steps - 1), 0f);

                var part = rig.HandPart(0f, 0f);

                var peak = 0f;
                var biggestStep = 0f;
                for (var i = 0; i < Steps; i++)
                {
                    peak = Mathf.Max(peak, part[i].x);
                    if (i > 0) biggestStep = Mathf.Max(biggestStep, Mathf.Abs(part[i].x - part[i - 1].x));
                }
                var nearRim = part[Mathf.RoundToInt(0.8f / 1.2f * (Steps - 1))].x;

                Assert.Greater(peak, 0.8f * rig.Settings.handPush, "the parting is as strong as the content says");
                Assert.Greater(nearRim, 0.3f * peak, "at 0.8 of the radius the push has not died away yet: the feather is wide");
                Assert.Less(biggestStep, 0.01f, "and it goes to nothing without a step");
            }
        }

        [Test]
        public void AMovingHandLeansTheGrassItsWay_WithASwingBackBehindIt()
        {
            using (var rig = new Rig(6))
            {
                var w = rig.Settings.handGustWidth;
                rig.Offsets = new[]
                {
                    new Vector2(0.4f, 0f),                 // just ahead of the hand
                    new Vector2(0f, 0f),                   // at the hand
                    new Vector2(-0.8f * w, 0f),            // behind it, where the body of the gust trails
                    new Vector2(-(1.9f * w + rig.GustLag * Foot), 0f), // where the grass swings back (the middle of a blade meets a gust a little later than its foot)
                    new Vector2(0f, 1.2f * w),             // beside the path
                    new Vector2(2f * rig.Settings.handRadius, 0f),
                };
                const float Speed = 2f;

                var still = rig.HandPart(0f, 0f);
                var moving = rig.HandPart(Speed, 0f);
                var lean = new Vector2[6];
                for (var i = 0; i < 6; i++) lean[i] = moving[i] - still[i];

                Assert.Greater(lean[0].x, 0.03f, "ahead of the hand the grass starts to lean the way the hand goes");
                Assert.Greater(lean[1].x, 0.1f, "and at the hand");
                Assert.Greater(lean[2].x, 0.03f, "the body of the gust trails behind the hand");
                Assert.Less(lean[3].x, -0.003f, "farther back it swings back past upright, as behind the breeze's gusts");
                Assert.Less(Mathf.Abs(lean[0].y), 0.02f, "along the path, not sideways");
                Assert.Greater(lean[4].x, 0f, "beside the path it leans too, less");
                Assert.Less(lean[4].x, lean[1].x, "but less than on the path");
                Assert.Less(lean[5].magnitude, Exact, "and nothing beyond the radius");
            }
        }

        [Test]
        public void AHandThatHardlyMovesHasNoDirection_SoItsLeanFadesInsteadOfFlipping()
        {
            using (var rig = new Rig(2) { Offsets = new[] { new Vector2(0.3f, 0.2f), new Vector2(-0.4f, 0.1f) } })
            {
                var still = rig.HandPart(0f, 0f);
                var right = rig.HandPart(0.03f, 0f);
                var up = rig.HandPart(0f, 0.03f);

                for (var i = 0; i < 2; i++)
                {
                    Assert.Less((right[i] - still[i]).magnitude, 0.003f, "a creeping hand barely leans the grass");
                    Assert.Less((up[i] - still[i]).magnitude, 0.003f);
                }
            }
        }

        [Test]
        public void TheLeanIsCappedAtTheFastestHand()
        {
            using (var rig = new Rig(1) { Offsets = new[] { new Vector2(0.4f, 0f) } })
            {
                var cap = rig.Settings.handSpeedCap;
                var still = rig.HandPart(0f, 0f)[0];
                var atCap = rig.HandPart(cap, 0f)[0] - still;
                var tenTimes = rig.HandPart(10f * cap, 0f)[0] - still;

                Assert.Greater(atCap.x, 0.05f);
                Assert.AreEqual(atCap.x, tenTimes.x, 1e-4f, "a faster hand does not blow harder: the lean stays bounded");
            }
        }

        [Test]
        public void BladesDifferInHowFarTheyGive()
        {
            Vector2 stiff;
            Vector2 soft;
            using (var rig = new Rig(1) { Offsets = new[] { new Vector2(0.45f * 3.4f, 0f) }, Blade = StiffBlade }) stiff = rig.HandPart(1f, 0f)[0];
            using (var rig = new Rig(1) { Offsets = new[] { new Vector2(0.45f * 3.4f, 0f) }, Blade = SoftBlade }) soft = rig.HandPart(1f, 0f)[0];

            Assert.Greater(Mathf.Abs(stiff.x - soft.x), 0.03f * Mathf.Abs(stiff.x), "as in the breeze, the blades are not all bent the same");
        }

        [Test]
        public void AtAWeakTier_TheHandStillPartsAndLeansTheGrass_WithoutTheSwingBack()
        {
            var cheap = Shader.PropertyToID("_MeadowSkyCheap");
            try
            {
                Shader.SetGlobalFloat(cheap, 1f);
                using (var rig = new Rig(3))
                {
                    var w = rig.Settings.handGustWidth;
                    rig.Offsets = new[] { new Vector2(0.45f * rig.Settings.handRadius, 0f), new Vector2(0.3f, 0f), new Vector2(-(1.9f * w + rig.GustLag * Foot), 0f) };
                    var still = rig.HandPart(0f, 0f);
                    var moving = rig.HandPart(2f, 0f);

                    Assert.Greater(still[0].x, 0.25f * rig.Settings.handPush, "the parting is the same");
                    Assert.Greater((moving[1] - still[1]).x, 0.05f, "the lean is there");
                    Assert.GreaterOrEqual((moving[2] - still[2]).x, 0f, "but nothing swings back behind the hand");
                }
            }
            finally
            {
                Shader.SetGlobalFloat(cheap, 0f);
            }
        }
    }
}
