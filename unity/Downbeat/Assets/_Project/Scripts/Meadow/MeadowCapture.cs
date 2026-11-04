using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Saves a picture of this app's own rendering: the camera is drawn once into an off-screen texture of the requested size. It never reads the desktop, and it works in a window,
    /// without one (batchmode) and in the editor.
    /// The picture goes through the same steps as what a window shows: a half-float linear HDR target (the glow of the fireflies and tips, which Bloom needs, is above 1), the multisampling
    /// of the active pipeline asset (the grass blades are drawn with alpha-to-coverage: without MSAA they are thin and the sparkle is gone), post-processing, and then the encode to 8-bit sRGB by the graphics card. <c>--capture-compare</c> checks the claim: the window's own back buffer and this picture, drawn in the same frame, differ by 0.3 of 255 on average.
    /// </summary>
    public static class MeadowCapture
    {
        const int DepthBits = 24;

        public static Texture2D Render(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, DepthBits, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = ActiveMsaaSamples() };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            RenderTexture encoded = null;
            Texture2D picture = null;
            var succeeded = false;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                // the encode to 8-bit sRGB is done by the graphics card (an sRGB target): fast, and the same step the window's final blit takes
                encoded = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(target, encoded);
                RenderTexture.active = encoded;
                picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                picture.Apply();
                succeeded = true;
                return picture;
            }
            finally
            {
                // everything that was taken is given back, also when a draw, blit or read throws
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (encoded != null) RenderTexture.ReleaseTemporary(encoded);
                if (!succeeded && picture != null) Release(picture);
                target.Release();
                Release(target);
            }
        }

        public static void SavePng(Camera camera, int width, int height, string path)
        {
            var picture = Render(camera, width, height);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Release(picture);
        }

        /// <summary>The multisampling of the pipeline asset that is active now (the quality tier's).</summary>
        static int ActiveMsaaSamples()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return asset == null ? 1 : Mathf.Max(1, asset.msaaSampleCount);
        }

        static void Release(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value); // the edit-mode tests draw pictures too
        }

        /// <summary>"1920x1080" (or "1920X1080") to its two numbers.</summary>
        public static bool TryParseSize(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.ToLowerInvariant().Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0;
        }
    }
}
