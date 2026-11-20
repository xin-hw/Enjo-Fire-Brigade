using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    public static class PhysXHoseLabSetup
    {
        public const string ScenePath="Assets/FireRescue/Scenes/PhysXHoseLab.unity";

        public static void Prepare()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                if (!AssetDatabase.CopyAsset(WaterLabSetup.ScenePath,ScenePath))
                    throw new InvalidOperationException("Could not create the independent PhysX hose lab.");
                var scene=EditorSceneManager.OpenScene(ScenePath);
                var input=UnityEngine.Object.FindAnyObjectByType<LocalPlayerInput>();
                var hose=input.GetComponent<HoseConnection>();
                var data=new SerializedObject(hose);
                data.FindProperty("breakUnderTension").boolValue=true;
                data.FindProperty("maximumLength").floatValue=10f;
                data.ApplyModifiedPropertiesWithoutUndo();
                var floor=GameObject.Find("Floor receiving surface");
                floor.transform.localScale=new Vector3(30f,.2f,30f);
                if (input.GetComponent<HoseView>()) input.GetComponent<HoseView>().enabled=false;
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the independent hose lab.");
            }
            EditorSceneManager.OpenScene(ScenePath);
            double ready=EditorApplication.timeSinceStartup+2d;
            void Enter()
            {
                if (EditorApplication.timeSinceStartup<ready) return;
                EditorApplication.update-=Enter;
                EditorApplication.EnterPlaymode();
            }
            EditorApplication.update+=Enter;
        }

        public static void Build()
        {
            string previous=PlayerSettings.bundleVersion;
            try
            {
                PlayerSettings.bundleVersion="0.2.6";
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes=new[]{ScenePath}, target=BuildTarget.StandaloneWindows64,
                    locationPathName="Builds/HosePhysXPreview/FireRescueHoseLab.exe"
                });
                if (result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException(result.summary.result.ToString());
                Debug.Log("PHYSX_HOSE_BUILD_PASS "+result.summary.totalSize);
            }
            finally { PlayerSettings.bundleVersion=previous; }
        }
    }
}
