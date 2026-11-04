using System;
using System.IO;
using Downbeat.App;
using Downbeat.Meadow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.EditorTools
{
    /// <summary>
    /// Creates <c>Meadow.unity</c>, the one scene of the app, and the materials it needs: the camera, the sun, the backdrop quad, the ground mesh holder, the
    /// global post-processing volume. They are all empty shells: every value is filled from content.json at run time
    /// (<see cref="MeadowStage.Configure"/>). The scene has exactly one AudioListener-free camera; the sound engine owns the only listener.
    /// Usage: Unity -batchmode -quit -projectPath ... -executeMethod Downbeat.EditorTools.MeadowSceneBuilder.Create
    /// </summary>
    public static class MeadowSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Meadow.unity";
        public const string MaterialFolder = "Assets/_Project/Materials";
        const string BackdropShader = "Downbeat/Meadow/BackdropBlend";
        const string GroundShader = "Downbeat/Meadow/Ground";
        const string GrassShader = "Downbeat/Meadow/Grass";
        const string FlowerShaderName = "Downbeat/Meadow/Flower";
        const string ParticleShaderName = "Downbeat/Meadow/Particle";
        const string RibbonShaderName = "Downbeat/Meadow/WindRibbon";
        const string PlacementPath = "Assets/_Project/Shaders/GrassPlacement.compute";
        const string MainCameraTag = "MainCamera";

        public static void Create()
        {
            try
            {
                Build();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MeadowSceneBuilder] failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var backdropMaterial = EnsureMaterial("Backdrop", BackdropShader);
            var groundMaterial = EnsureMaterial("Ground", GroundShader);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(UrpSetup.VolumeProfilePath);
            if (profile == null) throw new FileNotFoundException(UrpSetup.VolumeProfilePath + " (run UrpSetup first)");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var stageObject = new GameObject("Meadow");
            var stage = stageObject.AddComponent<MeadowStage>();
            var content = new GameObject("Content");
            content.transform.SetParent(stageObject.transform, false);

            var cameraObject = new GameObject("MeadowCamera") { tag = MainCameraTag };
            cameraObject.transform.SetParent(content.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); // no AudioListener: the sound engine owns the only one
            var rig = cameraObject.AddComponent<MeadowCameraRig>();
            cameraObject.GetComponent<Camera>().GetUniversalAdditionalCameraData();

            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(content.transform, false);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;

            var backdropObject = new GameObject("Backdrop");
            backdropObject.transform.SetParent(content.transform, false);
            backdropObject.AddComponent<MeshFilter>();
            var backdropRenderer = backdropObject.AddComponent<MeshRenderer>();
            backdropRenderer.sharedMaterial = backdropMaterial;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;
            var backdrop = backdropObject.AddComponent<BackdropView>();

            var terrainObject = new GameObject("Terrain");
            terrainObject.transform.SetParent(content.transform, false);
            var terrainFilter = terrainObject.AddComponent<MeshFilter>();
            var terrainRenderer = terrainObject.AddComponent<MeshRenderer>();
            terrainRenderer.sharedMaterial = groundMaterial;
            terrainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            terrainRenderer.receiveShadows = false;
            var terrain = terrainObject.AddComponent<TerrainView>();

            var grassObject = new GameObject("Grass");
            grassObject.transform.SetParent(content.transform, false);
            var grass = grassObject.AddComponent<GrassRenderer>();
            var placement = AssetDatabase.LoadAssetAtPath<ComputeShader>(PlacementPath);
            var grassShader = Shader.Find(GrassShader);
            if (placement == null || grassShader == null) throw new FileNotFoundException($"{PlacementPath} or shader '{GrassShader}' not found");

            var flowersObject = new GameObject("Flowers");
            flowersObject.transform.SetParent(content.transform, false);
            var flowerField = flowersObject.AddComponent<FlowerField>();
            var flowerShader = Shader.Find(FlowerShaderName);
            if (flowerShader == null) throw new FileNotFoundException($"shader '{FlowerShaderName}' not found");

            var particlesObject = new GameObject("Particles");
            particlesObject.transform.SetParent(content.transform, false);
            var particleDirector = particlesObject.AddComponent<ParticleDirector>();
            var particleShader = Shader.Find(ParticleShaderName);
            var handObject = new GameObject("HandWind");
            handObject.transform.SetParent(content.transform, false);
            var handWind = handObject.AddComponent<HandWindView>();
            var ribbonShader = Shader.Find(RibbonShaderName);
            if (particleShader == null || ribbonShader == null) throw new FileNotFoundException($"shader '{ParticleShaderName}' or '{RibbonShaderName}' not found");

            var volumeObject = new GameObject("PostProcessing");
            volumeObject.transform.SetParent(content.transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            Wire(backdrop, ("quad", backdropRenderer), ("sun", sun));
            Wire(terrain, ("meshFilter", terrainFilter), ("meshRenderer", terrainRenderer));
            Wire(grass, ("placement", placement), ("grassShader", grassShader));
            Wire(flowerField, ("flowerShader", flowerShader));
            Wire(particleDirector, ("particleShader", particleShader));
            Wire(handWind, ("ribbonShader", ribbonShader));
            Wire(stage, ("cameraRig", rig), ("backdrop", backdrop), ("terrain", terrain), ("grass", grass), ("flowers", flowerField), ("particles", particleDirector), ("handWind", handWind), ("content", content));

            var bootstrap = new GameObject("Downbeat");
            var scope = bootstrap.AddComponent<DownbeatLifetimeScope>();
            Wire(scope, ("meadow", stage));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeadowSceneBuilder] created {ScenePath}");
        }

        static void Wire(UnityEngine.Object target, params (string field, UnityEngine.Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in references)
            {
                var property = serialized.FindProperty(field);
                if (property == null) throw new MissingFieldException(target.GetType().Name, field);
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material EnsureMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new FileNotFoundException($"shader '{shaderName}' not found");
            Directory.CreateDirectory(MaterialFolder);
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
