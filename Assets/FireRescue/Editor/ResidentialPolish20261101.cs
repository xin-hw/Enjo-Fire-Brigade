using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    // Targeted migration for the currently authored level. Never rebuilds the imported house.
    public static class ResidentialPolish20261101
    {
        public const string Evidence="Docs/Validation/2026-11-01-connected-walls";
        public const string AssetFolder="Assets/FireRescue/Art/Procedural/ConnectedWalls";
        public static readonly string[] AddedDoorIds={"door.001","door.003","door.004"};
        [Serializable] public sealed class Geometry { public Vector3[] vertices,normals; public int[] triangles; }
        [Serializable] sealed class Entry { public string path,mesh; public bool active; public Vector3 min,max; }
        [Serializable] sealed class Probe { public Entry[] doors; public Entry wall; public float timer; }
        public static void BatchApply()
        {
            try{Apply();EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Apply()
        {
            Directory.CreateDirectory(Evidence);
            var scene=EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            var root=GameObject.Find("First Review 1027");if(!root)throw new InvalidOperationException("Wrong scene");
            var house=root.transform.Find("User House 1027");
            var protectedComponents=root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c=>c&&!(c is FirstReviewMission)&&!(c is SprayWallOcclusion))
                .ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
            var protectedColliders=root.GetComponentsInChildren<Collider>(true).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
            var wallFilter=house.Find("wall/Visual").GetComponent<MeshFilter>();
            var originalBounds=AssetDatabase.LoadAssetAtPath<Mesh>(FirstReview1027Setup.Folder+"/wall-Cutaway.asset").bounds;
            var data=JsonUtility.FromJson<Geometry>(File.ReadAllText("ArtSource/Procedural/ConnectedWalls/connected-wall.geometry.json"));
            if(data.vertices.Length!=data.normals.Length||data.triangles.Any(i=>i<0||i>=data.vertices.Length))throw new InvalidDataException("Invalid wall derivative");
            var candidate=new Mesh();candidate.indexFormat=data.vertices.Length>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            candidate.vertices=data.vertices;candidate.normals=data.normals;candidate.triangles=data.triangles;candidate.RecalculateBounds();
            var candidateBounds=candidate.bounds;UnityEngine.Object.DestroyImmediate(candidate);
            if((candidateBounds.min-originalBounds.min).magnitude>.015f||(candidateBounds.max-originalBounds.max).magnitude>.015f)
                throw new InvalidDataException("Wall extent changed beyond 15mm: "+candidateBounds+" vs "+originalBounds+"; original openings must be preserved");
            EnsureFolder(AssetFolder);
            string meshPath=AssetFolder+"/ConnectedWall.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!mesh){mesh=new Mesh{name="Connected square-faced rounded-edge wall"};AssetDatabase.CreateAsset(mesh,meshPath);}
            Undo.RecordObject(mesh,"Update connected wall visual");mesh.Clear();
            mesh.indexFormat=data.vertices.Length>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices=data.vertices;mesh.normals=data.normals;mesh.triangles=data.triangles;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var shader=Shader.Find("FireRescue/ConnectedPlaster");if(!shader)throw new InvalidOperationException("Connected plaster shader missing");
            string materialPath=AssetFolder+"/ConnectedPlaster.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!material){material=new Material(shader){name="Connected matte beige wall"};AssetDatabase.CreateAsset(material,materialPath);}
            material.shader=shader;material.color=new Color(.91f,.87f,.78f);material.SetFloat("_ModuleSize",.72f);
            material.SetFloat("_SeamWidth",.003f);material.SetFloat("_JointContrast",.035f);EditorUtility.SetDirty(material);
            Undo.RecordObjects(new UnityEngine.Object[]{wallFilter,wallFilter.GetComponent<MeshRenderer>()},"Replace wall presentation only");
            wallFilter.sharedMesh=mesh;wallFilter.GetComponent<MeshRenderer>().sharedMaterial=material;
            // Keep its ground shadow, but avoid the cutaway surface's own shadow-map acne.
            wallFilter.GetComponent<MeshRenderer>().receiveShadows=false;
            var geometry=root.GetComponent<SprayWallOcclusion>();var geometryState=new SerializedObject(geometry);var wallReferences=geometryState.FindProperty("walls");
            var walls=new List<Collider>();for(int i=0;i<wallReferences.arraySize;i++){var c=wallReferences.GetArrayElementAtIndex(i).objectReferenceValue as Collider;if(c)walls.Add(c);}
            foreach(string id in AddedDoorIds)
            {
                var owner=house.Find(id).gameObject;Undo.RecordObject(owner,"Enable authored door");owner.SetActive(true);
                var visual=owner.transform.Find("Visual");var bounds=visual.GetComponent<MeshFilter>().sharedMesh.bounds;
                var collider=owner.GetComponent<BoxCollider>();if(!collider)collider=Undo.AddComponent<BoxCollider>(owner);
                Undo.RecordObject(collider,"Door collision");collider.center=bounds.center;
                var size=bounds.size;if(size.x<size.z)size.x+=.05f;else size.z+=.05f;collider.size=size;collider.enabled=true;owner.layer=8;
                var door=owner.GetComponent<BreakableDoor>();if(!door)door=Undo.AddComponent<BreakableDoor>(owner);door.Configure(3,collider);
                var view=owner.GetComponent<BreakableDoorView>();if(!view)view=Undo.AddComponent<BreakableDoorView>(owner);view.Initialize(door,visual.gameObject,null);
                EditorUtility.SetDirty(door);EditorUtility.SetDirty(view);
                if(!walls.Contains(collider))walls.Add(collider);
            }
            wallReferences.arraySize=walls.Count;for(int i=0;i<walls.Count;i++)wallReferences.GetArrayElementAtIndex(i).objectReferenceValue=walls[i];geometryState.ApplyModifiedProperties();
            var mission=root.GetComponentInChildren<FirstReviewMission>();var missionState=new SerializedObject(mission);missionState.FindProperty("deadlineSeconds").floatValue=120;missionState.ApplyModifiedProperties();
            foreach(var pair in protectedComponents)if(EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidDataException("Unrelated gameplay component changed: "+pair.Key.GetType().Name);
            foreach(var pair in protectedColliders)if(EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidDataException("Existing collider changed: "+pair.Key.name);
            AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed");
            File.WriteAllText(Evidence+"/apply.txt","RESIDENTIAL_POLISH_APPLY_PASS\nThree authored doors enabled (front .001, bathroom .003, kitchen back .004); bedroom .002 retained.\nDeadline=120; connected wall derivative "+mesh.vertexCount+" vertices / "+mesh.triangles.Length/3+" triangles.\nProtected existing gameplay components="+protectedComponents.Count+"; existing colliders="+protectedColliders.Count+" all unchanged.\nOnly wall Visual, three doors, geometry references and deadline changed; no level rebuild.\n");
        }
        public static void BatchProbe()
        {
            try
            {
                Directory.CreateDirectory(Evidence);Directory.CreateDirectory("Logs/ConnectedWallWork");
                EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
                var house=GameObject.Find("First Review 1027/User House 1027").transform;
                var wall=house.Find("wall/Visual").GetComponent<MeshFilter>();
                var doors=Enumerable.Range(0,house.childCount).Select(i=>house.GetChild(i)).Where(t=>t.name.StartsWith("door.")).Select(t=>Describe(t.Find("Visual").GetComponent<MeshFilter>())).ToArray();
                var timer=new SerializedObject(UnityEngine.Object.FindAnyObjectByType<FirstReviewMission>()).FindProperty("deadlineSeconds").floatValue;
                File.WriteAllText(Evidence+"/scene-probe.json",JsonUtility.ToJson(new Probe{doors=doors,wall=Describe(wall),timer=timer},true));
                File.WriteAllText("Logs/ConnectedWallWork/wall-input.json",JsonUtility.ToJson(new Geometry{vertices=wall.sharedMesh.vertices,triangles=wall.sharedMesh.triangles}));
                Debug.Log("RESIDENTIAL_PROBE_PASS");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static Entry Describe(MeshFilter filter)
        {
            var bounds=filter.GetComponent<Renderer>().bounds;
            return new Entry{path=AnimationUtility.CalculateTransformPath(filter.transform,null),mesh=AssetDatabase.GetAssetPath(filter.sharedMesh),active=filter.gameObject.activeInHierarchy,min=bounds.min,max=bounds.max};
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
