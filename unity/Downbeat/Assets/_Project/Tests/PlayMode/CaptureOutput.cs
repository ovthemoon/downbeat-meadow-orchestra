using System;
using System.IO;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>
    /// Where the pictures and measurement files of the PlayMode tests go. Every run writes its full-size pictures (PNG) and its numbers into the git-ignored folder
    /// <c>captures/</c> at the repository root, so a test run never touches a tracked file. Only when the environment variable <c>DOWNBEAT_KEEP_CAPTURES=1</c> is set
    /// (on purpose, for the few pictures that are kept as evidence) is a copy also written to the tracked path: a picture as a JPEG of quality 85 and at most 1920 pixels wide.
    /// </summary>
    public static class CaptureOutput
    {
        public const string KeepVariable = "DOWNBEAT_KEEP_CAPTURES";
        const int JpegQuality = 85;
        const int KeptMaxWidth = 1920;

        public static bool KeepTracked => Environment.GetEnvironmentVariable(KeepVariable) == "1";

        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>The untracked folder for pictures (<c>captures/visual</c>) and numbers (<c>captures/perf</c>).</summary>
        public static string VisualDir => Path.Combine(RepoRoot, "captures", "visual");
        public static string PerfDir => Path.Combine(RepoRoot, "captures", "perf");
        public static string TrackedVisualDir => Path.Combine(RepoRoot, "measurements", "visual");
        public static string TrackedPerfDir => Path.Combine(RepoRoot, "measurements", "perf");

        /// <summary>Writes <paramref name="name"/> (a .png name) into the ignored folder; with the keep variable, also a JPEG of it into measurements/visual.</summary>
        public static void SavePicture(Texture2D picture, string name)
        {
            Directory.CreateDirectory(VisualDir);
            File.WriteAllBytes(Path.Combine(VisualDir, name), picture.EncodeToPNG());
            if (!KeepTracked) return;

            var kept = picture.width > KeptMaxWidth ? Shrink(picture, KeptMaxWidth) : picture;
            Directory.CreateDirectory(TrackedVisualDir);
            File.WriteAllBytes(Path.Combine(TrackedVisualDir, Path.ChangeExtension(name, ".jpg")), kept.EncodeToJPG(JpegQuality));
            if (kept != picture) UnityEngine.Object.Destroy(kept);
        }

        static Texture2D Shrink(Texture2D source, int width)
        {
            var height = Mathf.RoundToInt(source.height * (float)width / source.width);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }
    }
}
