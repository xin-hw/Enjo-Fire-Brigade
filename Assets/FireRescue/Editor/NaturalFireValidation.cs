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
    public static class NaturalFireValidation
    {
        const string Key="FireRescue.NaturalFireChecks";
        static bool done;
        static NaturalFireValidation(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){done=false;Application.logMessageReceived+=Log;new GameObject("Temporary natural fire checks").AddComponent<NaturalFireDriver>();}};}
        public static void BatchRun(){Directory.CreateDirectory(NaturalFireSetup.Evidence);EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Log(string m,string stack,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)Finish(false,m+"\n"+stack);}
        public static void Finish(bool pass,string report){if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;File.WriteAllText(NaturalFireSetup.Evidence+"/targeted.txt",(pass?"NATURAL_FIRE_PASS\n":"NATURAL_FIRE_FAIL\n")+report);EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);}
    }
    public sealed class NaturalFireDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),metrics=new List<string>();float began;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label+"\n"+string.Join("\n",metrics));checks.Add("PASS "+label);}
        void Update(){if(began>0&&Time.realtimeSinceStartup-began>180)NaturalFireValidation.Finish(false,"Timeout\n"+string.Join("\n",checks));}
        static void Freeze()
        {
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var i in FindObjectsByType<FireField>())i.Simulate=false;
            foreach(var i in FindObjectsByType<CookingOilIncident>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<FirstReviewMission>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<RescueNpc>())i.Autonomous=false;
            foreach(var i in FindObjectsByType<NpcItemRequest>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<PlayerFireContact>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<DoorFireExposure>())i.AutomaticTick=false;
        }
        IEnumerator Start()
        {
            began=Time.realtimeSinceStartup;DontDestroyOnLoad(gameObject);Freeze();yield return null;
            DoorAndBarrier();VisualCoverage();
            var shader=Resources.Load<Shader>("FlowingFire");Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Fire shader compiles on D3D11");
            var root=GameObject.Find("First Review 1027");var field=root.GetComponentInChildren<FireField>();var incident=root.GetComponentInChildren<CookingOilIncident>();
            var fx=root.GetComponentInChildren<FirstRescueEffects>();var potView=root.GetComponentInChildren<OilPanFireView>();potView.AutomaticTick=false;
            var doors=root.GetComponentsInChildren<DoorFireExposure>();
            Check(doors.Length==4&&doors.All(d=>d.BurnoutSeconds==5),"All four playable doors have five-second fire exposure");
            Check(root.GetComponentInChildren<FirstReviewMission>().Remaining>119.5f,"Two-minute deadline remains intact");
            Check(root.GetComponentInChildren<HoseConnection>().MaximumLength==10,"Hose remains 10m and water profiles are not retuned");
            foreach(string room in NaturalFireSetup.RoomNames){var b=root.transform.Find(room).GetComponent<Renderer>().bounds;int fuel=Count(field,(p,s)=>s.Fuel>0&&Inside(b,p));Check(fuel>0,room+" has actual combustible floor, not kitchen-only");}
            int kitchen=Count(field,(p,s)=>s.Fuel>0&&Inside(root.transform.Find("Kitchen floor").GetComponent<Renderer>().bounds,p));
            Check(field.BurningCount==0,"Round starts at the burning pot, not a prefilled floor grid");
            float rate=field.SpreadRate;int first=0,at5=0,outside=0;
            var kitchenBounds=root.transform.Find("Kitchen floor").GetComponent<Renderer>().bounds;
            for(int step=1;step<=2400;step++)
            {
                incident.Tick(.05f);field.Step(.05f);foreach(var d in doors)d.Tick(.05f);
                if(step==20)first=field.BurningCount;
                if(step==100)at5=field.BurningCount;
                if(step==100||step==300||step==600||step==1200||step==2400)
                {
                    float t=step*.05f;outside=Count(field,(p,s)=>s.IsBurning&&!Inside(kitchenBounds,p));
                    metrics.Add("t="+t+"s burning="+field.BurningCount+" outsideKitchen="+outside+" doorsBroken="+doors.Count(d=>d.GetComponent<BreakableDoor>().IsBroken));
                    fx.TickPresentation(0);potView.TickPresentation(0);foreach(var v in root.GetComponentsInChildren<DoorBurnView>())v.TickPresentation();
                    Capture("level-"+t.ToString("0")+"s.png",new Vector3(0,.35f,0),8);
                    if(step==600)Preview(fx,potView);
                }
            }
            Check(first>0&&at5>first&&at5<kitchen*.85f,"Fire expands from a local seed without scheduled five-second kitchen flashover");
            Check(Mathf.Approximately(rate,field.SpreadRate),"No opening fast/slow spread-rate switch remains");
            Check(outside>30,"Unattended fire genuinely spreads beyond the kitchen");
            Check(doors.All(d=>d.GetComponent<BreakableDoor>().IsBroken),"Unattended whole-house progression reaches and burns all four contacted doors");
            Check(Count(field,(p,s)=>s.IsBurning&&Inside(root.transform.Find("Bedroom floor").GetComponent<Renderer>().bounds,p))>20,"Fire can cross the burned bedroom threshold into the room, not stop at a visual floor seam");
            fx.TickPresentation(0);Check(fx.ActiveHeatMarkCount==field.BurningCount,"Every live whole-house fire cell keeps its danger marker");
            var path=SceneManager.GetActiveScene().path;yield return SceneManager.LoadSceneAsync(path);Freeze();yield return null;
            root=GameObject.Find("First Review 1027");
            Check(root.GetComponentsInChildren<BreakableDoor>().All(d=>!d.IsBroken&&d.BlockingCollider.enabled),"Real scene reload restores all doors and collisions");
            Check(root.GetComponentsInChildren<DoorFireExposure>().All(d=>d.ExposureSeconds==0)&&root.GetComponentInChildren<FireField>().BurningCount==0,"Reload clears exposure and restarts local ignition, not old whole-room fire");
            NaturalFireValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\n"+string.Join("\n",metrics)+"\nAutomated isolated/real-scene simulation; no human playthrough, player build, publish, screen control or test scene save. Captures omit IMGUI.");
        }
        void DoorAndBarrier()
        {
            var go=new GameObject("Temporary fire barrier fixture");var f=go.AddComponent<FireField>();f.Simulate=false;f.SetIgnitionSeeds(Array.Empty<Vector2Int>());f.Configure(new Vector3(60,.1f,60),3,1,.25f);f.SetSpreadRate(1);
            var dgo=GameObject.CreatePrimitive(PrimitiveType.Cube);dgo.transform.SetParent(go.transform);dgo.transform.position=new Vector3(60.25f,.65f,60.125f);dgo.transform.localScale=new Vector3(.1f,1.1f,.25f);dgo.layer=8;
            var box=dgo.GetComponent<BoxCollider>();var door=dgo.AddComponent<BreakableDoor>();door.Configure(3,box);
            var visual=new GameObject("Intact test door");visual.transform.SetParent(dgo.transform);var view=dgo.AddComponent<BreakableDoorView>();view.Initialize(door,visual,null);
            var exposure=dgo.AddComponent<DoorFireExposure>();exposure.AutomaticTick=false;exposure.Initialize(f,door);
            var geo=go.AddComponent<SprayWallOcclusion>();geo.Configure(new[]{box},Array.Empty<HoseWindow>());var barrier=go.AddComponent<FireSpreadBarrier>();barrier.Initialize(f,geo,new[]{door});Physics.SyncTransforms();
            var a=f.CellCenter(0,0);var b=f.CellCenter(1,0);f.Ignite(a,1);
            for(int i=0;i<100;i++)f.Step(.02f);
            Check(!f.GetCell(1,0).IsBurning&&!f.CanTransferHeat(a,b),"Intact door blocks natural heat spread, not only player collision");
            int events=0;door.Broken+=_=>events++;
            exposure.Tick(4.99f);Check(!door.IsBroken&&exposure.IsExposed,"Burning contact for 4.99 seconds does not destroy the door early");
            exposure.Tick(.01f);Physics.SyncTransforms();Check(door.IsBroken&&!box.enabled&&!visual.activeSelf&&events==1,"Exactly five seconds burns away the door and its collider once");
            exposure.Tick(10);Check(!door.BurnDown()&&events==1,"Fire cannot repeat a destruction event on an already broken door");
            for(int i=0;i<100;i++)f.Step(.02f);Check(f.GetCell(1,0).IsBurning&&f.CanTransferHeat(a,b),"Burn event invalidates cached edge and heat flows through the opened doorway");
            door.ResetDamageState();view.Initialize(door,visual,null);Check(!f.CanTransferHeat(a,b),"Explicit door reset also invalidates a previously open heat edge");f.ResetSimulation();f.Ignite(a,1);exposure.Initialize(f,door);exposure.Tick(3);
            f.ApplyWater(a,10,.1f);exposure.Tick(.1f);Check(exposure.ExposureSeconds==0&&!door.IsBroken,"Extinguishing door contact interrupts and resets continuous burn time");
            f.ResetSimulation();f.Ignite(a,1);exposure.enabled=false;exposure.Tick(8);Check(!door.IsBroken&&exposure.ExposureSeconds==0,"Disabled exposure cannot destroy a door via direct Tick");exposure.enabled=true;
            exposure.Tick(2);Check(!door.IsBroken,"Renewed contact must earn a fresh full five seconds");exposure.Tick(3);Check(door.IsBroken,"Five new seconds after interruption still destroys the door");
            door.ResetDamageState();view.Initialize(door,visual,null);f.ResetSimulation();f.Ignite(f.CellCenter(2,0),1);exposure.Initialize(f,door);exposure.Tick(8);Check(!door.IsBroken,"Remote fire does not burn a door through proximity alone");
            door.ReceiveAxeHit(1);door.ReceiveAxeHit(1);Check(!door.IsBroken,"Two axe hits retain the existing three-hit rule");door.ReceiveAxeHit(1);Check(door.IsBroken&&!visual.activeSelf,"Axe still breaks the door early using the same route/view transition");
            var pan=new GameObject("Temporary oil pot");pan.transform.SetParent(go.transform);pan.transform.position=a;
            var pot=pan.AddComponent<ObjectFire>();pot.Configure(FireKind.CookingOil,1);pot.ConfigureWaterEscalation(.65f,3);
            var food=new GameObject("Temporary pudding").AddComponent<CarryItem>();food.transform.SetParent(go.transform);food.transform.position=a;
            var incident=pan.AddComponent<CookingOilIncident>();incident.AutomaticTick=false;incident.Initialize(pot,f,food);
            pot.Receive(ElementKind.Water,.1f);pot.Receive(ElementKind.Water,.1f);Check(incident.ExplosionCount==1&&food.IsBurnt&&pot.IsBurning,"Water still causes one oil-pot burst and scorches pudding");pot.Receive(ElementKind.DryPowder,10);Check(!pot.IsBurning,"Powder still extinguishes the actual oil-pot source");
            go.SetActive(false);Destroy(go);
        }
        void VisualCoverage()
        {
            var go=new GameObject("Temporary large fire presentation");var field=go.AddComponent<FireField>();field.Simulate=false;field.SetIgnitionSeeds(Array.Empty<Vector2Int>());field.Configure(new Vector3(100,.1f,100),40,30,.25f);
            for(int z=0;z<field.Height;z++)for(int x=0;x<field.Width;x++)field.Ignite(field.CellCenter(x,z),.95f);
            // Set the real scene's 512 capacity before Awake builds the fixed pool.
            var viewRoot=new GameObject("Temporary bounded fire view");viewRoot.transform.SetParent(go.transform);viewRoot.SetActive(false);
            var fx=viewRoot.AddComponent<FirstRescueEffects>();var s=new SerializedObject(fx);s.FindProperty("spaciousFire").boolValue=true;s.FindProperty("fireCapacity").intValue=512;s.ApplyModifiedPropertiesWithoutUndo();viewRoot.SetActive(true);fx.Initialize(null,field);fx.TickPresentation(0);
            var danger=fx.transform.Find("Combined burning floor footprint").GetComponent<MeshFilter>();
            Check(field.BurningCount==1200&&fx.ActiveHeatMarkCount==1200&&danger.sharedMesh.vertexCount==4800,"Merged danger mesh covers 1200 burning cells without the old 512-cell cutoff");
            Check(fx.ActiveFlameCount<=512&&fx.ActiveFlameCount>0,"Visible flame count remains bounded independently of hazard area");
            Check(fx.ActiveFireAtmosphereCount>0&&fx.ActiveFireAtmosphereCount<=40&&go.GetComponentsInChildren<Light>().Length==0,"Rising smoke and embers stay capped at 40 without per-cell lights");
            var flames=go.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Orange Flame"&&r.enabled).ToArray();
            Check(flames.Any(r=>r.transform.position.z>field.Origin.z+6)&&flames.Any(r=>r.transform.position.x>field.Origin.x+8),"Sparse flame selection reaches the far rows and columns, not just scan-order cells");
            var snapshot=Snapshot(field);fx.TickPresentation(.2f);Check(snapshot.SequenceEqual(Snapshot(field)),"Fire presentation never modifies fire, fuel, water or powder state");
            s.Update();s.FindProperty("showFire").boolValue=false;s.ApplyModifiedPropertiesWithoutUndo();fx.TickPresentation(0);Check(fx.ActiveFireAtmosphereCount==0&&fx.ActiveHeatMarkCount==0,"Show Fire toggle clears both smoke and merged hazard view");
            fx.enabled=false;Check(field.BurningCount==1200&&fx.ActiveHeatMarkCount==0&&fx.ActiveFireAtmosphereCount==0,"Hiding fire visuals leaves every gameplay hazard intact");
            go.SetActive(false);Destroy(go);
        }
        void Preview(FirstRescueEffects fx,OilPanFireView pan)
        {
            string dir="Logs/NaturalFirePreview";Directory.CreateDirectory(dir);
            for(int i=0;i<48;i++){fx.TickPresentation(1f/15);pan.TickPresentation(1f/15);FirePresentationDriver.Capture(dir+"/frame"+i.ToString("D3")+".png",new Vector3(-2,.4f,-1.5f),6);}
            Capture("fire-close.png",new Vector3(-3.7f,.4f,-2.2f),2.6f);
        }
        static bool Inside(Bounds b,Vector3 p)=>p.x>b.min.x&&p.x<b.max.x&&p.z>b.min.z&&p.z<b.max.z;
        static int Count(FireField f,Func<Vector3,FireCellState,bool> predicate){int n=0;for(int z=0;z<f.Height;z++)for(int x=0;x<f.Width;x++)if(predicate(f.CellCenter(x,z),f.GetCell(x,z)))n++;return n;}
        static float[] Snapshot(FireField f){var s=new float[f.Width*f.Height*5];int n=0;for(int z=0;z<f.Height;z++)for(int x=0;x<f.Width;x++){var c=f.GetCell(x,z);s[n++]=c.Intensity;s[n++]=c.Fuel;s[n++]=c.Wetness;s[n++]=c.Oil;s[n++]=c.Powder;}return s;}
        static void Capture(string name,Vector3 focus,float size)=>FirePresentationDriver.Capture(NaturalFireSetup.Evidence+"/"+name,focus,size);
    }
}
