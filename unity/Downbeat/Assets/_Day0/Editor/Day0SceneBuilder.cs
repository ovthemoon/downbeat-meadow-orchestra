using System.IO;
using Downbeat.Day0;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Downbeat.EditorTools
{
    /// <summary>Creates the Day 0 test scene (copy of the template scene plus the controller) and configures the click clip import.</summary>
    public static class Day0SceneBuilder
    {
        const string ClickPath = "Assets/_Day0/Audio/click.wav";
        const string Day0Scene = "Assets/_Day0/Scenes/Day0.unity";

        public static void Create()
        {
            ConfigureClickImport();
            Directory.CreateDirectory(Path.GetDirectoryName(Day0Scene));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickPath);
            if (clip == null)
            {
                Debug.LogError($"[Day0SceneBuilder] click clip missing at {ClickPath}");
                EditorApplication.Exit(1);
                return;
            }

            var host = new GameObject("Day0");
            var controller = host.AddComponent<Day0Controller>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("clickClip").objectReferenceValue = clip;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var spinner = new GameObject("Spinner");
            spinner.AddComponent<Spinner>();

            EditorSceneManager.SaveScene(scene, Day0Scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Day0SceneBuilder] created {Day0Scene}");
            EditorApplication.Exit(0);
        }

        /// <summary>Scene with only a camera and a light, for the Built-in render pipeline size comparison (S-3).</summary>
        public static void CreateEmptyScene()
        {
            const string path = "Assets/Scenes/Empty.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var emptyScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(emptyScene, path);
            Debug.Log($"[Day0SceneBuilder] created {path}");
            EditorApplication.Exit(0);
        }

        static void ConfigureClickImport()
        {
            AssetDatabase.ImportAsset(ClickPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(ClickPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }
    }
}
