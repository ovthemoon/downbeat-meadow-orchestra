using System;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// The bend of a blade is written twice: in C# (BladeBend, which BladeBendTests pins down: length, direction, no NaN, the resting look) and in HLSL (MeadowBladeCurve in MeadowWind.hlsl, which bends the grass
    /// and the flower stems). This draws the HLSL for many pushes, heights up the blade and maximum bends through a probe shader and compares point and direction with the model.
    /// </summary>
    public class BladeBendGpuParityPlayModeTests
    {
        const float Tolerance = 5e-5f;
        const int Samples = 1024;

        [Test]
        public void TheGpuBend_MatchesTheCpuModel_ForEveryPush_HeightAndMaximum()
        {
            var shader = Shader.Find("Hidden/Downbeat/BladeBendProbe");
            Assert.IsNotNull(shader, "the probe shader is in the project");
            var material = new Material(shader);
            var input = new Texture2D(Samples, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var target = RenderTexture.GetTemporary(Samples, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var output = new Texture2D(Samples, 2, TextureFormat.RGBAFloat, false, true);
            var previousWind = Shader.GetGlobalVector("_MeadowWindA");
            try
            {
                var rng = new System.Random(5);
                var strengths = new[] { 0f, 1e-9f, 1e-6f, 1e-4f, 3e-3f, 0.05f, 0.1f, 0.3f, 0.6f, 1f, 2f, 4f, 8f, 25f, 60f, 1e5f };
                var heights = new[] { 0f, 0.1f, 0.5f, 0.9f, 1f };
                var places = new (float bx, float bz, float t, float height)[Samples];
                for (var i = 0; i < Samples; i++)
                {
                    var s = i < strengths.Length * 8 ? strengths[i % strengths.Length] : (float)Math.Pow(10.0, rng.NextDouble() * 3.0 - 2.0);   // 0.01 .. 10 beyond the listed ones
                    var angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    var t = i < strengths.Length * 8 ? heights[(i / strengths.Length) % heights.Length] : (float)rng.NextDouble();
                    var height = 0.15f + (float)rng.NextDouble() * 0.7f;
                    // the components stay below the clamp both sides apply (40): the direction is then the push's own
                    places[i] = (Mathf.Clamp(s * Mathf.Cos(angle), -39f, 39f), Mathf.Clamp(s * Mathf.Sin(angle), -39f, 39f), t, height);
                }
                var colours = new Color[Samples];
                for (var i = 0; i < Samples; i++) colours[i] = new Color(places[i].bx, places[i].bz, places[i].t, places[i].height);
                input.SetPixels(colours);
                input.Apply();
                Shader.SetGlobalTexture("_BendIn", input);

                var worstPoint = 0f;
                var worstTangent = 0f;
                var largest = 0f;
                foreach (var maxDeg in new[] { 120f, 75f, 150f, 50f })
                {
                    var maxBend = BladeBend.MaxBendRadians(maxDeg);
                    Shader.SetGlobalVector("_MeadowWindA", new Vector4(2.6f, 2.2f, maxBend, 0f));
                    var previous = RenderTexture.active;
                    Graphics.Blit(null, target, material);
                    RenderTexture.active = target;
                    output.ReadPixels(new Rect(0, 0, Samples, 2), 0, 0);
                    output.Apply();
                    RenderTexture.active = previous;

                    var read = output.GetPixels();
                    for (var i = 0; i < Samples; i++)
                    {
                        var (bx, bz, t, height) = places[i];
                        var cpu = BladeBend.At(bx, bz, t, height, maxBend);
                        var offset = read[i];
                        var tangent = read[Samples + i];
                        worstPoint = Mathf.Max(worstPoint, Mathf.Abs(cpu.X - offset.r), Mathf.Abs(cpu.Y - offset.g), Mathf.Abs(cpu.Z - offset.b));
                        worstTangent = Mathf.Max(worstTangent, Mathf.Abs(cpu.TangentX - tangent.r), Mathf.Abs(cpu.TangentY - tangent.g), Mathf.Abs(cpu.TangentZ - tangent.b));
                        largest = Mathf.Max(largest, Mathf.Abs(offset.r), Mathf.Abs(offset.b));
                        Assert.IsFalse(float.IsNaN(offset.r + offset.g + offset.b + tangent.r + tangent.g + tangent.b), $"sample {i}: the GPU gives a number");
                    }
                }

                Debug.Log($"[BladeBendGpuParity] GPU vs CPU over {Samples} pushes x 4 maximums: point {worstPoint:0.0000000}, direction {worstTangent:0.0000000}");
                Assert.Greater(largest, 0.2f, "the samples include real bends (the test is not comparing zeros)");
                Assert.Less(worstPoint, Tolerance, "the point of the blade");
                Assert.Less(worstTangent, Tolerance, "the direction of the blade");
            }
            finally
            {
                Shader.SetGlobalVector("_MeadowWindA", previousWind);
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(input);
                UnityEngine.Object.DestroyImmediate(output);
            }
        }
    }
}
