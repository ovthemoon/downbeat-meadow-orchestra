using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.EditorTools
{
    /// <summary>
    /// Creates the Universal Render Pipeline set-up of the project: two pipeline assets (High and Low), the two matching quality
    /// levels, Linear colour space and the Volume profile of the meadow look. Idempotent: running it again updates the same assets.
    /// Usage: Unity -batchmode -quit -projectPath ... -executeMethod Downbeat.EditorTools.UrpSetup.Run
    /// </summary>
    public static class UrpSetup
    {
        public const string SettingsFolder = "Assets/_Project/Settings";
        public const string HighName = "URP_High";
        public const string LowName = "URP_Low";
        public const string VolumeProfilePath = SettingsFolder + "/MeadowVolumeProfile.asset";

        const string UrpPackagePath = "Packages/com.unity.render-pipelines.universal";
        const string PostProcessDataPath = UrpPackagePath + "/Runtime/Data/PostProcessData.asset";
        const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";
        const int TemplateQualityIndex = 3; // "High" of the Unity template; its other values (anisotropic, LOD, ...) are kept
        const int MsaaDisabled = 1;
        const int MsaaFourSamples = 4;
        const int LightsDisabled = 0;
        const int ShadowMapResolution2048 = 2048;
        const float HighShadowDistance = 40f;
        const int HighShadowCascades = 2;
        const int ColorGradingHdr = 1;
        const int ColorGradingLdr = 0;

        // The look of the meadow. The backdrop plates are finished pictures: the post-processing must leave their colours alone (the neutral tonemapper does
        // below 0.8; ACES would darken and tint the whole sky) and only roll off the HDR highlights of the grass, bloom the brightest tips and warm the shadows.
        // Bloom: only the clearly HDR pixels glow (threshold), each pixel's contribution is capped (clamp: sunlit grass tips and dew sparkles reach tens at sunset, and without the cap they bloomed into white blobs
        // a twentieth of the screen wide), and the halo stays tight (scatter): a sparkle or a firefly gets a small soft halo. Looked at with tools/bloom_metrics.py on pictures that match the window (--capture-compare).
        const float BloomThreshold = 1.2f;
        const float BloomIntensity = 0.3f;
        const float BloomScatter = 0.35f;
        const float BloomClamp = 6f;
        const float GradingContrast = 0f;
        const float GradingSaturation = 0f;
        static readonly Color GradingFilter = Color.white;

        public static void Run()
        {
            try
            {
                Apply();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UrpSetup] failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void Apply()
        {
            EnsureFolder(SettingsFolder);
            MoveGeneratedAssetsIntoSettingsFolder();

            var high = EnsureTierAsset(HighName, ConfigureHigh);
            var low = EnsureTierAsset(LowName, ConfigureLow);
            EnsureVolumeProfile();

            PlayerSettings.colorSpace = ColorSpace.Linear;
            GraphicsSettings.defaultRenderPipeline = high;
            ApplyQualityLevels(high, low);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UrpSetup] URP_High / URP_Low, quality levels, Linear colour space and MeadowVolumeProfile are in place");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>URP creates its global settings and default Volume profile in Assets/ on first import; keep them with the other settings.</summary>
        static void MoveGeneratedAssetsIntoSettingsFolder()
        {
            foreach (var name in new[] { "UniversalRenderPipelineGlobalSettings.asset", "DefaultVolumeProfile.asset" })
            {
                var from = "Assets/" + name;
                var to = SettingsFolder + "/" + name;
                if (AssetDatabase.LoadMainAssetAtPath(from) == null || AssetDatabase.LoadMainAssetAtPath(to) != null) continue;
                var problem = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(problem)) throw new InvalidOperationException($"could not move {from}: {problem}");
            }
        }

        static UniversalRenderPipelineAsset EnsureTierAsset(string name, Action<SerializedObject> configure)
        {
            var path = $"{SettingsFolder}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                var renderer = EnsureRendererData($"{SettingsFolder}/{name}_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            configure(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static UniversalRendererData EnsureRendererData(string path)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer != null) return renderer;
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            AssetDatabase.CreateAsset(renderer, path);
            ResourceReloader.ReloadAllNullIn(renderer, UrpPackagePath);
            return renderer;
        }

        static void ConfigureCommon(SerializedObject asset)
        {
            asset.FindProperty("m_SupportsHDR").boolValue = true; // Bloom and the ACES tonemap want a high dynamic range buffer
            asset.FindProperty("m_RenderScale").floatValue = 1f;
            asset.FindProperty("m_AdditionalLightsRenderingMode").intValue = LightsDisabled; // one sun is the only light of the meadow
            asset.FindProperty("m_UseSRPBatcher").boolValue = true;
        }

        static void ConfigureHigh(SerializedObject asset)
        {
            ConfigureCommon(asset);
            asset.FindProperty("m_MSAA").intValue = MsaaFourSamples;
            asset.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            asset.FindProperty("m_AnyShadowsSupported").boolValue = true;
            asset.FindProperty("m_SoftShadowsSupported").boolValue = true;
            asset.FindProperty("m_MainLightShadowmapResolution").intValue = ShadowMapResolution2048;
            asset.FindProperty("m_ShadowDistance").floatValue = HighShadowDistance;
            asset.FindProperty("m_ShadowCascadeCount").intValue = HighShadowCascades;
            asset.FindProperty("m_ColorGradingMode").intValue = ColorGradingHdr;
        }

        static void ConfigureLow(SerializedObject asset)
        {
            ConfigureCommon(asset);
            asset.FindProperty("m_MSAA").intValue = MsaaDisabled;
            asset.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            asset.FindProperty("m_AnyShadowsSupported").boolValue = false;
            asset.FindProperty("m_SoftShadowsSupported").boolValue = false;
            asset.FindProperty("m_ColorGradingMode").intValue = ColorGradingLdr;
        }

        static void EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.clamp.Override(BloomClamp);

            var grading = GetOrAdd<ColorAdjustments>(profile);
            grading.contrast.Override(GradingContrast);
            grading.saturation.Override(GradingSaturation);
            grading.colorFilter.Override(GradingFilter);

            EditorUtility.SetDirty(profile);
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>Exactly two quality levels, "High" and "Low", each bound to its own pipeline asset; High is the default everywhere.</summary>
        static void ApplyQualityLevels(UniversalRenderPipelineAsset high, UniversalRenderPipelineAsset low)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath(QualitySettingsPath);
            if (settings.Length == 0) throw new FileNotFoundException(QualitySettingsPath);
            var serialized = new SerializedObject(settings[0]);
            var levels = serialized.FindProperty("m_QualitySettings");

            // keep the template level, drop the rest, then make High and Low from that one
            if (levels.arraySize > TemplateQualityIndex)
            {
                for (var i = levels.arraySize - 1; i >= 0; i--)
                {
                    if (i != TemplateQualityIndex) levels.DeleteArrayElementAtIndex(i);
                }
            }
            if (levels.arraySize == 1) levels.InsertArrayElementAtIndex(0);

            ConfigureLevel(levels.GetArrayElementAtIndex(0), "High", high);
            ConfigureLevel(levels.GetArrayElementAtIndex(1), "Low", low);

            var defaults = serialized.FindProperty("m_PerPlatformDefaultQuality");
            for (var i = 0; i < defaults.arraySize; i++) defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 0;
            serialized.FindProperty("m_CurrentQuality").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings[0]);
        }

        static void ConfigureLevel(SerializedProperty level, string name, UniversalRenderPipelineAsset pipeline)
        {
            level.FindPropertyRelative("name").stringValue = name;
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
        }
    }
}
