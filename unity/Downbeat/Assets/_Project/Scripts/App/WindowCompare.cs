using System.Collections;
using System.IO;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--capture-compare PATH_PREFIX [--capture-after SEC]</c>: proves (or disproves) that a capture looks like the window. In one frame it saves the window's own back buffer and the
    /// off-screen picture of the same size that every other capture uses, and logs how far apart they are (mean absolute difference per colour channel, 0..255). Use with <c>--no-record</c>.
    /// </summary>
    public sealed class WindowCompare : MonoBehaviour
    {
        const double DefaultAfterSec = 12.0;

        string _prefix;
        double _afterSec;
        Camera _camera;

        public static void Begin(LaunchArgs args, Camera camera)
        {
            var prefix = args.Get("capture-compare");
            if (string.IsNullOrEmpty(prefix)) return;
            var host = new GameObject("WindowCompare").AddComponent<WindowCompare>();
            host._prefix = Path.GetFullPath(prefix);
            host._afterSec = args.TryGetDouble("capture-after", out var after) ? after : DefaultAfterSec;
            host._camera = camera;
            host.StartCoroutine(host.Run());
        }

        IEnumerator Run()
        {
            while (Time.realtimeSinceStartupAsDouble < _afterSec) yield return null;
            yield return new WaitForEndOfFrame();
            var window = ScreenCapture.CaptureScreenshotAsTexture();
            var offscreen = MeadowCapture.Render(_camera, window.width, window.height);
            var difference = MeanAbsoluteDifference(window, offscreen);
            Directory.CreateDirectory(Path.GetDirectoryName(_prefix));
            File.WriteAllBytes(_prefix + "-window.png", window.EncodeToPNG());
            File.WriteAllBytes(_prefix + "-offscreen.png", offscreen.EncodeToPNG());
            Debug.Log($"[WindowCompare] {QualitySettings.names[QualitySettings.GetQualityLevel()]} {window.width}x{window.height}: mean absolute difference window vs off-screen {difference:0.00} of 255");
            Destroy(offscreen);
            Destroy(window);
        }

        static float MeanAbsoluteDifference(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            var sum = 0L;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }
    }
}
