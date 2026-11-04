using System.IO;
using Downbeat.App;
using Downbeat.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Downbeat.EditorTools
{
    /// <summary>Creates the one scene of the app (a bootstrap object only; everything else is built from content.json at run time) and its additive material.</summary>
    public static class DownbeatSceneBuilder
    {
        const string MaterialPath = "Assets/_Project/Materials/GlowAdditive.mat";
        const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string AdditiveShader = "Legacy Shaders/Particles/Additive";
        const float TintStrength = 1.3f;

        public static void Create()
        {
            var material = CreateMaterial();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var host = new GameObject("Downbeat");
            var assets = host.AddComponent<ViewAssets>();
            var assetsObject = new SerializedObject(assets);
            assetsObject.FindProperty("additiveMaterial").objectReferenceValue = material;
            assetsObject.ApplyModifiedPropertiesWithoutUndo();

            var scope = host.AddComponent<DownbeatLifetimeScope>();
            var scopeObject = new SerializedObject(scope);
            scopeObject.FindProperty("viewAssets").objectReferenceValue = assets;
            scopeObject.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[DownbeatSceneBuilder] created {ScenePath}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static Material CreateMaterial()
        {
            var shader = Shader.Find(AdditiveShader);
            if (shader == null) throw new FileNotFoundException($"shader '{AdditiveShader}' not found");
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "GlowAdditive" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            // The particle shader multiplies by _TintColor (default grey 0.5). A value above 1 makes the glow clearly visible on the bright stage.
            material.SetColor("_TintColor", new Color(TintStrength, TintStrength, TintStrength, TintStrength));
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
