using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FireRescue.Editor
{
    public static class WaterFireLabSetup
    {
        public const string ScenePath = "Assets/FireRescue/Scenes/WaterFireLab.unity";
        public static readonly Vector3 SmallFire = new(-.6f, .1f, -1f);
        public static readonly Vector3 LargeFire = new(-.6f, .1f, .6f);
        public static readonly Vector3 WallBack = new(2.6f, .1f, -.2f);

        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("WaterFireLab already exists.");
            if (!AssetDatabase.CopyAsset(WaterLabSetup.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy Water Lab.");

            var scene = EditorSceneManager.OpenScene(ScenePath);
            var review = EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var roots = review.GetRootGameObjects();
            var panTemplate = roots.SelectMany(root => root.GetComponentsInChildren<ObjectFire>())
                .Single(fire => fire.Kind == FireKind.CookingOil);
            var powderTemplate = roots.SelectMany(root => root.GetComponentsInChildren<FluidSource>())
                .Single(source => source.Element == ElementKind.DryPowder);
            var pan = Copy(panTemplate.gameObject, scene).GetComponent<ObjectFire>();
            pan.transform.position = new Vector3(1.1f, panTemplate.transform.position.y, 1.58f);
            var incident = pan.GetComponent<CookingOilIncident>();
            incident.Initialize(pan, null, null);
            pan.GetComponent<OilPanFireView>().SetArea(null);
            var powder = Copy(powderTemplate.gameObject, scene).GetComponent<FluidSource>();
            powder.transform.position = new Vector3(-2.82f, .125f, 1.05f);
            EditorSceneManager.CloseScene(review, true);

            var input = Object.FindAnyObjectByType<LocalPlayerInput>();
            var emitter = input.GetComponent<SprayEmitter>();
            var toilet = Object.FindObjectsByType<FluidSource>().Single(s => s.Element == ElementKind.Water);
            input.Initialize(input.GetComponent<FirefighterMotor>(), emitter.Connection, emitter, Camera.main,
                new[] { toilet, powder }, Array.Empty<RescueNpc>());

            var groundFire = new GameObject("Ordinary fire patches");
            var field = groundFire.AddComponent<FireField>();
            field.SetIgnitionSeeds(Array.Empty<Vector2Int>());
            field.Configure(new Vector3(-4.4f, .1f, -3.2f), 21, 16, .4f);
            field.SetSpreadRate(0f);
            var visuals = groundFire.AddComponent<FirstRescueEffects>();
            visuals.Initialize(null, field);
            groundFire.AddComponent<SprayParticleView>().Initialize(emitter);

            var root = Object.FindAnyObjectByType<WaterLab>();
            var steam = new GameObject("White contact steam");
            steam.transform.SetParent(root.transform, false);
            var steamView = steam.AddComponent<WaterSteamView>();
            var trial = root.gameObject.AddComponent<WaterFireLab>();
            trial.Initialize(field, pan.GetComponent<Collider>(), steamView, SmallFire, LargeFire);
            root.SetFireTrial(trial);
            root.SetSprayWidth(WaterLab.SprayWidth.Concentrated);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save WaterFireLab.");
            Debug.Log("WATER_FIRE_LAB_CREATED");
        }

        static GameObject Copy(GameObject template, Scene scene)
        {
            var clone = Object.Instantiate(template);
            clone.name = template.name;
            if (clone.scene != scene) SceneManager.MoveGameObjectToScene(clone, scene);
            return clone;
        }
    }
}
