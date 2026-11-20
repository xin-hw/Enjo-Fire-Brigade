using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireRescue.Editor
{
    [InitializeOnLoad]
    public static class ResidentialPolishValidation
    {
        public const string Evidence=ResidentialPolish20261101.Evidence;
        const string Key="FireRescue.ResidentialPolishValidation";
        static bool done;
        static ResidentialPolishValidation()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
                done=false;Application.logMessageReceived+=OnLog;
                new GameObject("Temporary residential polish checks").AddComponent<ResidentialPolishDriver>();
            };
        }
        public static void BatchRun()
        {
            Directory.CreateDirectory(Evidence);EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Finish(false,message+"\n"+stack);}
        public static void Finish(bool pass,string report)
        {
            if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=OnLog;
            File.WriteAllText(Evidence+"/targeted.txt",(pass?"RESIDENTIAL_POLISH_PASS\n":"RESIDENTIAL_POLISH_FAIL\n")+report);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);
        }
    }

    public sealed class ResidentialPolishDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),metrics=new List<string>();
        float began;
        void Check(bool valid,string label)
        {if(!valid)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label+"\n"+string.Join("\n",metrics));checks.Add("PASS "+label);}
        void Update(){if(began>0&&Time.realtimeSinceStartup-began>85)ResidentialPolishValidation.Finish(false,"Timeout\n"+string.Join("\n",checks));}
        IEnumerator Start()
        {
            began=Time.realtimeSinceStartup;DontDestroyOnLoad(gameObject);Freeze();yield return null;
            var root=GameObject.Find("First Review 1027");var house=root.transform.Find("User House 1027");
            var mission=root.GetComponentInChildren<FirstReviewMission>();
            Check(mission.Remaining>119.5f&&Mathf.CeilToInt(mission.Remaining)==120,"Authored mission starts with 120 seconds (02:00)");
            Check(house.GetComponentsInChildren<BreakableDoor>().Length==4,"Three new breakable doors coexist with the original bedroom door");
            Check(!house.Find("door.005").gameObject.activeSelf,"Unrequested detached storage door remains unchanged");
            WallChecks(house);
            CaptureInitial(root);
            AxeAndHoseChecks(root,house);
            NpcChecks(root,house);
            TimerChecks();
            var scenePath=SceneManager.GetActiveScene().path;
            yield return SceneManager.LoadSceneAsync(scenePath);
            Freeze();yield return null;
            root=GameObject.Find("First Review 1027");house=root.transform.Find("User House 1027");mission=root.GetComponentInChildren<FirstReviewMission>();
            Check(house.GetComponentsInChildren<BreakableDoor>().All(d=>!d.IsBroken&&d.RemainingDurability==3&&d.BlockingCollider.enabled),"Scene restart restores all four doors and collisions");
            Check(mission.Remaining>119.5f&&mission.CurrentStatus==FirstReviewMission.Status.Active,"Scene restart restores the two-minute mission");
            Check(root.GetComponentInChildren<HoseConnection>().MaximumLength==10,"Door/wall update keeps the existing 10m hose limit");
            ResidentialPolishValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\n"+string.Join("\n",metrics)+"\nNo scene saved by tests, player build, publishing or screen control. Offscreen images do not include IMGUI.");
        }
        static void Freeze()
        {
            foreach(var input in FindObjectsByType<LocalPlayerInput>())input.ReadDevices=false;
            foreach(var field in FindObjectsByType<FireField>())field.Simulate=false;
            foreach(var incident in FindObjectsByType<CookingOilIncident>())incident.AutomaticTick=false;
            foreach(var mission in FindObjectsByType<FirstReviewMission>())mission.AutomaticTick=false;
            foreach(var npc in FindObjectsByType<RescueNpc>())npc.Autonomous=false;
            foreach(var request in FindObjectsByType<NpcItemRequest>())request.AutomaticTick=false;
        }
        void WallChecks(Transform house)
        {
            var visual=house.Find("wall/Visual");var filter=visual.GetComponent<MeshFilter>();var renderer=visual.GetComponent<Renderer>();
            var shader=renderer.sharedMaterial.shader;
            Check(shader.name=="FireRescue/ConnectedPlaster"&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Connected matte wall shader compiles on D3D11");
            Check(AssetDatabase.GetAssetPath(filter.sharedMesh)==ResidentialPolish20261101.AssetFolder+"/ConnectedWall.asset","Wall Visual uses the derived mesh, not an overwritten user mesh");
            var collider=house.Find("wall").GetComponent<MeshCollider>();
            Check(AssetDatabase.GetAssetPath(collider.sharedMesh)==FirstReview1027Setup.Folder+"/wall-Full.asset","Original full-height wall collision remains authoritative");
            Check(visual.GetComponents<Collider>().Length==0,"Rounded visual adds no separate block colliders or gaps");
            Check(filter.sharedMesh.vertexCount>0&&filter.sharedMesh.vertexCount<20000&&filter.sharedMesh.normals.All(n=>float.IsFinite(n.x)&&float.IsFinite(n.y)&&float.IsFinite(n.z)),"Beveled mesh is finite and bounded, not one object per square");
            Check(filter.sharedMesh.vertices.Any(v=>v.y>.785f&&v.y<.812f),"Outer edge bevel has visible physical depth, not a sub-millimetre clamped edge");
            Check(!renderer.receiveShadows&&renderer.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off,"Wall avoids self-shadow acne but keeps casting its ground shadow");
            Check(renderer.sharedMaterial.GetFloat("_ModuleSize")==.72f&&renderer.sharedMaterial.GetFloat("_SeamWidth")<.005f,"Square face modules touch with flush joints, not separated rounded bricks");
            metrics.Add("WALL vertices="+filter.sharedMesh.vertexCount+" triangles="+filter.sharedMesh.triangles.Length/3+" bounds="+filter.sharedMesh.bounds);
        }
        void CaptureInitial(GameObject root)
        {
            Capture("whole-level.png",new Vector3(0,.35f,0),8);
            Capture("front-door.png",new Vector3(5.8f,.5f,-4.4f),2.5f);
            Capture("bathroom-door.png",new Vector3(.3f,.4f,1.1f),2.7f);
            Capture("back-door.png",new Vector3(-3.4f,.4f,-.4f),2.7f);
            Capture("wall-joints.png",new Vector3(.4f,.45f,-5.7f),1.6f);
        }
        void AxeAndHoseChecks(GameObject root,Transform house)
        {
            var player=root.GetComponentInChildren<LocalPlayerInput>();var tools=player.GetComponent<FirefighterTools>();var controller=player.GetComponent<CharacterController>();
            var geometry=root.GetComponent<SprayWallOcclusion>();
            foreach(string id in ResidentialPolish20261101.AddedDoorIds)
            {
                var owner=house.Find(id);var door=owner.GetComponent<BreakableDoor>();var box=door.BlockingCollider;
                var center=box.bounds.center;var normal=box.bounds.size.x<box.bounds.size.z?Vector3.right:Vector3.forward;
                var from=center-normal*.6f;var to=center+normal*.6f;from.y=to.y=.45f;
                Check(door.MaximumDurability==3&&!door.IsBroken&&box.enabled,id+" starts intact with three-hit durability");
                Check(geometry.TryBlock(from,to,out var hit,out _)&&hit==box,id+" blocks real spray/cable geometry while closed");
                controller.enabled=false;player.transform.SetPositionAndRotation(new Vector3(center.x,.1f,center.z)-normal*.65f,Quaternion.LookRotation(normal));controller.enabled=true;Physics.SyncTransforms();
                var approach=player.transform.position;
                for(int i=0;i<60;i++)controller.Move(normal*.025f);
                Check(Vector3.Dot(player.transform.position-approach,normal)<.6f,id+" physically stops the player before axe destruction");
                controller.enabled=false;player.transform.position=approach;controller.enabled=true;Physics.SyncTransforms();
                tools.Stow();tools.TickPointer(center,true,false,false,false,null,1f/60);
                int hits=tools.HitCount;
                for(int stroke=1;stroke<=3;stroke++)
                {
                    tools.TickPointer(center,true,true,false,false,null,.01f);
                    for(int i=0;i<40;i++)tools.TickPointer(center,false,false,false,false,null,1f/60);
                    Check(door.RemainingDurability==3-stroke&&tools.HitCount-hits==stroke,id+" actual axe stroke "+stroke+" applies exactly one hit");
                }
                Physics.SyncTransforms();
                Check(door.IsBroken&&!box.enabled&&owner.gameObject.activeSelf&&!owner.Find("Visual").gameObject.activeSelf,id+" removes only intact visual and blocking collision after break");
                Check(!geometry.TryBlock(from,to,out _,out _),id+" becomes a clear spray/cable passage after breaking");
                for(int i=0;i<60;i++)controller.Move(normal*.025f);
                Check(Vector3.Dot(player.transform.position-approach,normal)>1.2f,id+" physically lets the player cross the original doorway after destruction");
                tools.Stow();
            }
            foreach(string id in ResidentialPolish20261101.AddedDoorIds)
            {
                var owner=house.Find(id);var door=owner.GetComponent<BreakableDoor>();door.ResetDamageState();owner.GetComponent<BreakableDoorView>().Initialize(door,owner.Find("Visual").gameObject,null);
            }
            Physics.SyncTransforms();
        }
        void NpcChecks(GameObject root,Transform house)
        {
            var field=root.GetComponentInChildren<FireField>();field.ResetSimulation();
            var npc=root.GetComponentInChildren<RescueNpc>();var front=house.Find("door.001").GetComponent<BreakableDoor>();
            var center=front.BlockingCollider.bounds.center;var goal=root.transform.Find("Rescue Exit");
            var player=root.GetComponentInChildren<LocalPlayerInput>();var pc=player.GetComponent<CharacterController>();pc.enabled=false;player.transform.position=new Vector3(3.8f,.1f,-3);pc.enabled=true;
            var cc=npc.GetComponent<CharacterController>();cc.enabled=false;npc.transform.position=new Vector3(center.x-.9f,.1f,center.z);cc.enabled=true;
            npc.SetEscapeRequirement(null);npc.Initialize(field,goal,Array.Empty<ObjectFire>());Physics.SyncTransforms();
            var start=npc.transform.position;for(int i=0;i<90;i++)npc.Tick(1f/60);
            Check(!npc.IsRescued&&!npc.HasSafePath&&(npc.transform.position-start).magnitude<.1f,"NPC waits behind intact front/back doors in an otherwise safe level");
            for(int i=0;i<3;i++)front.ReceiveAxeHit(1);Physics.SyncTransforms();
            for(int i=0;i<480&&!npc.IsRescued;i++)npc.Tick(1f/60);
            metrics.Add("NPC start="+start+" end="+npc.transform.position+" exit="+goal.position+" state="+npc.CurrentState);
            Check(npc.IsRescued,"NPC can physically leave directly through the broken front door");
        }
        void TimerChecks()
        {
            var go=new GameObject("Temporary two-minute timer");go.SetActive(false);var mission=go.AddComponent<FirstReviewMission>();mission.AutomaticTick=false;
            var settings=new SerializedObject(mission);settings.FindProperty("deadlineSeconds").floatValue=120;settings.ApplyModifiedPropertiesWithoutUndo();go.SetActive(true);
            Check(mission.Remaining==120&&mission.CanDeliver,"Two-minute timer accepts delivery at round start");mission.Tick(119.99f);
            Check(mission.CurrentStatus==FirstReviewMission.Status.Active&&mission.Remaining>0,"Timer does not expire before the full two minutes");mission.Tick(.02f);
            Check(mission.CurrentStatus==FirstReviewMission.Status.Failed&&mission.FailureCause==FirstReviewMission.Failure.TimeExpired,"Timer expires correctly after 120 seconds");
            Destroy(go);
        }
        static void Capture(string name,Vector3 focus,float size)
        {FirePresentationDriver.Capture(ResidentialPolishValidation.Evidence+"/"+name,focus,size,50,0);}
    }
}
