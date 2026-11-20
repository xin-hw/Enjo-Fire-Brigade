using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    [InitializeOnLoad]
    public static class DoorHeightWallPolish
    {
        public const string Evidence="Docs/Validation/2026-11-01-door-height-wall";
        public const string MeshPath=ResidentialPolish20261101.AssetFolder+"/DoorHeightWall.asset";
        const string Key="FireRescue.DoorHeightWallPolish";
        static bool done;
        static DoorHeightWallPolish(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){done=false;Application.logMessageReceived+=Log;new GameObject("Temporary door-height wall checks").AddComponent<DoorHeightWallDriver>();}};}
        static void Log(string m,string stack,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)Finish(false,m+"\n"+stack);}
        public static void Finish(bool pass,string report){if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;File.WriteAllText(Evidence+"/targeted.txt",(pass?"DOOR_HEIGHT_WALL_PASS\n":"DOOR_HEIGHT_WALL_FAIL\n")+report);EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);}
        public static void BatchApplyAndCheck()
        {
            try
            {
                Directory.CreateDirectory(Evidence);var scene=EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
                var root=GameObject.Find("First Review 1027");var visual=root.transform.Find("User House 1027/wall/Visual");var filter=visual.GetComponent<MeshFilter>();
                var protectedObjects=root.GetComponentsInChildren<MonoBehaviour>(true).Cast<UnityEngine.Object>()
                    .Concat(root.GetComponentsInChildren<Collider>(true)).Concat(root.GetComponentsInChildren<Transform>(true)).Concat(root.GetComponentsInChildren<Renderer>(true))
                    .ToDictionary(o=>o,o=>EditorJsonUtility.ToJson(o));
                var source=AssetDatabase.LoadAssetAtPath<Mesh>(ResidentialPolish20261101.AssetFolder+"/ConnectedWall.asset");
                float top=root.GetComponentsInChildren<BreakableDoor>().Max(d=>d.BlockingCollider.bounds.max.y);
                float localTop=visual.InverseTransformPoint(new Vector3(visual.position.x,top,visual.position.z)).y;
                float bottom=source.bounds.min.y,oldTop=source.bounds.max.y;const float bevel=.035f;
                if(localTop<=bottom+bevel*2)throw new InvalidDataException("Door height cannot accommodate the original bevel");
                var vertices=source.vertices;
                for(int i=0;i<vertices.Length;i++)
                {
                    float y=vertices[i].y;
                    // Keep both 35mm bevel lips unchanged; lengthen only the straight wall.
                    vertices[i].y=y<=bottom+bevel?y:y>=oldTop-bevel?localTop-(oldTop-y):Mathf.Lerp(bottom+bevel,localTop-bevel,(y-bottom-bevel)/(oldTop-bottom-bevel*2));
                }
                var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Connected wall aligned to door tops";mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                var bounds=mesh.bounds;
                if(Mathf.Abs(bounds.min.x-source.bounds.min.x)>.00001f||Mathf.Abs(bounds.max.x-source.bounds.max.x)>.00001f||Mathf.Abs(bounds.min.z-source.bounds.min.z)>.00001f||Mathf.Abs(bounds.max.z-source.bounds.max.z)>.00001f||Mathf.Abs(bounds.max.y-localTop)>.0001f)throw new InvalidDataException("Wall footprint or requested top changed");
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if(!saved){saved=mesh;AssetDatabase.CreateAsset(saved,MeshPath);}else{EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
                Undo.RecordObject(filter,"Align visible walls to door tops");filter.sharedMesh=saved;
                foreach(var pair in protectedObjects)if(EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidDataException("Unrelated object changed: "+pair.Key.name);
                AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed");
                File.WriteAllText(Evidence+"/apply.txt","DOOR_HEIGHT_WALL_APPLY_PASS\nTop world="+top+"; old visual top="+oldTop+"; bevel=.035; vertices="+saved.vertexCount+"\nProtected MonoBehaviours/colliders/transforms/renderers="+protectedObjects.Count+" unchanged. Scene edit only wall MeshFilter reference.\n");
                SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
    public sealed class DoorHeightWallDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label);checks.Add("PASS "+label);}
        IEnumerator Start()
        {
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var f in FindObjectsByType<FireField>())f.Simulate=false;
            foreach(var i in FindObjectsByType<CookingOilIncident>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<FirstReviewMission>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<RescueNpc>())i.Autonomous=false;
            yield return null;
            var root=GameObject.Find("First Review 1027");var wall=root.transform.Find("User House 1027/wall/Visual");var filter=wall.GetComponent<MeshFilter>();var render=wall.GetComponent<Renderer>();
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(ResidentialPolish20261101.AssetFolder+"/ConnectedWall.asset");
            Check(AssetDatabase.GetAssetPath(filter.sharedMesh)==DoorHeightWallPolish.MeshPath,"Scene references the new tall derived mesh, not a scaled user model");
            foreach(var d in root.GetComponentsInChildren<BreakableDoor>())Check(Mathf.Abs(d.BlockingCollider.bounds.max.y-render.bounds.max.y)<.006f,d.name+" wall aligns with its door top within 6mm");
            Check(Mathf.Abs(render.bounds.max.y-1.9f)<.006f,"Visible wall top is 1.90m, up from 0.82m");
            var originalVertices=source.vertices;
            Check(filter.sharedMesh.vertices.Where((v,i)=>v.x!=originalVertices[i].x||v.z!=originalVertices[i].z).Count()==0,"Every vertex preserves original XZ position and doorway footprint");
            Check(filter.sharedMesh.triangles.SequenceEqual(source.triangles),"Wall topology and door openings remain unchanged");
            Check(filter.sharedMesh.vertices.Any(v=>v.y>1.865f&&v.y<1.899f),"Upper rounded lip retains physical depth after raising the wall");
            Check(render.sharedMaterial.shader.name=="FireRescue/ConnectedPlaster"&&render.sharedMaterial.GetFloat("_ModuleSize")==.72f,"Matte beige square face modules retain their size");
            Check(root.GetComponentInChildren<FirstReviewMission>().Remaining>119.5f&&root.GetComponentInChildren<HoseConnection>().MaximumLength==10,"Two-minute mission and 10m hose remain unchanged");
            Check(root.GetComponentsInChildren<DoorFireExposure>().Length==4&&root.GetComponentsInChildren<DoorFireExposure>().All(d=>d.BurnoutSeconds==5),"All four five-second burnable doors remain wired");
            var shader=render.sharedMaterial.shader;Check(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Wall shader compiles on D3D11");
            var player=root.GetComponentInChildren<LocalPlayerInput>();
            FirePresentationDriver.Capture(DoorHeightWallPolish.Evidence+"/spawn.png",player.transform.position+Vector3.up*.3f,6);
            FirePresentationDriver.Capture(DoorHeightWallPolish.Evidence+"/wall-door-height.png",new Vector3(.3f,.6f,1.1f),3.1f);
            FirePresentationDriver.Capture(DoorHeightWallPolish.Evidence+"/whole-level.png",new Vector3(0,.35f,0),8);
            DoorHeightWallPolish.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\nOffscreen D3D11 images inspected separately; no IMGUI, full gameplay run, player build or publishing.\n");
        }
    }
}
