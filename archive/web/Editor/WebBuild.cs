using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Downbeat.EditorTools
{
    /// <summary>
    /// Batchmode entry point for Web (WebGL) builds.
    /// Usage: Unity -batchmode -projectPath ... -executeMethod Downbeat.EditorTools.WebBuild.Run
    ///        -dbScene Assets/Scenes/SampleScene.unity -dbOut Builds/s3-template -dbProfile baseline|size
    /// </summary>
    public static class WebBuild
    {
        const string DefaultScene = "Assets/Scenes/SampleScene.unity";
        const string DefaultOut = "Builds/web";
        const string ProfileBaseline = "baseline";
        const string ProfileSize = "size";

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var scene = ReadArg(args, "-dbScene") ?? DefaultScene;
            var outDir = ReadArg(args, "-dbOut") ?? DefaultOut;
            var profile = ReadArg(args, "-dbProfile") ?? ProfileBaseline;
            var template = ReadArg(args, "-dbTemplate");
            var codeOptimization = ReadArg(args, "-dbCodeOpt"); // BuildTimes | RuntimeSpeed | RuntimeSpeedLTO | DiskSize | DiskSizeLTO

            try
            {
                var exitCode = Build(scene, outDir, profile, template, codeOptimization);
                EditorApplication.Exit(exitCode);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebBuild] build threw: {e}");
                EditorApplication.Exit(2);
            }
        }

        static int Build(string scene, string outDir, string profile, string template, string codeOptimization)
        {
            if (!File.Exists(scene))
            {
                Debug.LogError($"[WebBuild] scene not found: {scene}");
                return 3;
            }

            ApplyCommonSettings(template);
            if (profile == ProfileSize) ApplySizeProfile(codeOptimization);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = profile == ProfileSize ? BuildOptions.DetailedBuildReport : BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            WriteReport(report, scene, outDir, profile);
            WriteAssetBreakdown(report, outDir);
            Debug.Log($"[WebBuild] result={report.summary.result} totalSize={report.summary.totalSize} time={report.summary.totalTime}");
            return report.summary.result == BuildResult.Succeeded ? 0 : 1;
        }

        static void ApplyCommonSettings(string template)
        {
            EditorUserBuildSettings.development = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.dataCaching = true;
            if (!string.IsNullOrEmpty(template)) PlayerSettings.WebGL.template = template;
        }

        static void ApplySizeProfile(string codeOptimization)
        {
            var target = NamedBuildTarget.WebGL;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
            var optimization = string.IsNullOrEmpty(codeOptimization)
                ? UnityEditor.WebGL.WasmCodeOptimization.DiskSize
                : (UnityEditor.WebGL.WasmCodeOptimization)Enum.Parse(typeof(UnityEditor.WebGL.WasmCodeOptimization), codeOptimization);
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = optimization;
            PlayerSettings.SplashScreen.show = false;
        }

        static void WriteReport(BuildReport report, string scene, string outDir, string profile)
        {
            var target = NamedBuildTarget.WebGL;
            var summary = new BuildSummaryJson
            {
                unity = Application.unityVersion,
                result = report.summary.result.ToString(),
                scene = scene,
                profile = profile,
                buildSeconds = report.summary.totalTime.TotalSeconds,
                reportedTotalBytes = (long)report.summary.totalSize,
                compression = PlayerSettings.WebGL.compressionFormat.ToString(),
                decompressionFallback = PlayerSettings.WebGL.decompressionFallback,
                nameFilesAsHashes = PlayerSettings.WebGL.nameFilesAsHashes,
                exceptionSupport = PlayerSettings.WebGL.exceptionSupport.ToString(),
                strippingLevel = PlayerSettings.GetManagedStrippingLevel(target).ToString(),
                il2cppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(target).ToString(),
                il2cppConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(target).ToString(),
                codeOptimization = UnityEditor.WebGL.UserBuildSettings.codeOptimization.ToString(),
                splashScreenShown = PlayerSettings.SplashScreen.show,
                development = EditorUserBuildSettings.development,
                template = PlayerSettings.WebGL.template,
            };
            var reportPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outDir)) ?? ".", Path.GetFileName(outDir) + ".build-report.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(summary, true));
        }

        static void WriteAssetBreakdown(BuildReport report, string outDir)
        {
            var entries = new List<AssetEntry>();
            foreach (var packed in report.packedAssets)
            {
                foreach (var content in packed.contents)
                {
                    entries.Add(new AssetEntry
                    {
                        path = content.sourceAssetPath,
                        type = content.type != null ? content.type.Name : "?",
                        bytes = (long)content.packedSize,
                    });
                }
            }

            var byType = entries
                .GroupBy(e => e.type)
                .Select(g => new TypeTotal { type = g.Key, count = g.Count(), bytes = g.Sum(e => e.bytes) })
                .OrderByDescending(t => t.bytes)
                .Take(25)
                .ToArray();
            var top = entries.OrderByDescending(e => e.bytes).Take(60).ToArray();
            var modules = report.strippingInfo != null
                ? report.strippingInfo.includedModules.OrderBy(m => m).ToArray()
                : new string[0];

            var breakdown = new BreakdownJson
            {
                assetCount = entries.Count,
                packedTotalBytes = entries.Sum(e => e.bytes),
                byType = byType,
                topAssets = top,
                includedEngineModules = modules,
            };
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outDir)) ?? ".", Path.GetFileName(outDir) + ".asset-breakdown.json");
            File.WriteAllText(path, JsonUtility.ToJson(breakdown, true));
        }

        static string ReadArg(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        [Serializable]
        class AssetEntry
        {
            public string path;
            public string type;
            public long bytes;
        }

        [Serializable]
        class TypeTotal
        {
            public string type;
            public int count;
            public long bytes;
        }

        [Serializable]
        class BreakdownJson
        {
            public int assetCount;
            public long packedTotalBytes;
            public TypeTotal[] byType;
            public AssetEntry[] topAssets;
            public string[] includedEngineModules;
        }

        [Serializable]
        class BuildSummaryJson
        {
            public string unity;
            public string result;
            public string scene;
            public string profile;
            public double buildSeconds;
            public long reportedTotalBytes;
            public string compression;
            public bool decompressionFallback;
            public bool nameFilesAsHashes;
            public string exceptionSupport;
            public string strippingLevel;
            public string il2cppCodeGeneration;
            public string il2cppConfiguration;
            public string codeOptimization;
            public bool splashScreenShown;
            public bool development;
            public string template;
        }
    }
}
