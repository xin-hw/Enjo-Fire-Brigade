using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FireRescue.Editor
{
    public static class WaterLabSetup
    {
        public const string ScenePath = "Assets/FireRescue/Scenes/WaterLab.unity";

        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("WaterLab already exists. Open the saved scene.");

            Scene review = EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            var actorTemplate = Object.FindAnyObjectByType<LocalPlayerInput>();
            var toiletTemplate = Object.FindObjectsByType<FluidSource>().Single(s => s.Element == ElementKind.Water);
            var viewTemplate = Camera.main;
            var lights = Object.FindObjectsByType<Light>().Where(light => light.type == LightType.Directional).ToArray();
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var ambientIntensity = RenderSettings.ambientIntensity;
            var sky = RenderSettings.skybox;
            var white = AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder + "/Kitchen floor.mat");
            var plaster = AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder + "/Warm plaster.mat");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FireRescue/Art/Shaders/WaterLab.shader");
            if (!actorTemplate || !toiletTemplate || !viewTemplate || !white || !plaster || !shader)
                throw new InvalidOperationException("Water Lab is missing one of its source scene assets.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.skybox = sky;
            foreach (var light in lights)
            {
                var clone = Copy(light.gameObject, scene);
                if (light.type == LightType.Directional) RenderSettings.sun = clone.GetComponent<Light>();
            }

            var backdrop = Box("White backdrop", new Vector3(0f, -.08f, 0f), new Vector3(48f, .2f, 48f), white, 0);
            Object.DestroyImmediate(backdrop);
            var floor = Box("Floor receiving surface", Vector3.zero, new Vector3(8f, .2f, 6.4f), white, 0);
            var counter = Box("Counter receiving surface", new Vector3(1.1f, .525f, 1.58f),
                new Vector3(1.14f, .85f, 1.06f), plaster, 8);
            var divider = Box("Divider wall", new Vector3(1.96f, .99f, 0f), new Vector3(.24f, 1.78f, 6.4f), plaster, 8);
            var rear = Box("Rear wall", new Vector3(0f, .99f, 3.15f), new Vector3(8f, 1.78f, .18f), plaster, 8);
            var left = Box("Left wall", new Vector3(-3.92f, .99f, 0f), new Vector3(.18f, 1.78f, 6.4f), plaster, 8);

            var player = Copy(actorTemplate.gameObject, scene);
            player.transform.SetPositionAndRotation(new Vector3(-2.56f, .125f, -.25f), Quaternion.LookRotation(Vector3.right));
            var toilet = Copy(toiletTemplate.gameObject, scene).GetComponent<FluidSource>();
            toilet.transform.position = new Vector3(-3.36f, .125f, -.9f);
            var camera = Copy(viewTemplate.gameObject, scene).GetComponent<Camera>();
            camera.GetComponent<RescueCamera>().Initialize(player.transform);
            var labRoot = new GameObject("Water Lab");
            var wallOcclusion = labRoot.AddComponent<SprayWallOcclusion>();
            wallOcclusion.Configure(new Collider[] { divider, rear, left }, Array.Empty<HoseWindow>());
            var hose = player.GetComponent<HoseConnection>();
            var emitter = player.GetComponent<SprayEmitter>();
            var emitterData = new SerializedObject(emitter);
            var inlet = (Transform)emitterData.FindProperty("hoseInletAnchor").objectReferenceValue;
            hose.ConfigurePhysics(wallOcclusion, inlet);
            var motor = player.GetComponent<FirefighterMotor>();
            motor.Initialize(null, hose);
            emitter.Initialize(hose, labRoot.AddComponent<ElementReaction>());
            emitter.SetGroundHeight(.1f);
            emitter.SetWallOcclusion(wallOcclusion);
            var input = player.GetComponent<LocalPlayerInput>();
            input.Initialize(motor, hose, emitter, camera, new[] { toilet }, Array.Empty<RescueNpc>());
            input.SetMissionInteraction(null, null);
            Object.DestroyImmediate(player.GetComponent<PlayerFireContact>());
            Object.DestroyImmediate(player.GetComponent<PlayerCarryInteraction>());
            labRoot.AddComponent<WaterLab>().Initialize(emitter, toilet, camera, floor, counter, shader);

            EditorSceneManager.CloseScene(review, true);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save Water Lab.");
            Debug.Log("WATER_LAB_CREATED " + ScenePath);
        }

        static GameObject Copy(GameObject template, Scene scene)
        {
            var clone = Object.Instantiate(template);
            clone.name = template.name;
            if (clone.scene != scene) SceneManager.MoveGameObjectToScene(clone, scene);
            return clone;
        }

        static BoxCollider Box(string name, Vector3 position, Vector3 size, Material material, int layer)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = layer;
            box.transform.position = position;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box.GetComponent<BoxCollider>();
        }
    }
}
