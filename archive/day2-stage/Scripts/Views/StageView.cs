using Downbeat.Content;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>The stage: background with a slow drift (burn-in protection) and the three musicians.</summary>
    public sealed class StageView
    {
        public const float WorldWidth = 16f;
        public const float WorldHeight = 9f;
        const float DriftAmplitude = 0.12f;
        const float DriftRate = 0.15f;
        const int BackgroundSortingOrder = -100;

        readonly Camera _camera;
        readonly Transform _background;
        readonly Vector2 _backgroundSize;
        readonly PerformerView[] _performers = new PerformerView[Parts.Count];

        public StageView(ContentBundle content, ViewLayout layout, ViewAssets assets, Sprite disc)
        {
            var theme = content.Dto.theme;
            var root = new GameObject("StageView").transform;

            _camera = CreateCamera("StageCamera", layout.StageViewport, root);
            _camera.gameObject.AddComponent<AudioListener>();

            var backgroundTexture = content.Texture(theme.stageBackground);
            var bg = new GameObject("StageBackground").AddComponent<SpriteRenderer>();
            bg.transform.SetParent(root, false);
            bg.sprite = SpriteFactory.FromImage(backgroundTexture, theme.pixelsPerUnit, new Vector2(0.5f, 0.5f));
            bg.sortingOrder = BackgroundSortingOrder;
            _background = bg.transform;
            _backgroundSize = new Vector2(backgroundTexture.width / theme.pixelsPerUnit, backgroundTexture.height / theme.pixelsPerUnit);

            for (var i = 0; i < theme.performers.Length; i++)
            {
                var data = theme.performers[i];
                Parts.TryParse(data.part, out var part);
                var idleTexture = content.Texture(data.idle);
                var pivot = new Vector2(data.pivotX, data.pivotY);
                var idle = SpriteFactory.FromImage(idleTexture, theme.pixelsPerUnit, pivot);
                var play = SpriteFactory.FromImage(content.Texture(data.play), theme.pixelsPerUnit, pivot);
                ColorUtility.TryParseHtmlString(ColorFor(theme, data.part), out var color);
                _performers[(int)part] = new PerformerView(root, data, theme.pixelsPerUnit, idle, play, color, assets.AdditiveMaterial, disc, (int)part);
            }
        }

        public Camera Camera => _camera;

        /// <summary>The musician for this part plays now (called when the note is heard, not when it was requested).</summary>
        public void Flash(Part part, float velocity, float time) => _performers[(int)part].Flash(velocity, time);

        public void Tick(float time)
        {
            // cover the camera with the background whatever the view's aspect is
            var viewHeight = _camera.orthographicSize * 2f;
            var viewWidth = viewHeight * _camera.aspect;
            var scale = Mathf.Max(viewWidth / _backgroundSize.x, viewHeight / _backgroundSize.y) * 1.02f;
            _background.localScale = new Vector3(scale, scale, 1f);
            _background.localPosition = new Vector3(Mathf.Sin(time * DriftRate) * DriftAmplitude, 0f, 0f);
            for (var i = 0; i < _performers.Length; i++) _performers[i].Tick(time);
        }

        static string ColorFor(ThemeDto theme, string part)
        {
            foreach (var entry in theme.partColors)
            {
                if (entry.part == part) return entry.color;
            }
            return "#FFFFFF";
        }

        internal static Camera CreateCamera(string name, Rect viewport, Transform parent, float worldX = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldX, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = WorldHeight * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.rect = viewport;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            return camera;
        }
    }
}
