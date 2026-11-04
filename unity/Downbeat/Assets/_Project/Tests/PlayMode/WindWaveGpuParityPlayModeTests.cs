using System;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// The waves of the wind are written twice: in C# (WindField, which WindFieldTests and WindReactionTests pin down) and in HLSL (MeadowWind.hlsl, which bends the grass). This draws the HLSL for samples through the
    /// probe shader and compares it with the C# field: the push (x and z), the strength and the glow, for every kind of wave there is (a plain sweep, a sweep to the left, a wide sweep, a plain ring, a wide ring,
    /// a wide glowing ring), at the fronts of the waves and away from them. The wind with the waves minus the same wind without them is what the waves did.
    /// </summary>
    public class WindWaveGpuParityPlayModeTests
    {
        const float Foot = 0.5f;
        const float Blade = 0.62f;
        const double Now = 0.9;
        const float Tolerance = 3e-3f;
        const int RandomSamples = 160;
        const int FrontSamples = 24;

        sealed class Case
        {
            public string Name;
            public WindWave Wave;
        }

        static ContentDto Shipped() =>
            JsonUtility.FromJson<ContentDto>(System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        static Case[] Cases(MeadowWindDto wind)
        {
            var longWave = WindWaves.Long(new FieldPoint(-3f, 6f), 1f, 0.2, wind.longWave);
            var longRight = WindWaves.Long(new FieldPoint(2.5f, 6f), 0.5f, 0.3, wind.longWave);
            var drum = WindWaves.Drum(new FieldPoint(3f, 5f), 1f, 0.35, wind.drumRing);
            return new[]
            {
                new Case { Name = "plain sweep", Wave = new WindWave(-9f, 0f, 0.0, 0.5f, 7f, 3.2f, WindWaveKind.Sweep, 0f) },
                new Case { Name = "sweep to the left", Wave = new WindWave(4f, 0f, 0.1, 0.6f, -6f, 3f, WindWaveKind.Sweep, 0f) },
                new Case { Name = "wide sweep", Wave = new WindWave(-4f, 0f, 0.15, 0.4f, 5f, 3f, WindWaveKind.Sweep, 0f, widthScale: 2.4f) },
                new Case { Name = "plain ring", Wave = new WindWave(-2f, 5f, 0.25, 0.3f, 4.5f, 2.6f, WindWaveKind.Radial, 0f) },
                new Case { Name = "wide glowing ring", Wave = new WindWave(1f, 7f, 0.4, 0.5f, 5f, 2.5f, WindWaveKind.Radial, 0.8f, widthScale: 2f) },
                new Case { Name = "long wave of the low string (left)", Wave = longWave },
                new Case { Name = "long wave of the low string (right)", Wave = longRight },
                new Case { Name = "ring of the hand drum", Wave = drum },
            };
        }

        sealed class Rig : IDisposable
        {
            readonly BreezeGpu _breeze;
            readonly BreezeField _field = new BreezeField();
            readonly WindFieldGpu _gpu;
            readonly Material _material;
            readonly Texture2D _input;
            readonly RenderTexture _target;
            readonly Texture2D _output;
            readonly int _samples;
            readonly Color[] _colours;

            public Rig(MeadowDto meadow, int samples)
            {
                _samples = samples;
                _breeze = new BreezeGpu(meadow.breeze, meadow.sky.clouds);
                _gpu = new WindFieldGpu(meadow.wind);
                var shader = Shader.Find("Hidden/Downbeat/WindProbe");
                Assert.IsNotNull(shader, "the probe shader is in the project");
                _material = new Material(shader);
                _input = new Texture2D(samples, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
                _target = RenderTexture.GetTemporary(samples, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _output = new Texture2D(samples, 1, TextureFormat.RGBAFloat, false, true);
                _colours = new Color[samples];
            }

            /// <summary>(push x, push z, strength, glow) of the whole wind of the grass with these waves at the places.</summary>
            public Vector4[] Read(WindField waves, (float x, float z)[] places)
            {
                _gpu.SetHand(0, false, 0f, 0f, 0f, 0f);
                _gpu.SetHand(1, false, 0f, 0f, 0f, 0f);
                _breeze.Upload(0.0);
                _field.Bake();
                _gpu.Upload(waves, Now);
                for (var i = 0; i < _samples; i++) _colours[i] = new Color(places[i].x, places[i].z, Foot, Blade);
                _input.SetPixels(_colours);
                _input.Apply();
                Shader.SetGlobalTexture("_ProbeIn", _input);
                var previous = RenderTexture.active;
                Graphics.Blit(null, _target, _material);
                RenderTexture.active = _target;
                _output.ReadPixels(new Rect(0, 0, _samples, 1), 0, 0);
                _output.Apply();
                RenderTexture.active = previous;
                var read = _output.GetPixels();
                var result = new Vector4[_samples];
                for (var i = 0; i < _samples; i++) result[i] = new Vector4(read[i].r, read[i].g, read[i].b, read[i].a);
                return result;
            }

            public void Dispose()
            {
                _field.Dispose();
                _gpu.Dispose();
                RenderTexture.ReleaseTemporary(_target);
                UnityEngine.Object.DestroyImmediate(_material);
                UnityEngine.Object.DestroyImmediate(_input);
                UnityEngine.Object.DestroyImmediate(_output);
            }
        }

        /// <summary>Random places over the field, and places on and near the front of the wave (where the strength is not nearly zero).</summary>
        static (float x, float z)[] PlacesFor(Case c, int count)
        {
            var rng = new System.Random(13);
            var places = new (float x, float z)[count];
            var age = (float)(Now - c.Wave.StartTime);
            var travelled = c.Wave.Speed * age;
            for (var i = 0; i < count; i++)
            {
                var z = (float)(rng.NextDouble() * 9 + 1);
                var offset = (float)(rng.NextDouble() * 6 - 3);
                if (c.Wave.Kind == WindWaveKind.Sweep)
                {
                    places[i] = (c.Wave.OriginX + travelled + offset, z);
                }
                else
                {
                    var angle = (float)(rng.NextDouble() * Math.PI * 2);
                    var radius = Mathf.Max(0.05f, travelled + offset);
                    places[i] = (c.Wave.OriginX + radius * Mathf.Cos(angle), c.Wave.OriginZ + radius * Mathf.Sin(angle));
                }
            }
            return places;
        }

        [Test]
        public void TheGpuWaves_MatchTheCpuField_ForEveryKindOfWave()
        {
            var meadow = Shipped().meadow;
            Shader.SetGlobalFloat(RenderLevers.SkyCheapId, 0f);   // the strong tier (a test that started the app at Low leaves the global at the weak tier's value)
            var rng = new System.Random(3);
            using (var rig = new Rig(meadow, FrontSamples + RandomSamples))
            {
                var checkedSamples = 0;
                foreach (var c in Cases(meadow.wind))
                {
                    var places = new (float x, float z)[FrontSamples + RandomSamples];
                    Array.Copy(PlacesFor(c, FrontSamples), places, FrontSamples);
                    for (var i = FrontSamples; i < places.Length; i++) places[i] = ((float)(rng.NextDouble() * 24 - 12), (float)(rng.NextDouble() * 12 - 1));

                    var cpu = new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth);
                    cpu.Add(c.Wave);
                    var without = rig.Read(new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth), places);
                    var with = rig.Read(cpu, places);

                    var strongest = 0f;
                    for (var i = 0; i < places.Length; i++)
                    {
                        var (x, z) = places[i];
                        var gpu = with[i] - without[i];
                        var push = cpu.SampleVector(x, z, Now);
                        var where = $"{c.Name} at ({x:0.00}, {z:0.00})";
                        Assert.AreEqual(push.X, gpu.x, Tolerance, where + ": push x");
                        Assert.AreEqual(push.Z, gpu.y, Tolerance, where + ": push z");
                        Assert.AreEqual(cpu.SampleBend(x, z, Now), gpu.z, Tolerance, where + ": strength");
                        Assert.AreEqual(cpu.SampleGlow(x, z, Now), gpu.w, Tolerance, where + ": glow");
                        strongest = Mathf.Max(strongest, cpu.SampleBend(x, z, Now));
                    }
                    Assert.Greater(strongest, 0.1f, c.Name + ": the samples do meet the wave (the test is not comparing zeros)");
                    checkedSamples += places.Length;
                }
                Assert.Greater(checkedSamples, 1000);
            }
        }

        [Test]
        public void SeveralWavesAtOnce_AddUpOnTheGpuAsOnTheCpu()
        {
            var meadow = Shipped().meadow;
            Shader.SetGlobalFloat(RenderLevers.SkyCheapId, 0f);
            var cpu = new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth);
            foreach (var c in Cases(meadow.wind)) cpu.Add(c.Wave);
            var rng = new System.Random(5);
            var places = new (float x, float z)[200];
            for (var i = 0; i < places.Length; i++) places[i] = ((float)(rng.NextDouble() * 16 - 8), (float)(rng.NextDouble() * 9 + 1));

            using (var rig = new Rig(meadow, places.Length))
            {
                var without = rig.Read(new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth), places);
                var with = rig.Read(cpu, places);
                for (var i = 0; i < places.Length; i++)
                {
                    var gpu = with[i] - without[i];
                    var push = cpu.SampleVector(places[i].x, places[i].z, Now);
                    Assert.AreEqual(push.X, gpu.x, Tolerance, $"push x at ({places[i].x:0.00}, {places[i].z:0.00})");
                    Assert.AreEqual(push.Z, gpu.y, Tolerance, $"push z at ({places[i].x:0.00}, {places[i].z:0.00})");
                    Assert.AreEqual(cpu.SampleBend(places[i].x, places[i].z, Now), gpu.z, Tolerance, "strength");
                }
            }
        }
    }
}
