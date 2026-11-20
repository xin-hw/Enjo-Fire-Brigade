using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    public static class ReviewWaterIntegration
    {
        public const string Version = "0.2.6";
        public const string PlayerPath = "Builds/Local-v0.2.6/FireRescue.exe";

        public static void Validate()
        {
            double ready = EditorApplication.timeSinceStartup + 3d;
            void Enter()
            {
                if (EditorApplication.timeSinceStartup < ready) return;
                EditorApplication.update -= Enter;
                EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
                EditorApplication.EnterPlaymode();
            }
            EditorApplication.update += Enter;
        }

        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            var effects = UnityEngine.Object.FindAnyObjectByType<FirstRescueEffects>();
            if (effects.GetComponent<WaterLab>()) throw new InvalidOperationException("Water is already integrated.");
            var spray = UnityEngine.Object.FindAnyObjectByType<SprayEmitter>();
            var source = UnityEngine.Object.FindObjectsByType<FluidSource>().Single(item => item.Element == ElementKind.Water);
            var pan = UnityEngine.Object.FindObjectsByType<ObjectFire>().Single(item => item.Kind == FireKind.CookingOil);
            var field = UnityEngine.Object.FindAnyObjectByType<FireField>();
            var reaction = UnityEngine.Object.FindAnyObjectByType<ElementReaction>();
            var doors = UnityEngine.Object.FindObjectsByType<BreakableDoor>();
            var floor = GameObject.Find("Ground").GetComponent<Collider>();
            var counter = GameObject.Find("kitchen counter").GetComponent<Collider>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FireRescue/Art/Shaders/WaterLab.shader");
            if (!spray || !floor || !counter || !shader || !field || !reaction || !Camera.main)
                throw new InvalidOperationException("Missing water integration references.");
            var water = effects.gameObject.AddComponent<WaterLab>();
            water.InitializeLevel(spray, source, Camera.main, floor, counter, shader, field, reaction, pan, doors);
            effects.UseSurfaceWater();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save FirstReview1027.");
            Debug.Log("REVIEW_WATER_INTEGRATED");
        }

        public static void Build()
        {
            var previousVersion = PlayerSettings.bundleVersion;
            try
            {
                string version = Environment.GetEnvironmentVariable("FIRERESCUE_BUILD_VERSION");
                if (string.IsNullOrEmpty(version)) version = Version;
                if (!System.Version.TryParse(version, out _)) throw new ArgumentException($"Invalid build version {version}.");
                PlayerSettings.bundleVersion = version;
                string location = version == Version ? PlayerPath : $"Builds/Local-v{version}/FireRescue.exe";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { FirstReview1027Setup.ScenePath },
                    locationPathName = location,
                    target = BuildTarget.StandaloneWindows64
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException(report.summary.result.ToString());
                Debug.Log($"REVIEW_WATER_BUILD_PASS {version} {report.summary.totalSize} bytes at {location}");
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
            }
        }
    }
}
