using System;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Meadow
{
    /// <summary>
    /// The knobs of how much the picture costs, set from a quality tier of <c>meadow.quality</c>: the render scale (the pixels drawn), how the picture is stretched back to the window, the MSAA
    /// sample count, the frame cap, the Bloom and the sky detail. They are values of the active URP pipeline asset and the scene's volume, set while the app runs; the first time one is set its
    /// old value is kept so that <see cref="Restore"/> can put it back (in the editor the pipeline assets are files, and a run must not change them).
    /// The render scale is a value of the pipeline asset, which every camera would share. It is meant for the picture of the meadow only: the record screen (whichever display it is on) and every capture (a
    /// camera that draws into a texture) are drawn at full scale, so their text and detail stay crisp. The meadow's camera is the one that is named (<see cref="SetWindowCamera"/>), not guessed from a display
    /// number (the meadow can be on the second display). Just before a camera is set up (<c>beginCameraRendering</c>) the asset carries that camera's scale, and the meadow's scale is put back when it is done.
    /// </summary>
    public static class RenderLevers
    {
        public const float MinRenderScale = 0.3f;
        public const float MaxRenderScale = 1.0f;
        public const int MinFps = 10;
        public const int MaxFps = 240;
        public static readonly int SkyCheapId = Shader.PropertyToID("_MeadowSkyCheap");

        struct Saved
        {
            public float Scale;
            public int Msaa;
            public UpscalingFilterSelection Upscaler;
        }

        static readonly System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, Saved> SavedAssets = new System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, Saved>();
        static bool _remembered;
        static int _vSync;
        static int _targetFps;
        static bool _bloomChanged;
        static float _windowScale = 1f;
        static bool _hooked;
        static Camera _windowCamera;

        /// <summary>
        /// The pipeline asset of the quality level in use. <c>GraphicsSettings.currentRenderPipeline</c> still names the old tier's asset in the frame in which the level was switched, so the
        /// asset of the level is asked for by its index.
        /// </summary>
        static UniversalRenderPipelineAsset Asset
        {
            get
            {
                var level = QualitySettings.GetRenderPipelineAssetAt(QualitySettings.GetQualityLevel()) as UniversalRenderPipelineAsset;
                return level != null ? level : GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            }
        }

        /// <summary>The render scale of the window now in use (1 without the URP asset).</summary>
        public static float RenderScale => Asset != null ? _windowScale : 1f;

        /// <summary>The frame cap now in use, 0 for none.</summary>
        public static int FrameCap => QualitySettings.vSyncCount == 0 && Application.targetFrameRate > 0 ? Application.targetFrameRate : 0;

        /// <summary>Applies a tier's levers (and the <c>--render-scale</c> / <c>--max-fps</c> overrides on top of them).</summary>
        public static void Apply(MeadowQualityDto tier, LaunchArgs launch)
        {
            Remember(Asset);
            var scale = launch != null && launch.TryGetDouble("render-scale", out var asked) ? (float)asked : tier.renderScale;
            SetRenderScale(scale);
            SetUpscaler(tier.upscaler);
            if (tier.msaa > 0) SetMsaa(tier.msaa);
            var cap = launch != null && launch.TryGetDouble("max-fps", out var fps) ? (int)fps : tier.targetFps;
            SetFrameCap(cap);
            ApplyBloom(tier.bloom);
            Shader.SetGlobalFloat(SkyCheapId, tier.skyDetail < 0.5f ? 1f : 0f);   // 1: the sky is drawn the cheap way (clouds from the baked tile, see MeadowCommon.hlsl); it moves just the same
        }

        public static void SetRenderScale(float scale)
        {
            var asset = Asset;
            if (asset == null) return;
            Remember(asset);
            _windowScale = Mathf.Clamp(scale, MinRenderScale, MaxRenderScale);
            asset.renderScale = _windowScale;
            Hook();
        }

        /// <summary>The camera of the meadow: the only one the render scale is for. Set when the meadow is built.</summary>
        public static void SetWindowCamera(Camera camera) => _windowCamera = camera;

        /// <summary>True for the camera of the meadow when it draws to the screen (a capture that gives it a texture to draw into is at full scale too); false for every other camera, the record screen's included.</summary>
        public static bool ScalesCamera(Camera camera) => camera != null && camera == _windowCamera && camera.cameraType == CameraType.Game && camera.targetTexture == null;

        /// <summary>The render scale that this camera is drawn at now: the meadow's scale for the camera of the meadow, 1 for any other.</summary>
        public static float ScaleFor(Camera camera) => ScalesCamera(camera) ? _windowScale : 1f;

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            var asset = Asset;
            if (asset != null) asset.renderScale = ScaleFor(camera);   // read by the pipeline when it sets this camera up, right after this event
        }

        static void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            var asset = Asset;
            if (asset != null) asset.renderScale = _windowScale;
        }

        public static void SetMsaa(int samples)
        {
            var asset = Asset;
            if (asset == null) return;
            Remember(asset);
            asset.msaaSampleCount = samples;
        }

        public static void SetUpscaler(string name)
        {
            var asset = Asset;
            if (asset == null) return;
            Remember(asset);
            asset.upscalingFilter = ParseUpscaler(name);
        }

        /// <summary>A frame cap of <paramref name="fps"/> (0: back to the display's vertical sync). The vertical sync is switched off while a cap is set: with it on, Unity ignores the cap.</summary>
        public static void SetFrameCap(int fps)
        {
            Remember();
            if (fps <= 0)
            {
                QualitySettings.vSyncCount = _vSync;
                Application.targetFrameRate = _targetFps;
                return;
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(fps, MinFps, MaxFps);
        }

        /// <summary>The cost of the glow: on or off, the blur, the size of the first picture and how many pictures it goes down through. The scene's volume is copied first (the asset is not touched).</summary>
        public static void ApplyBloom(MeadowBloomQualityDto settings)
        {
            if (settings == null) return;
            var volume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (volume == null || !volume.profile.TryGet<Bloom>(out var bloom)) return;
            bloom.active = settings.enabled;
            bloom.filter.Override(ParseFilter(settings.filter));
            bloom.downscale.Override(ParseDownscale(settings.downscale));
            bloom.maxIterations.Override(Mathf.Clamp(settings.maxIterations, 1, 16));
            _bloomChanged = true;
        }

        public static UpscalingFilterSelection ParseUpscaler(string name)
        {
            switch (name)
            {
                case "Auto": return UpscalingFilterSelection.Auto;
                case "Point": return UpscalingFilterSelection.Point;
                case "FSR": return UpscalingFilterSelection.FSR;
                default: return UpscalingFilterSelection.Linear;
            }
        }

        static BloomFilterMode ParseFilter(string name) => name == "Dual" ? BloomFilterMode.Dual : (name == "Kawase" ? BloomFilterMode.Kawase : BloomFilterMode.Gaussian);

        static BloomDownscaleMode ParseDownscale(string name) => name == "Quarter" ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half;

        /// <summary>One line for the log: what the picture costs now.</summary>
        public static string Describe()
        {
            var asset = Asset;
            if (asset == null) return "no URP asset";
            var cap = FrameCap;
            return $"scale {asset.renderScale:0.00} msaa {asset.msaaSampleCount} {asset.upscalingFilter} cap {(cap > 0 ? cap.ToString() : "vsync")}";
        }

        static void Remember(UniversalRenderPipelineAsset asset = null)
        {
            if (!_remembered)
            {
                _vSync = QualitySettings.vSyncCount;
                _targetFps = Application.targetFrameRate;
                _remembered = true;
            }
            if (asset != null && !SavedAssets.ContainsKey(asset)) SavedAssets[asset] = new Saved { Scale = asset.renderScale, Msaa = asset.msaaSampleCount, Upscaler = asset.upscalingFilter };
        }

        /// <summary>Puts the values of every pipeline asset that was changed back as they were before the first lever was set.</summary>
        public static void Restore()
        {
            foreach (var pair in SavedAssets)
            {
                if (pair.Key == null) continue;
                pair.Key.renderScale = pair.Value.Scale;
                pair.Key.msaaSampleCount = pair.Value.Msaa;
                pair.Key.upscalingFilter = pair.Value.Upscaler;
            }
            SavedAssets.Clear();
            _remembered = false;
            _bloomChanged = false;
            if (_hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                _hooked = false;
            }
            _windowScale = 1f;
            _windowCamera = null;
        }
    }
}
