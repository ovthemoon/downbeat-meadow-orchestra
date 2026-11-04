using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Downbeat.EditorTools
{
    /// <summary>
    /// Batchmode entry points for the two native players (Mono scripting backend, no IL2CPP):
    ///   Unity -batchmode -quit -projectPath ... -buildTarget StandaloneWindows64 -executeMethod Downbeat.EditorTools.NativeBuild.BuildWindows
    ///   Unity -batchmode -quit -projectPath ... -buildTarget StandaloneOSX       -executeMethod Downbeat.EditorTools.NativeBuild.BuildMac
    /// Output: &lt;repo&gt;/builds/windows/Downbeat.exe and &lt;repo&gt;/builds/mac/Downbeat.app (override the folder with -dbOut).
    /// The repository's content folder is copied next to the executable, where the player looks for it first, and a second time into the player's StreamingAssets/default-content
    /// (Downbeat_Data/StreamingAssets on Windows, Downbeat.app/Contents/Resources/Data/StreamingAssets on a Mac), the built-in copy that a PC without a usable content folder starts with.
    /// </summary>
    public static class NativeBuild
    {
        const string WindowsFolder = "windows";
        const string MacFolder = "mac";
        const string WindowsExecutable = "Downbeat.exe";
        const string MacExecutable = "Downbeat.app";
        const string ContentFolder = "content";
        const string BuiltInContentFolder = "default-content";
        const string StreamingAssetsFolder = "StreamingAssets";
        const string CompanyName = "ovthemoon"; // part of Application.persistentDataPath, where the cards (Task 11) are kept
        const int DefaultWidth = 1920;
        const int DefaultHeight = 1080;

        public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, WindowsFolder, WindowsExecutable);

        public static void BuildMac() => Run(BuildTarget.StandaloneOSX, MacFolder, MacExecutable);

        static void Run(BuildTarget target, string folderName, string executableName)
        {
            try
            {
                var exitCode = Build(target, folderName, executableName);
                EditorApplication.Exit(exitCode);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativeBuild] build threw: {e}");
                EditorApplication.Exit(2);
            }
        }

        static int Build(BuildTarget target, string folderName, string executableName)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var outDir = Path.GetFullPath(ReadArg("-dbOut") ?? Path.Combine(repoRoot, "builds", folderName));
            Directory.CreateDirectory(outDir);

            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[NativeBuild] no enabled scene in the build settings");
                return 3;
            }

            ApplyPlayerSettings(target);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, executableName),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.DetailedBuildReport,
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[NativeBuild] result={report.summary.result} totalSize={report.summary.totalSize} time={report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded) return 1;

            CopyDirectory(Path.Combine(repoRoot, ContentFolder), Path.Combine(outDir, ContentFolder));
            var builtIn = Path.Combine(StreamingAssetsDirectory(target, outDir, executableName), BuiltInContentFolder);
            CopyDirectory(Path.Combine(repoRoot, ContentFolder), builtIn);
            if (!File.Exists(Path.Combine(builtIn, "content.json")))
            {
                Debug.LogError($"[NativeBuild] the built-in content was not copied to {builtIn}");
                return 4;
            }
            Debug.Log($"[NativeBuild] built-in content copied to {builtIn}");
            WriteSummary(report, target, outDir, repoRoot);
            return 0;
        }

        /// <summary>Where the player finds its StreamingAssets: the folder Application.streamingAssetsPath names.</summary>
        static string StreamingAssetsDirectory(BuildTarget target, string outDir, string executableName)
        {
            if (target == BuildTarget.StandaloneOSX) return Path.Combine(outDir, executableName, "Contents", "Resources", "Data", StreamingAssetsFolder);
            return Path.Combine(outDir, Path.GetFileNameWithoutExtension(executableName) + "_Data", StreamingAssetsFolder);
        }

        static string[] EnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var paths = new System.Collections.Generic.List<string>(scenes.Length);
            foreach (var scene in scenes)
            {
                if (scene.enabled) paths.Add(scene.path);
            }
            return paths.ToArray();
        }

        static void ApplyPlayerSettings(BuildTarget target)
        {
            EditorUserBuildSettings.development = false;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = DefaultWidth;
            PlayerSettings.defaultScreenHeight = DefaultHeight;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.SplashScreen.show = false;
#if UNITY_EDITOR_OSX
            // UnityEditor.OSXStandalone only exists in an editor with Mac build support; a Windows editor without it would not compile this file.
            if (target == BuildTarget.StandaloneOSX) UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.x64ARM64;
#endif
        }

        static void WriteSummary(BuildReport report, BuildTarget target, string outDir, string repoRoot)
        {
            var summary = new BuildSummary
            {
                unity = Application.unityVersion,
                target = target.ToString(),
                result = report.summary.result.ToString(),
                buildSeconds = report.summary.totalTime.TotalSeconds,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                colorSpace = PlayerSettings.colorSpace.ToString(),
                output = outDir.Replace(repoRoot, string.Empty).TrimStart(Path.DirectorySeparatorChar),
            };
            File.WriteAllText(outDir.TrimEnd(Path.DirectorySeparatorChar) + ".build-report.json", JsonUtility.ToJson(summary, true));
        }

        static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
        }

        static string ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        [Serializable]
        class BuildSummary
        {
            public string unity;
            public string target;
            public string result;
            public double buildSeconds;
            public string scriptingBackend;
            public string colorSpace;
            public string output;
        }
    }
}
