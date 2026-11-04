using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>Draws something moving every frame so the GPU/render path is exercised during frame-time tests.</summary>
    public sealed class Spinner : MonoBehaviour
    {
        const int TextureSize = 16;
        const float DegreesPerSecond = 90f;

        void Awake()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[TextureSize * TextureSize];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 160, 40, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), TextureSize / 2f);
            gameObject.AddComponent<SpriteRenderer>().sprite = sprite;
        }

        void Update() => transform.Rotate(0f, 0f, DegreesPerSecond * Time.unscaledDeltaTime);
    }
}
