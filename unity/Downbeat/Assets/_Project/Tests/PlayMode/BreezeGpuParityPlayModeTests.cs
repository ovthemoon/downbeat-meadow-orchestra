using System;
using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// The steady wind is written twice: in C# (BreezeModel, which the other tests pin down) and in HLSL (MeadowBreeze.hlsl, which draws the grass). The gusts and cloud shadows reach the grass through a
    /// baked field (BreezeField). This bakes it, draws the HLSL for samples through a probe shader and compares with the model: exactly at the middles of texels (and the three heights the field keeps),
    /// and within the error of the bilinear read anywhere else.
    /// </summary>
    public class BreezeGpuParityPlayModeTests
    {
        const int Samples = 512;
        const float BendTolerance = 1e-3f;
        const float GustTolerance = 2e-3f;
        const float ShadowTolerance = 3e-3f;
        const float OffCentreBendTolerance = 6e-3f;
        const float OffCentreGustTolerance = 4e-2f;
        const float OffCentreShadowTolerance = 4e-2f;
        const float TexelSize = BreezeField.SizeX / BreezeField.Width;

        static ContentDto Shipped() =>
            JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        [Test]
        public void TheGpuWind_MatchesTheCpuModel_AtTexelMiddles_AndCloseEverywhereElse()
        {
            var meadow = Shipped().meadow;
            var gpu = new BreezeGpu(meadow.breeze, meadow.sky.clouds);
            Shader.SetGlobalVector("_MeadowWindMute", Vector4.one);    // nothing muted (WindFieldGpu sets these in the running app)
            Shader.SetGlobalVector("_MeadowWindMute2", Vector4.one);
            var field = new BreezeField();
            var shader = Shader.Find("Hidden/Downbeat/BreezeProbe");
            Assert.IsNotNull(shader, "the probe shader is in the project");
            var material = new Material(shader);
            var input = new Texture2D(Samples, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var target = RenderTexture.GetTemporary(Samples, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var output = new Texture2D(Samples, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                var rng = new System.Random(21);
                var places = new (float x, float z, float h, float blade)[Samples];
                var heights = new[] { 0f, 0.5f, 1f };
                for (var i = 0; i < Samples / 2; i++)
                {
                    // the middle of a texel in the part of the grid where the meadow is, at one of the three heights the field keeps
                    var tx = rng.Next(40, BreezeField.Width - 40);
                    var tz = rng.Next(10, BreezeField.Height - 60);
                    places[i] = (BreezeField.OriginX + (tx + 0.5f) * TexelSize, BreezeField.OriginZ + (tz + 0.5f) * TexelSize, heights[i % 3], (float)rng.NextDouble());
                }
                for (var i = Samples / 2; i < Samples; i++)
                    places[i] = ((float)(rng.NextDouble() * 40 - 20), (float)(rng.NextDouble() * 60 - 2), (float)rng.NextDouble(), (float)rng.NextDouble());
                var colours = new Color[Samples];
                for (var i = 0; i < Samples; i++) colours[i] = new Color(places[i].x, places[i].z, places[i].h, places[i].blade);
                input.SetPixels(colours);
                input.Apply();
                Shader.SetGlobalTexture("_ProbeIn", input);

                var worstCentre = (bend: 0f, gust: 0f, shadow: 0f);
                var worstOff = (bend: 0f, gust: 0f, shadow: 0f);
                var largestBend = 0f;
                var shadowed = 0;
                foreach (var time in new[] { 0.0, 1.37, 8.9, 61.0, 733.3, 86400.0 * 3 + 0.25, 86400.0 * 400 + 11.5 })
                {
                    gpu.Upload(time);
                    field.Bake();
                    var previous = RenderTexture.active;
                    Graphics.Blit(null, target, material);
                    RenderTexture.active = target;
                    output.ReadPixels(new Rect(0, 0, Samples, 1), 0, 0);
                    output.Apply();
                    RenderTexture.active = previous;

                    var phase = gpu.Model.PhaseAt(time);
                    var read = output.GetPixels();
                    for (var i = 0; i < Samples; i++)
                    {
                        var (x, z, h, blade) = places[i];
                        var bend = gpu.Model.Bend(x, z, h, blade, phase);
                        var dBend = Mathf.Max(Mathf.Abs(bend.X - read[i].r), Mathf.Abs(bend.Z - read[i].g));
                        var dGust = Mathf.Abs(gpu.Model.Gust(x, z, h, phase) - read[i].b);
                        var dShadow = Mathf.Abs(gpu.Model.CloudShadow(x, z, phase) - read[i].a);
                        largestBend = Mathf.Max(largestBend, Mathf.Abs(bend.X), Mathf.Abs(bend.Z), Mathf.Abs(read[i].r), Mathf.Abs(read[i].g));
                        if (read[i].a > 0.5f) shadowed++;
                        if (i < Samples / 2) worstCentre = (Mathf.Max(worstCentre.bend, dBend), Mathf.Max(worstCentre.gust, dGust), Mathf.Max(worstCentre.shadow, dShadow));
                        else worstOff = (Mathf.Max(worstOff.bend, dBend), Mathf.Max(worstOff.gust, dGust), Mathf.Max(worstOff.shadow, dShadow));
                    }
                }

                Debug.Log($"[BreezeGpuParity] GPU (baked field) vs CPU model, {Samples / 2} texel middles + {Samples / 2} other places x 7 times: at texel middles bend {worstCentre.bend:0.000000} gust {worstCentre.gust:0.000000} shadow {worstCentre.shadow:0.000000}; elsewhere bend {worstOff.bend:0.000000} gust {worstOff.gust:0.000000} shadow {worstOff.shadow:0.000000}");
                Assert.Greater(largestBend, 0.1f, "the samples include a real wind (the test is not comparing zeros)");
                Assert.Greater(shadowed, 20, "and some of the samples lie in a cloud shadow");
                Assert.Less(worstCentre.bend, BendTolerance, "bend at texel middles");
                Assert.Less(worstCentre.gust, GustTolerance, "gust at texel middles");
                Assert.Less(worstCentre.shadow, ShadowTolerance, "cloud shadow at texel middles");
                Assert.Less(worstOff.bend, OffCentreBendTolerance, "bend elsewhere");
                Assert.Less(worstOff.gust, OffCentreGustTolerance, "gust elsewhere");
                Assert.Less(worstOff.shadow, OffCentreShadowTolerance, "cloud shadow elsewhere");
            }
            finally
            {
                field.Dispose();
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(input);
                UnityEngine.Object.DestroyImmediate(output);
            }
        }
    }
}
