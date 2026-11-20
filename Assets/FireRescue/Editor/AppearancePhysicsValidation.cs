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
    public static class AppearancePhysicsValidation
    {
        public const string Evidence="Docs/Validation/2026-10-29-appearance-physics";
        const string Key="FireRescue.AppearancePhysicsValidation";
        static bool finished;
        static AppearancePhysicsValidation()
        {
            EditorApplication.playModeStateChanged+=s=>
            {
                if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
                finished=false;Application.logMessageReceived+=OnError;
                new GameObject("Temporary appearance/physics checks").AddComponent<AppearancePhysicsDriver>();
            };
        }
        public static void BatchRun()
        {
            Directory.CreateDirectory(Evidence);
            EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void OnError(string text,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Finish(false,text+"\n"+stack);}
        public static void Finish(bool success,string result)
        {
            if(finished)return;finished=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=OnError;
            File.WriteAllText(Evidence+"/targeted.txt",(success?"APPEARANCE_PHYSICS_PASS\n":"APPEARANCE_PHYSICS_FAIL\n")+result);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
    }

    public sealed class AppearancePhysicsDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),metrics=new List<string>();
        float started;
        void Check(bool condition,string label)
        {if(!condition)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label+"\n"+string.Join("\n",metrics));checks.Add("PASS "+label);}
        void Update(){if(Time.realtimeSinceStartup-started>45)AppearancePhysicsValidation.Finish(false,"Short-check timeout\n"+string.Join("\n",checks));}
        IEnumerator Start()
        {
            started=Time.realtimeSinceStartup;
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var f in FindObjectsByType<FireField>())f.Simulate=false;
            foreach(var m in FindObjectsByType<FirstReviewMission>())m.AutomaticTick=false;
            foreach(var c in FindObjectsByType<CookingOilIncident>())c.AutomaticTick=false;
            foreach(var n in FindObjectsByType<RescueNpc>())n.Autonomous=false;
            foreach(var r in FindObjectsByType<NpcItemRequest>())r.AutomaticTick=false;
            yield return null;
            Check(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Real graphics device, not null rendering");
            var house=GameObject.Find("First Review 1027").transform;
            foreach(var pair in new[]{("Ground","Outdoor floor"),("Living floor","Living floor"),("Kitchen floor","Kitchen floor"),("Bedroom floor","Bedroom floor"),("Bathroom floor","Bathroom floor"),("Storage floor","Storage floor")})
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/"+pair.Item2+".mat");
                Check(m&&m.color==Color.white&&m.GetFloat("_Glossiness")==0&&house.Find(pair.Item1).GetComponent<Renderer>().sharedMaterial==m,pair.Item1+" uses the white matte floor material");
            }
            var plaster=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/Warm plaster.mat");
            Check(plaster&&Vector4.Distance(plaster.color,new Color(.91f,.87f,.78f))<.001f&&plaster.shader.name=="FireRescue/MattePlaster","Beige walls retain their diffuse-only shader");
            Check(house.Find("User House 1027/wall/Visual").GetComponent<Renderer>().sharedMaterial==plaster,"User house actually references the beige wall material");
            Capture("white-floor-beige-walls.png",new Vector3(0,.4f,.4f),8.2f);
            yield return HoseChecks();
            FireChecks();
            WaterChecks();
            yield return null;
            AppearancePhysicsValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\n"+string.Join("\n",metrics)+"\nTargeted checks only: no full level, Windows player, or UI acceptance. Scene not saved.");
        }
        IEnumerator HoseChecks()
        {
            var settled=new Dictionary<int,Vector3[]>();
            foreach(int fps in new[]{30,60,120,144})
            {
                var root=new GameObject("Temporary hose "+fps);var start=new Vector3(80,.1f,80);root.transform.position=start+Vector3.right*1.25f;
                var source=new GameObject("Temporary hose source").AddComponent<FluidSource>();source.transform.position=start;
                var floor=Floor(start,12);var h=root.AddComponent<HoseConnection>();
                var data=new SerializedObject(h);data.FindProperty("maximumLength").floatValue=10;data.ApplyModifiedPropertiesWithoutUndo();
                var emitter=root.AddComponent<SprayEmitter>();emitter.AutomaticSimulation=false;emitter.Initialize(h,null);
                var tube=root.AddComponent<PhysicalHoseTube>();tube.AutomaticSimulation=false;tube.Initialize(emitter);
                Check(h.TryConnect(source),fps+"fps hose fixture connects");Physics.SyncTransforms();tube.TickPresentation(0);
                for(int i=1;i<=fps;i++)
                {
                    float t=i/(float)fps;root.transform.position=start+new Vector3(1.25f+1.75f*t,0,Mathf.Sin(t*Mathf.PI)*.5f);
                    tube.TickPresentation(1f/fps);
                }
                for(int i=0;i<fps/2;i++)tube.TickPresentation(1f/fps);
                settled[fps]=Enumerable.Range(0,tube.NodeCount).Select(tube.NodePosition).ToArray();
                Check(tube.IsVisible&&tube.NodeCount==128&&tube.UnexpectedHiddenFrames==0&&tube.LongestSegment<.2f&&settled[fps].All(Finite),fps+"fps continuous tube has stable count, finite nodes and bounded spans");
                var before=tube.NodePosition(64);tube.TickPresentation(float.NaN);tube.TickPresentation(float.PositiveInfinity);
                Check(tube.NodePosition(64)==before,fps+"fps rejects invalid time without poisoning nodes");
                root.transform.position+=Vector3.right*.25f;tube.TickPresentation(.5f);
                Check(tube.IsVisible&&Vector3.Distance(tube.NodePosition(127),emitter.HoseInletPosition)<.0001f&&Enumerable.Range(0,tube.NodeCount).All(i=>Finite(tube.NodePosition(i))),fps+"fps stall is bounded and endpoints still follow the inlet");
                h.Disconnect();tube.TickPresentation(0);Check(!tube.IsVisible,fps+"fps disconnect hides the tube immediately");
                root.transform.position=start+Vector3.right*1.25f;Check(h.TryConnect(source),fps+"fps reconnect remains reachable");tube.TickPresentation(1f/fps);
                Check(tube.IsVisible&&tube.NodeCount==128&&h.MaximumLength==10,fps+"fps reconnect resets simulation without changing hose length");
                metrics.Add("HOSE fps="+fps+" longest="+tube.LongestSegment+" max-frame-step="+tube.MaximumNodeStep);
                root.SetActive(false);floor.SetActive(false);source.gameObject.SetActive(false);Destroy(root);Destroy(floor);Destroy(source.gameObject);yield return null;
            }
            foreach(int fps in new[]{30,60,144})
            {
                float difference=settled[fps].Select((p,i)=>Vector3.Distance(p,settled[120][i])).Max();
                metrics.Add("HOSE settled delta "+fps+" vs 120="+difference);
                Check(difference<.03f,fps+"fps settled hose agrees with 120Hz reference within 3cm");
            }
        }
        void FireChecks()
        {
            var root=new GameObject("Temporary fire checks");var field=root.AddComponent<FireField>();field.Simulate=false;
            field.SetIgnitionSeeds(Array.Empty<Vector2Int>());field.Configure(new Vector3(100,.1f,100),3,1,.35f);
            var a=field.CellCenter(0,0);var b=field.CellCenter(2,0);field.Ignite(a,.9f);field.Ignite(b,.9f);
            var floor=Floor(new Vector3(100.525f,.1f,100.175f),6);var fx=root.AddComponent<FirstRescueEffects>();
            var presentation=new SerializedObject(fx);presentation.FindProperty("spaciousFire").boolValue=true;presentation.ApplyModifiedPropertiesWithoutUndo();fx.Initialize(null,field);fx.TickPresentation(0);
            var first=root.transform.Find("Fire Cluster 0/Orange Flame");var second=root.transform.Find("Fire Cluster 1/Orange Flame");
            float strong=first.localScale.y,other=second.localScale.y;var otherPosition=second.position;float authority=field.GetCell(0,0).Intensity;fx.TickPresentation(0);
            Check(field.GetCell(0,0).Intensity==authority,"Rendering fire never writes fire intensity");
            field.ApplyWater(a,1.5f,.25f);fx.TickPresentation(0);
            float weak=first.localScale.y;
            Check(field.IsBurningAt(a)&&weak<strong*.65f,"Suppressed live fire visibly shrinks before extinguishing");
            Capture("strong-and-suppressed-fire.png",new Vector3(100.525f,.25f,100.175f),1.1f);
            field.ApplyWater(a,10,.25f);fx.TickPresentation(0);
            Check(fx.ActiveFireVisualCount==1&&Mathf.Abs(first.localScale.y-other)<.0001f&&Vector3.Distance(first.position,otherPosition)<.0001f,"Extinguishing a neighbor does not reset the remaining cell flicker");
            var original=field.GetCell(2,0);field.Step(float.NaN);field.Step(float.PositiveInfinity);field.Step(-1);var after=field.GetCell(2,0);
            Check(original.Intensity==after.Intensity&&original.Fuel==after.Fuel,"Fire simulation ignores non-finite/negative time");
            field.Step(.1f);Check(field.GetCell(2,0).Fuel<after.Fuel&&float.IsFinite(field.GetCell(2,0).Intensity),"Finite fire simulation still consumes fuel normally");
            metrics.Add("FIRE measured height strong="+strong+" suppressed="+weak+"; stable neighbor height="+other);
            root.SetActive(false);floor.SetActive(false);Destroy(root);Destroy(floor);
        }
        void WaterChecks()
        {
            var root=new GameObject("Temporary water checks");root.transform.position=new Vector3(130,.1f,130);var floor=Floor(root.transform.position,16);
            var source=new GameObject("Temporary water source").AddComponent<FluidSource>();source.Configure(ElementKind.Water);source.transform.position=root.transform.position;source.SetSprayProfile(AssetDatabase.LoadAssetAtPath<SprayProfile>("Assets/FireRescue/Settings/ReviewWater.asset"));
            var hose=root.AddComponent<HoseConnection>();Check(hose.TryConnect(source),"Water fixture connects the unchanged review profile");
            var emitter=root.AddComponent<SprayEmitter>();emitter.Initialize(hose,root.AddComponent<ElementReaction>());emitter.AutomaticSimulation=false;emitter.EnableDirectionalSpray(true);emitter.SetGroundHeight(.1f);
            var fx=root.AddComponent<FirstRescueEffects>();fx.Initialize(emitter,null);bool hit=false;Vector3 normal=Vector3.zero,velocity=Vector3.zero;
            emitter.ParticleImpacted+=p=>{if(hit)return;hit=true;normal=p.Normal;velocity=p.Velocity;};Physics.SyncTransforms();
            for(int i=0;i<100&&!hit;i++){emitter.Tick(root.transform.position+Vector3.forward*8,true,1f/120);emitter.Advance(1f/120);}
            Check(hit&&normal.y>.99f,"Real swept water particles hit the ground with an upward collision normal");
            float amount=emitter.TotalEmittedAmount;fx.TickPresentation(.1f);var splash=root.transform.Find("Water Impact 0/Splash");var wet=root.transform.Find("Water Impact 0/Wet Mark");
            Check(splash&&wet&&Vector3.Dot(splash.up,normal)>.99f,"Water splash follows the actual collision plane");
            Check(splash.localScale.z>splash.localScale.x&&Vector3.Dot(splash.forward,Vector3.ProjectOnPlane(velocity,normal).normalized)>.99f,"Glancing water splashes stretch along their real incoming tangent");
            Check(splash.position.y>wet.position.y+.02f,"Incoming normal velocity produces a visible splash kick");
            Check(emitter.TotalEmittedAmount==amount&&fx.ActiveSplashCount>0,"Impact presentation does not emit more water or apply a second dose");
            Capture("water-impact.png",wet.position+Vector3.up*.4f,1.6f);
            root.SetActive(false);floor.SetActive(false);source.gameObject.SetActive(false);Destroy(root);Destroy(floor);Destroy(source.gameObject);
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        static GameObject Floor(Vector3 top,float size)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Temporary white floor";go.transform.position=top+Vector3.down*.05f;go.transform.localScale=new Vector3(size,.1f,size);
            go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/Kitchen floor.mat");return go;
        }
        static void Capture(string file,Vector3 focus,float size)
        {
            var camera=new GameObject("Temporary appearance camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=size;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.32f,.35f,.34f);
            camera.transform.rotation=Quaternion.Euler(50,0,0);camera.transform.position=focus-camera.transform.forward*24;
            var rt=new RenderTexture(1280,900,24);var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1280,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,900),0,0);image.Apply();File.WriteAllBytes(AppearancePhysicsValidation.Evidence+"/"+file,image.EncodeToPNG());
            RenderTexture.active=old;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(image);Destroy(camera.gameObject);
        }
    }
}
