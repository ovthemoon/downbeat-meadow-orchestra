using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Meadow
{
    /// <summary>
    /// For looking at the glow of the meadow: <c>--bloom-threshold X</c>, <c>--bloom-intensity X</c>, <c>--bloom-scatter X</c> and <c>--bloom-clamp X</c> change the Bloom of the scene's volume for this run
    /// (a copy of the profile; the asset is not touched). Without them nothing happens.
    /// </summary>
    public static class BloomTuning
    {
        public static void Apply(LaunchArgs args)
        {
            if (!args.TryGetDouble("bloom-threshold", out var threshold) & !args.TryGetDouble("bloom-intensity", out var intensity) & !args.TryGetDouble("bloom-scatter", out var scatter) & !args.TryGetDouble("bloom-clamp", out var clamp)) return;
            var volume = Object.FindFirstObjectByType<Volume>();
            if (volume == null || !volume.profile.TryGet<Bloom>(out var bloom))
            {
                Debug.LogWarning("[BloomTuning] no Bloom in the scene's volume");
                return;
            }
            if (args.TryGetDouble("bloom-threshold", out threshold)) bloom.threshold.Override((float)threshold);
            if (args.TryGetDouble("bloom-intensity", out intensity)) bloom.intensity.Override((float)intensity);
            if (args.TryGetDouble("bloom-scatter", out scatter)) bloom.scatter.Override((float)scatter);
            if (args.TryGetDouble("bloom-clamp", out clamp)) bloom.clamp.Override((float)clamp);
            Debug.Log($"[BloomTuning] threshold {bloom.threshold.value} intensity {bloom.intensity.value} scatter {bloom.scatter.value} clamp {bloom.clamp.value}");
        }
    }
}
