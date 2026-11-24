using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FireRescue.Editor
{
    public static class FirePhysicsReviewSetup
    {
        public const string ScenePath = "Assets/FireRescue/Scenes/FirePhysicsReview.unity";
        public const string PlayerPath = "Builds/FireWaterArtReview/FireRescue.exe";

        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("The fire review scene already exists.");
            if (!AssetDatabase.CopyAsset(WaterFireLabSetup.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy the water and fire lab.");

            var scene = EditorSceneManager.OpenScene(ScenePath);
            var lab = Object.FindAnyObjectByType<WaterFireLab>();
            var field = Object.FindAnyObjectByType<FireField>();
            var input = Object.FindAnyObjectByType<LocalPlayerInput>();
            var hose = new SerializedObject(input.GetComponent<HoseConnection>());
            hose.FindProperty("breakUnderTension").boolValue = true;
            hose.ApplyModifiedPropertiesWithoutUndo();
            foreach (var highlight in Object.FindObjectsByType<SourceHighlightView>())
                highlight.enabled = false;
            var divider = GameObject.Find("Divider wall");
            var plaster = divider.GetComponent<Renderer>().sharedMaterial;
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/FireRescue/Art/UserProvided/Level1026/Door.mat");

            // Leave a real aperture in the new lab's partition. No edits to the original labs.
            Object.DestroyImmediate(divider);
            var south = Box("Divider south", new Vector3(1.96f, .99f, -1.93f), new Vector3(.24f, 1.78f, 2.54f), plaster);
            var north = Box("Divider north", new Vector3(1.96f, .99f, 1.73f), new Vector3(.24f, 1.78f, 2.94f), plaster);
            var opening = Box("Airflow door", new Vector3(1.96f, .99f, -.2f), new Vector3(.16f, 1.78f, .92f), wood);
            var door = opening.gameObject.AddComponent<BreakableDoor>();
            door.Configure(3, opening);
            var model = new GameObject("Visual");
            model.transform.SetParent(opening.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = opening.GetComponent<MeshFilter>().sharedMesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = wood;
            Object.DestroyImmediate(opening.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(opening.GetComponent<MeshFilter>());
            door.gameObject.AddComponent<BreakableDoorView>().Initialize(door, model, null);

            var walls = new Collider[]
            {
                south, north, opening, GameObject.Find("Rear wall").GetComponent<Collider>(),
                GameObject.Find("Left wall").GetComponent<Collider>()
            };
            var geometry = lab.GetComponent<SprayWallOcclusion>();
            geometry.Configure(walls, Array.Empty<HoseWindow>());
            var waterData = new SerializedObject(lab.GetComponent<WaterLab>());
            var waterDoors = waterData.FindProperty("doors");
            waterDoors.arraySize = 1;
            waterDoors.GetArrayElementAtIndex(0).objectReferenceValue = door;
            waterData.ApplyModifiedPropertiesWithoutUndo();
            field.gameObject.AddComponent<FireSpreadBarrier>().Initialize(field, geometry, new[] { door });
            field.SetSpreadRate(.032f);
            Physics.SyncTransforms();
            var fixedObstacles = walls.Where(wall => wall != opening)
                .Append(GameObject.Find("Counter receiving surface").GetComponent<Collider>()).ToArray();
            var fuelCells = Enumerable.Range(0, field.Width * field.Height)
                .Select(index => new Vector2Int(index % field.Width, index / field.Width))
                .Where(cell => fixedObstacles.Any(wall =>
                    wall.bounds.Contains(field.CellCenter(cell.x, cell.y) + Vector3.up * .06f)))
                .ToArray();
            field.SetNonFlammableCells(fuelCells);
            field.GetComponent<FirstRescueEffects>().enabled = false;

            var airflow = field.gameObject.AddComponent<FireAirflowSolver>();
            var airflowData = new SerializedObject(airflow);
            airflowData.FindProperty("obstacleMask").intValue = 1 << 8;
            airflowData.ApplyModifiedPropertiesWithoutUndo();
            airflow.Initialize(field, fixedObstacles.Append(opening).ToArray(), new[] { door });
            field.gameObject.AddComponent<FireAirflowFlameView>();
            lab.gameObject.AddComponent<FirePhysicsReviewControls>().Initialize(airflow, input);

            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save the fire review scene.");
            Debug.Log("FIRE_PHYSICS_REVIEW_SCENE_SAVED");
        }

        static BoxCollider Box(string name, Vector3 center, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = 8;
            box.transform.SetPositionAndRotation(center, Quaternion.identity);
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box.GetComponent<BoxCollider>();
        }

        public static void Build()
        {
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64
            });
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Fire review build {result.summary.result}.");
            Debug.Log($"FIRE_PHYSICS_REVIEW_BUILD_PASS {result.summary.totalSize} bytes");
        }
    }
}
