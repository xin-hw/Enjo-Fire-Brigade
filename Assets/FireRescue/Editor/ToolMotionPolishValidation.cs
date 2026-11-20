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
    public static class ToolMotionPolishValidation
    {
        public const string Evidence="Docs/Validation/2026-11-01-tool-motion";
        const string Key="FireRescue.ToolMotionPolish";
        public static bool Baseline=>SessionState.GetBool(Key+".Baseline",false);
        static bool done;
        static ToolMotionPolishValidation(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){done=false;Application.logMessageReceived+=Log;new GameObject("Temporary tool motion review").AddComponent<ToolMotionPolishDriver>();}};}
        public static void BatchBaseline(){SessionState.SetBool(Key+".Baseline",true);Begin();}
        public static void BatchRun(){SessionState.SetBool(Key+".Baseline",false);Begin();}
        static void Begin(){Directory.CreateDirectory(Evidence);EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Log(string m,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Finish(false,m+"\n"+stack);}
        public static void Finish(bool pass,string text){if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;File.WriteAllText(Evidence+(Baseline?"/baseline.txt":"/targeted.txt"),(pass?"TOOL_MOTION_PASS\n":"TOOL_MOTION_FAIL\n")+text);EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);}
    }
    public sealed class ToolMotionPolishDriver:MonoBehaviour
    {
        readonly List<string> report=new List<string>();
        int checks;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(string.Join("\n",report)+"\nFAIL "+label);checks++;report.Add("PASS "+label);}
        IEnumerator Start()
        {
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var f in FindObjectsByType<FireField>())f.Simulate=false;
            foreach(var i in FindObjectsByType<CookingOilIncident>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<FirstReviewMission>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<RescueNpc>())i.Autonomous=false;
            foreach(var i in FindObjectsByType<PlayerFireContact>())i.AutomaticTick=false;
            yield return null;
            var input=FindAnyObjectByType<LocalPlayerInput>();var actor=input.transform;var cc=input.GetComponent<CharacterController>();cc.enabled=false;actor.SetPositionAndRotation(new Vector3(40,.1f,40),Quaternion.identity);cc.enabled=true;
            var tools=input.GetComponent<FirefighterTools>();var motion=input.GetComponent<FirefighterCartoonMotion>();motion.AutomaticTick=false;var emitter=input.GetComponent<SprayEmitter>();emitter.AutomaticSimulation=false;
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Temporary pose review floor";ground.transform.position=new Vector3(40,-.05f,40);ground.transform.localScale=new Vector3(6,.3f,6);
            var mat=new Material(Shader.Find("Standard")){color=new Color(.72f,.71f,.67f)};ground.GetComponent<Renderer>().sharedMaterial=mat;
            string prefix=ToolMotionPolishValidation.Baseline?"baseline-":"";
            foreach(var t in actor.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Arm")||t.name.Contains("Hand")||t.name.Contains("Mount")||t.name.Contains("Pivot")||t.parent&&t.parent.name=="Anchors"))
                report.Add(t.name+" actorPos="+actor.InverseTransformPoint(t.position)+" actorRot="+(Quaternion.Inverse(actor.rotation)*t.rotation).eulerAngles+" local="+t.localPosition+" scale="+t.lossyScale);
            tools.EquipHose();for(int i=0;i<20;i++)motion.TickPresentation(1f/60);yield return null;
            Capture(prefix+"hose.png",actor,150);Capture(prefix+"hose-side.png",actor,90);
            report.Add("Hose wrists="+motion.LeftGripError+","+motion.RightGripError);
            tools.Stow();tools.TickPointer(actor.position+Vector3.forward*4,true,false,false,false,null,1f/60);
            for(int i=0;i<20;i++)motion.TickPresentation(1f/60);yield return null;Capture(prefix+"axe-ready.png",actor,150);
            tools.TickPointer(actor.position+Vector3.forward*4,true,true,false,false,null,.01f);
            for(int i=0;i<15;i++){tools.TickPointer(actor.position+Vector3.forward*4,false,false,false,false,null,1f/60);motion.TickPresentation(1f/60);}
            yield return null;
            Capture(prefix+"axe-strike.png",actor,150);
            if(!ToolMotionPolishValidation.Baseline)yield return Detailed(input,tools,motion,emitter);
            ToolMotionPolishValidation.Finish(true,"Checks="+checks+"\n"+string.Join("\n",report)+"\nPose fixture and actual tools; no scene saved, player build, publishing or screen control.\n");
        }
        IEnumerator Detailed(LocalPlayerInput input,FirefighterTools tools,FirefighterCartoonMotion motion,SprayEmitter emitter)
        {
            Transform actor=input.transform;var hose=input.GetComponent<HoseConnection>();
            Vector3 muzzle=actor.InverseTransformPoint(emitter.NozzlePosition),inlet=actor.InverseTransformPoint(emitter.HoseInletPosition);
            var bones=actor.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("UpperArm")||t.name.Contains("LowerArm")||t.name.Contains("Hand")).ToArray();
            var scales=bones.Select(t=>t.localScale).ToArray();
            var oldView=actor.GetComponentInChildren<FirefighterToolsView>(true);Check(oldView&&!oldView.enabled,"Legacy tool pose writer remains disabled; current rig owns presentation");
            tools.Stow();for(int i=0;i<35;i++)motion.TickPresentation(1f/60);
            Check(motion.HosePoseBlend<.01f&&motion.AxePoseBlend<.01f,"Stowing blends arms back to the idle pose");
            tools.EquipHose();motion.TickPresentation(1f/60);Check(motion.HosePoseBlend>0&&motion.HosePoseBlend<.8f,"Equip starts a transition, not a one-frame hand teleport");
            for(int i=0;i<30;i++)motion.TickPresentation(1f/60);
            Check(motion.HosePoseBlend>.99f&&motion.LeftGripError<.03f&&motion.RightGripError<.03f,"Both hose wrists settle within 3cm of calibrated tool-local grips");
            float maxGrip=0,maxMuzzle=0;var start=actor.position;
            for(int i=0;i<60;i++){actor.position+=Vector3.forward*.03f;motion.TickPresentation(1f/60);maxGrip=Mathf.Max(maxGrip,motion.LeftGripError,motion.RightGripError);maxMuzzle=Mathf.Max(maxMuzzle,Vector3.Distance(motion.VisualMuzzlePosition,emitter.NozzlePosition));}
            report.Add("Moving hose max wrist error="+maxGrip+" visual muzzle gap="+maxMuzzle);
            Check(maxGrip<.045f,"Walking retains both grips without stretching the short arms");
            Check(maxMuzzle<.025f,"Moving visible muzzle stays within 2.5cm of the independent gameplay muzzle");
            for(int i=0;i<20;i++)motion.TickPresentation(1f/60);Check(motion.MovementBlend<.01f,"Stopping smoothly returns the gait to idle");
            var source=new GameObject("Temporary animation water source").AddComponent<FluidSource>();source.transform.position=actor.position-Vector3.forward*.6f;source.Configure(ElementKind.Water);
            source.SetSprayProfile(FindObjectsByType<FluidSource>().First(s=>s!=source&&s.Element==ElementKind.Water).Profile);
            hose.Disconnect();Check(hose.TryConnect(source),"Animation fixture connects the existing hose system without changing its length");
            var fill=new GameObject("Temporary pose-review fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.5f;fill.transform.rotation=Quaternion.Euler(25,155,0);
            string sprayFrames="Logs/ToolMotionSprayFrames",axeFrames="Logs/ToolMotionAxeFrames";Directory.CreateDirectory(sprayFrames);Directory.CreateDirectory(axeFrames);
            Time.captureDeltaTime=1f/15;
            for(int frame=0;frame<48;frame++)
            {
                for(int sub=0;sub<4;sub++){tools.Tick(actor.position+Vector3.forward*5,false,frame>=8&&frame<38,1f/60);emitter.Advance(1f/60);motion.TickPresentation(1f/60);}
                yield return null;
                FirePresentationDriver.Capture(sprayFrames+"/frame"+frame.ToString("D3")+".png",actor.position+new Vector3(0,.8f,.1f),1.25f,22,125);
                if(frame==24){Check(motion.SprayBraceBlend>.95f,"Actual spraying settles into the recoil/braced upper-body pose");Capture("hose-spray.png",actor,150);}
            }
            Check(motion.SprayBraceBlend<.03f,"Releasing spray eases the brace back without changing selected tool");
            var doorGo=GameObject.CreatePrimitive(PrimitiveType.Cube);doorGo.name="Temporary motion damage target";doorGo.transform.position=actor.position+new Vector3(0,.8f,1);doorGo.transform.localScale=new Vector3(1,1.5f,.1f);
            var door=doorGo.AddComponent<BreakableDoor>();door.Configure(3,doorGo.GetComponent<Collider>());Physics.SyncTransforms();
            hose.Disconnect();tools.Stow();tools.TickPointer(actor.position+Vector3.forward*5,true,false,false,false,null,1f/60);
            Check(tools.SelectedTool==FirefighterTools.Tool.Axe&&!tools.IsSwinging,"First left click still equips without striking");
            for(int i=0;i<30;i++)motion.TickPresentation(1f/60);
            tools.TickPointer(actor.position+Vector3.forward*5,true,true,false,false,null,.001f);
            int hits=tools.HitCount;float axeGrip=0;
            for(int i=0;i<40;i++){tools.TickPointer(actor.position+Vector3.forward*5,false,false,false,false,null,1f/60);motion.TickPresentation(1f/60);axeGrip=Mathf.Max(axeGrip,motion.RightGripError);}
            report.Add("Axe max active wrist error="+axeGrip);
            Check(axeGrip<.06f,"Axe wind-up/contact/recovery keeps the holding wrist within 6cm");
            Check(!tools.IsSwinging&&tools.HitCount-hits==1&&door.RemainingDurability==2,"One click still delivers one damage event and completes one stroke");
            var shoulder=actor.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Chest");var idleRot=shoulder.rotation;
            // Keep the real collider/durability active while leaving the review camera
            // an unobstructed view of the animation, not the back of the test target.
            doorGo.GetComponent<Renderer>().enabled=false;
            int swings=tools.SwingCount;tools.TickPointer(actor.position+Vector3.forward*5,true,true,false,false,null,.001f);
            float maxTwist=0;
            for(int frame=0;frame<48;frame++)
            {
                for(int sub=0;sub<4;sub++){tools.TickPointer(actor.position+Vector3.forward*5,false,true,false,false,null,1f/60);motion.TickPresentation(1f/60);maxTwist=Mathf.Max(maxTwist,Quaternion.Angle(idleRot,shoulder.rotation));}
                yield return null;
                FirePresentationDriver.Capture(axeFrames+"/frame"+frame.ToString("D3")+".png",actor.position+Vector3.up*.8f,1.25f,22,150);
                if(frame==11)Capture("axe-follow-through.png",actor,90);
            }
            Check(tools.SwingCount>swings+3&&door.IsBroken,"Holding still repeats axe strokes and breaks the real damage target");
            Check(maxTwist>8,"Shoulders and torso participate instead of only rotating the axe prop");
            Check(bones.Select((b,i)=>b.localScale==scales[i]).All(v=>v),"Arm posing never stretches bone scale");
            Check(Vector3.Distance(muzzle,actor.InverseTransformPoint(emitter.NozzlePosition))<.00001f&&Vector3.Distance(inlet,actor.InverseTransformPoint(emitter.HoseInletPosition))<.00001f,"Muzzle and hose inlet gameplay anchor offsets never changed");
            var pos=actor.position;motion.TickPresentation(float.NaN);motion.TickPresentation(float.PositiveInfinity);motion.TickPresentation(-1);Check(actor.position==pos,"Invalid animation dt cannot move or poison the actor");
            tools.Stow();for(int i=0;i<35;i++)motion.TickPresentation(1f/60);Check(motion.AxePoseBlend<.01f&&motion.HosePoseBlend<.01f,"Switching back to empty hands clears both tool pose weights");
            var selected=tools.SelectedTool;motion.enabled=false;Check(tools.SelectedTool==selected&&actor.position==pos,"Disabling presentation never mutates tools or actor authority");motion.enabled=true;motion.AutomaticTick=false;motion.TickPresentation(1f/60);
            Check(motion.MovementBlend<.01f&&float.IsFinite(motion.RightGripError),"Re-enable resets sampling cleanly without gait burst or NaNs");
            motion.enabled=false;var rig=actor.Find("Visual/Swap Player Role2");motion.Initialize(actor,rig,tools,input.GetComponent<PlayerCarryInteraction>(),actor.Find("Visual/Nozzle Mount"),actor.Find("Visual/Axe Pivot"));motion.enabled=true;motion.AutomaticTick=false;
            tools.EquipHose();for(int i=0;i<30;i++)motion.TickPresentation(1f/60);Check(motion.LeftGripError<.03f&&motion.RightGripError<.03f,"Explicit Initialize also rebuilds the spine/grip basis for builder-created rigs");
            Time.captureDeltaTime=0;Destroy(source.gameObject);Destroy(doorGo);Destroy(fill.gameObject);
            report.Add("Fixture travel="+Vector3.Distance(start,actor.position)+"; no scene/asset saved; review-only fill light was temporary.");
        }
        void OnDestroy(){Time.captureDeltaTime=0;}
        static void Capture(string name,Transform actor,float yaw)=>FirePresentationDriver.Capture(ToolMotionPolishValidation.Evidence+"/"+name,actor.position+Vector3.up*.8f,1.12f,22,yaw);
    }
}
