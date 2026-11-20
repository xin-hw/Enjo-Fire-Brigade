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
    public static class FireWaterPolishValidation
    {
        public const string Evidence="Docs/Validation/2026-11-09-fire-water-polish";
        const string Key="FireRescue.FireWaterPolish";
        static bool done;
        static FireWaterPolishValidation()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
                done=false;Application.logMessageReceived+=Log;
                new GameObject("Temporary fire water polish checks").AddComponent<FireWaterPolishDriver>();
            };
        }
        public static void BatchRun()
        {
            Directory.CreateDirectory(Evidence);
            EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Log(string text,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Finish(false,text+"\n"+stack);}
        public static void Finish(bool pass,string report)
        {
            if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;
            File.WriteAllText(Evidence+"/targeted.txt",(pass?"FIRE_WATER_POLISH_PASS\n":"FIRE_WATER_POLISH_FAIL\n")+report);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);
        }
    }

    public sealed class FireWaterPolishDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        readonly List<string> metrics=new List<string>();
        float began;
        void Check(bool condition,string label)
        {if(!condition)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label+"\n"+string.Join("\n",metrics));checks.Add("PASS "+label);}
        void Update(){if(began>0&&Time.realtimeSinceStartup-began>180)FireWaterPolishValidation.Finish(false,"Timeout\n"+string.Join("\n",checks));}
        IEnumerator Start()
        {
            began=Time.realtimeSinceStartup;
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var i in FindObjectsByType<FireField>())i.Simulate=false;
            foreach(var i in FindObjectsByType<CookingOilIncident>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<FirstReviewMission>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<RescueNpc>())i.Autonomous=false;
            foreach(var i in FindObjectsByType<PlayerFireContact>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<DoorFireExposure>())i.AutomaticTick=false;
            foreach(var i in FindObjectsByType<OilPanFireView>())i.AutomaticTick=false;
            yield return null;
            foreach(string name in new[]{"FlowingWater","FlowingFire"})
            {var s=Resources.Load<Shader>(name);Check(s&&s.isSupported&&!ShaderUtil.ShaderHasError(s),name+" compiles on the actual graphics device");}
            yield return SurfaceMomentum();
            DoseParity();
            WaterContact();
            FirePreview();
            FireWaterPolishValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\n"+string.Join("\n",metrics)+
                "\nTemporary fixtures and real-scene offscreen captures; no saved scene, Windows build, full playthrough or publish.");
        }

        IEnumerator SurfaceMomentum()
        {
            var root=new GameObject("Temporary surface momentum");var r=root.AddComponent<ElementReaction>();
            var direct=Body(root,new Vector3(90,3,90));var glance=Body(root,new Vector3(93,3,90));
            foreach(var body in new[]{direct,glance})
                r.ApplyBallisticSurfaceImpact(ElementKind.Water,body.position+new Vector3(0,.3f,-.5f),
                    body==direct?Vector3.forward*10:new Vector3(Mathf.Sqrt(99),0,1),Vector3.back,.1f,body.GetComponent<Collider>(),.9f,5,SprayEffectSettings.Neutral);
            yield return new WaitForFixedUpdate();yield return null;
            Check(direct.linearVelocity.z>4.7f&&direct.angularVelocity.magnitude>.01f,"Head-on water transfers normal momentum with off-center torque");
            Check(glance.linearVelocity.magnitude<direct.linearVelocity.magnitude*.25f&&glance.linearVelocity.x>0&&glance.linearVelocity.z>0,
                "Grazing jet retains surface slip instead of transferring full incoming momentum");
            metrics.Add("Momentum direct="+direct.linearVelocity+" grazing="+glance.linearVelocity);
            var old=glance.linearVelocity;r.ApplyBallisticSurfaceImpact(ElementKind.Water,glance.position,Vector3.forward*10,Vector3.one*float.NaN,1,glance.GetComponent<Collider>(),1,5,SprayEffectSettings.Neutral);
            yield return new WaitForFixedUpdate();Check((old-glance.linearVelocity).sqrMagnitude<.0001f,"Invalid contact normal does not corrupt rigidbody state");
            root.SetActive(false);Destroy(root);
        }
        static Rigidbody Body(GameObject parent,Vector3 p)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(parent.transform);go.transform.position=p;
            var rb=go.AddComponent<Rigidbody>();rb.useGravity=false;rb.linearDamping=rb.angularDamping=0;return rb;
        }
        void DoseParity()
        {
            var root=new GameObject("Temporary unchanged reaction dose");
            var a=Field(root,new Vector3(130,.1f,130));var b=Field(root,new Vector3(135,.1f,130));
            var ar=a.gameObject.AddComponent<ElementReaction>();ar.Initialize(a);var br=b.gameObject.AddComponent<ElementReaction>();br.Initialize(b);
            foreach(var element in new[]{ElementKind.Water,ElementKind.DryPowder})
            {
                a.ResetSimulation();b.ResetSimulation();a.Ignite(a.CellCenter(1,1),1);b.Ignite(b.CellCenter(1,1),1);
                ar.ApplyBallisticImpact(element,a.CellCenter(1,1),Vector3.forward*10,.4f,null,.6f,5,SprayEffectSettings.Neutral);
                br.ApplyBallisticSurfaceImpact(element,b.CellCenter(1,1),Vector3.forward*10,Vector3.up,.4f,null,.6f,5,SprayEffectSettings.Neutral);
                var ac=a.GetCell(1,1);var bc=b.GetCell(1,1);
                Check(ac.Intensity==bc.Intensity&&ac.Wetness==bc.Wetness&&ac.Powder==bc.Powder&&ac.WaterRemaining==bc.WaterRemaining,
                    element+" dose and extinguishing are independent of impact angle");
            }
            root.SetActive(false);Destroy(root);
        }
        static FireField Field(GameObject parent,Vector3 origin)
        {
            var go=new GameObject("Temporary field");go.transform.SetParent(parent.transform);var f=go.AddComponent<FireField>();
            f.Simulate=false;f.SetIgnitionSeeds(Array.Empty<Vector2Int>());f.Configure(origin,3,3,.4f);return f;
        }

        void WaterContact()
        {
            var root=new GameObject("Temporary water contact review");var origin=new Vector3(60,.1f,60);
            Cube(root,origin+Vector3.down*.05f,new Vector3(12,.1f,12),"Kitchen floor");
            var wall=Cube(root,origin+new Vector3(0,.8f,3.1f),new Vector3(4,1.6f,.2f),"Warm plaster");wall.layer=8;
            var actor=new GameObject("Temporary water actor");actor.transform.SetParent(root.transform);actor.transform.position=origin;
            var source=new GameObject("Temporary source").AddComponent<FluidSource>();source.transform.SetParent(root.transform);
            source.Configure(ElementKind.Water);source.transform.position=origin;
            source.SetSprayProfile(AssetDatabase.LoadAssetAtPath<SprayProfile>("Assets/FireRescue/Settings/ReviewWater.asset"));
            var hose=actor.AddComponent<HoseConnection>();Check(hose.TryConnect(source),"Contact fixture uses actual water source profile");
            var e=actor.AddComponent<SprayEmitter>();e.Initialize(hose,root.AddComponent<ElementReaction>());e.EnableDirectionalSpray(true);e.AutomaticSimulation=false;
            var geo=root.AddComponent<SprayWallOcclusion>();geo.Configure(new[]{wall.GetComponent<Collider>()},Array.Empty<HoseWindow>());e.SetWallOcclusion(geo);
            var fx=root.AddComponent<FirstRescueEffects>();fx.Initialize(e,null);Physics.SyncTransforms();
            Check(geo.TryBlock(origin+new Vector3(-1,1,0),origin+new Vector3(1,1,4),out _,out _,out var normal)&&Vector3.Dot(normal,Vector3.back)>.999f,
                "Cutaway wall query returns actual wall normal for oblique hits");
            int wallHits=0;bool normals=true;
            e.ParticleImpacted+=hit=>{if(hit.Position.z>62.8f){wallHits++;normals&=Vector3.Dot(hit.Normal,Vector3.back)>.999f;}};
            for(int i=0;i<120;i++){e.Tick(origin+new Vector3(1,0,8),true,1f/120);e.Advance(1f/120);fx.TickPresentation(1f/120);}
            Check(wallHits>0&&normals,"Authoritative packet events keep the wall normal, not minus incoming velocity");
            var v=fx.WaterVolumes;Check(v.CoreSectionCount==1&&v.IsVisible&&v.DisplayMesh.vertexCount>0&&
                v.DisplayMesh.tangents.Length==v.DisplayMesh.vertexCount,"Live jet remains one coherent 3D surface with a complete tangent frame");
            var vertices=v.DisplayMesh.vertices;var uv=new List<Vector4>();v.DisplayMesh.GetUVs(0,uv);
            int seams=0;
            for(int i=0;i+12<vertices.Length;i++)if(uv[i].x==0&&uv[i+12].x==1&&uv[i].y==uv[i+12].y)
            {Check(Vector3.Distance(vertices[i],vertices[i+12])<.00001f,"Water seam closes with separate 0/1 UVs");seams++;break;}
            Check(seams>0,"Continuous water includes a correctly duplicated UV seam");
            e.Stop();for(int i=0;i<120;i++){e.Advance(1f/120);fx.TickPresentation(1f/120);}
            var wet=fx.GetComponentsInChildren<Renderer>().First(r=>r.name=="Wet Mark"&&r.enabled);
            var wallWet=fx.GetComponentsInChildren<Renderer>().First(r=>r.name=="Wet Mark"&&r.enabled&&Mathf.Abs(r.transform.up.y)<.5f);
            var wetMesh=wallWet.GetComponent<MeshFilter>().sharedMesh;
            Check(wetMesh.vertexCount>0&&wetMesh.tangents.Length==wetMesh.vertexCount,
                "Contact films provide a real tangent frame for surface-aligned lighting");
            var wt=wallWet.transform.TransformDirection((Vector3)wetMesh.tangents[0]).normalized;
            Check(Mathf.Abs(Vector3.Dot(wt,wallWet.transform.up))<.001f,"Vertical wall wet-film highlights remain in the actual contact plane");
            var block=new MaterialPropertyBlock();wet.GetPropertyBlock(block);float first=block.GetFloat("_WetFade");
            for(int i=0;i<80;i++)fx.TickPresentation(.02f);
            wet.GetPropertyBlock(block);Check(!wet.enabled||block.GetFloat("_WetFade")<first,"Wet contact film fades instead of disappearing as an opaque dot");
            fx.enabled=false;fx.enabled=true;
            string frames="Logs/FireWaterPolish-Water";Directory.CreateDirectory(frames);
            for(int frame=0;frame<48;frame++)
            {
                for(int step=0;step<8;step++)
                {
                    float angle=frame<24?Mathf.Sin(frame*.12f)*.45f:0;
                    if(frame<36)e.Tick(origin+new Vector3(angle,0,8),true,1f/120);else e.Stop();
                    e.Advance(1f/120);fx.TickPresentation(1f/120);
                }
                FirePresentationDriver.Capture(frames+"/frame"+frame.ToString("D3")+".png",origin+new Vector3(0,.45f,1.5f),2.5f,50,-25);
                if(frame==22)FirePresentationDriver.Capture(FireWaterPolishValidation.Evidence+"/water-contact.png",origin+new Vector3(0,.45f,1.5f),2.5f,50,-25);
            }
            root.SetActive(false);Destroy(root);
        }
        static GameObject Cube(GameObject root,Vector3 p,Vector3 scale,string material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(root.transform);go.transform.position=p;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/"+material+".mat");return go;
        }
        void FirePreview()
        {
            var root=GameObject.Find("First Review 1027");var field=root.GetComponentInChildren<FireField>();
            var incident=root.GetComponentInChildren<CookingOilIncident>();var fx=root.GetComponentInChildren<FirstRescueEffects>();
            var pot=root.GetComponentInChildren<OilPanFireView>();var doors=root.GetComponentsInChildren<DoorFireExposure>();
            for(int i=0;i<600;i++){incident.Tick(.05f);field.Step(.05f);foreach(var d in doors)d.Tick(.05f);}
            float[] before=State(field);string frames="Logs/FireWaterPolish-Fire";Directory.CreateDirectory(frames);
            for(int i=0;i<48;i++)
            {fx.TickPresentation(1f/15);pot.TickPresentation(1f/15);FirePresentationDriver.Capture(frames+"/frame"+i.ToString("D3")+".png",new Vector3(-2,.4f,-1.5f),6);}
            FirePresentationDriver.Capture(FireWaterPolishValidation.Evidence+"/fire-close.png",new Vector3(-3.7f,.4f,-2.2f),2.6f);
            FirePresentationDriver.Capture(FireWaterPolishValidation.Evidence+"/fire-game-camera.png",new Vector3(-2,.4f,-1.5f),6);
            Check(before.SequenceEqual(State(field)),"Fire animation cannot change burning/fuel/water/powder gameplay state");
            Check(fx.ActiveHeatMarkCount==field.BurningCount&&fx.ActiveFlameCount>0,"Every live fire cell remains visibly marked even with sparse flames");
            Check(fx.ActiveFireAtmosphereCount<=40,"Atmosphere stays bounded without a dense smoke wall");
            metrics.Add("Live fire cells="+field.BurningCount+" flame groups="+fx.ActiveFlameCount+" atmosphere="+fx.ActiveFireAtmosphereCount);
        }
        static float[] State(FireField field)
        {
            var data=new List<float>();for(int z=0;z<field.Height;z++)for(int x=0;x<field.Width;x++)
            {var s=field.GetCell(x,z);data.Add(s.Intensity);data.Add(s.Fuel);data.Add(s.Wetness);data.Add(s.Powder);}return data.ToArray();
        }
    }
}
