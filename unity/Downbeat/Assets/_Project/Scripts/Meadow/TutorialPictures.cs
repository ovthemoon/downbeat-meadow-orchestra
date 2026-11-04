using System;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The pictures of the hands that come with the content (PNGs with a transparent background, loaded like the other pictures of the content and held by the content bundle). The tutorial draws from a copy of its own,
    /// so that a reload of the content, which gives the bundle's pictures back, cannot take them from under it. The copy has a chain of smaller sizes (the hand is drawn at about half the size of the picture) and
    /// the pixels with nothing in them are given the colour of the outline, so that the smoothing between a pixel of the hand and an empty one does not pull its edge towards black.
    /// </summary>
    static class TutorialPictures
    {
        static readonly Color32 EmptyPixel = new Color32(0x8A, 0x5A, 0x2B, 0);   // the brown of the outline, no alpha

        /// <summary>A readable-free copy of <paramref name="source"/> that the caller owns; null when there is no source or it cannot be copied (reported once).</summary>
        public static Texture2D Copy(Texture2D source, string name)
        {
            if (source == null) return null;
            var previous = RenderTexture.active;
            RenderTexture target = null;
            try
            {
                var width = source.width;
                var height = source.height;
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, target);   // also for a picture that is not readable
                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                var pixels = copy.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a == 0) pixels[i] = EmptyPixel;
                }
                copy.SetPixels32(pixels);
                copy.Apply(true, true);
                return copy;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.LogWarning($"[tutorial] the picture {source.name} could not be copied ({e.Message}); that hand is drawn in code instead");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
