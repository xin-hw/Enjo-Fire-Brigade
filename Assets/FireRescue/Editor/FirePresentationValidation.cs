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
    public static class FirePresentationValidation
    {
        public const string Evidence = "Docs/Validation/2026-10-31-fire-presentation";
        const string Key = "FireRescue.FirePresentationValidation";
        public static bool StudyOnly=>SessionState.GetBool(Key+".Study",false);
        static bool done;
        static FirePresentationValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                done = false; Application.logMessageReceived += OnLog;
                new GameObject("Temporary fire presentation checks").AddComponent<FirePresentationDriver>();
            };
        }
        public static void BatchStudy()
        {SessionState.SetBool(Key+".Study",true);Begin();}
        public static void BatchRun()
        {SessionState.SetBool(Key+".Study",false);Begin();}
        static void Begin()
        {
            Directory.CreateDirectory(Evidence);
            EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
        }
        static void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) Finish(false, message + "\n" + stack); }
        public static void Finish(bool pass, string message)
        {
            if (done) return; done = true; SessionState.SetBool(Key, false); Application.logMessageReceived -= OnLog;
            File.WriteAllText(Evidence + (StudyOnly?"/study.txt":"/targeted.txt"), (pass ? "FIRE_PRESENTATION_PASS\n" : "FIRE_PRESENTATION_FAIL\n") + message);
            EditorApplication.delayCall += () => EditorApplication.Exit(pass ? 0 : 1);
        }
    }

    public sealed class FirePresentationDriver : MonoBehaviour
    {
        readonly List<string> checks = new List<string>();
        readonly List<string> metrics=new List<string>();
        float began;
        void Update(){if(began>0&&Time.realtimeSinceStartup-began>90)FirePresentationValidation.Finish(false,"Timeout\n"+string.Join("\n",checks));}
        void Check(bool valid, string label)
        { if (!valid) throw new InvalidOperationException(string.Join("\n", checks) + "\nFAIL " + label); checks.Add("PASS " + label); }
        IEnumerator Start()
        {
            began=Time.realtimeSinceStartup;
            foreach (var input in FindObjectsByType<LocalPlayerInput>()) input.ReadDevices = false;
            foreach (var field in FindObjectsByType<FireField>()) field.Simulate = false;
            foreach (var mission in FindObjectsByType<FirstReviewMission>()) mission.AutomaticTick = false;
            foreach (var incident in FindObjectsByType<CookingOilIncident>()) incident.AutomaticTick = false;
            foreach (var npc in FindObjectsByType<RescueNpc>()) npc.Autonomous = false;
            foreach (var request in FindObjectsByType<NpcItemRequest>()) request.AutomaticTick = false;
            yield return null;
            var shader = Resources.Load<Shader>("FlowingFire");
            Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Fire shader compiles on the real D3D device");
            if(FirePresentationValidation.StudyOnly){Study(false, "cards");Study(true, "volume");}
            else{FieldChecks();PotChecks();ParityChecks();yield return SceneChecks();}
            FirePresentationValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n", checks)+"\n"+string.Join("\n",metrics) + "\nNo scene saved, player build, publishing or screen control.");
        }

        void FieldChecks()
        {
            var root=new GameObject("Temporary floor fire integration");
            var field=CreateField(root,new Vector3(60,.1f,60),4,4);Floor(root,field.Origin+new Vector3(.7f,0,.7f));
            var fx=root.AddComponent<FirstRescueEffects>();var so=new SerializedObject(fx);so.FindProperty("spaciousFire").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();fx.Initialize(null,field);
            var a=field.CellCenter(0,0);field.Ignite(a,.95f);field.Ignite(field.CellCenter(3,3),.95f);fx.TickPresentation(0);
            var first=root.transform.Find("Fire Cluster 0/Orange Flame");var second=root.transform.Find("Fire Cluster 1/Orange Flame");
            float strong=first.localScale.y;var neighbor=second.position;
            var material=first.GetComponent<Renderer>().sharedMaterial;
            Check(material.shader.name=="FireRescue/FlowingFire"&&first.GetComponent<MeshFilter>().sharedMesh.vertexCount==14,"Floor uses flowing upright tongues, not the old solid gemstone");
            Check(first.GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"Fire casts no solid-object shadow");
            field.ApplyWater(a,1.5f,.12f);fx.TickPresentation(0);
            Check(field.IsBurningAt(a)&&first.localScale.y<strong*.7f,"Water visibly lowers a still-dangerous floor flame before it dies");
            Capture(FirePresentationValidation.Evidence+"/strong-and-weak.png",field.Origin+new Vector3(.7f,.2f,.7f),1.5f);
            field.ApplyWater(a,10,.12f);fx.TickPresentation(0);
            Check(fx.ActiveHeatMarkCount==1&&fx.ActiveFireVisualCount==1&&!field.IsBurningAt(a),"Extinguished cells remove their heat warning immediately");
            Check(Vector3.Distance(first.position,neighbor)<.0001f,"Pool compaction preserves the surviving cell position");
            field.ResetSimulation();for(int z=0;z<4;z++)for(int x=0;x<4;x++)field.Ignite(field.CellCenter(x,z),.95f);fx.TickPresentation(0);
            Check(fx.ActiveFireVisualCount==16&&fx.ActiveHeatMarkCount==16&&fx.ActiveFlameCount<16,"Every dangerous cell stays visible while interior flame density stays sparse");
            Check(root.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Orange Flame"&&r.enabled).Select(r=>r.transform.localScale.y).Distinct().Count()>4,"Equal-intensity cells retain stable varied heights rather than identical stamps");
            var warnings=root.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Burning floor footprint"&&r.enabled).ToArray();
            int edgeSum=0;var block=new MaterialPropertyBlock();
            foreach(var warning in warnings){warning.GetPropertyBlock(block);var edges=block.GetVector(FireSurface.Edges);edgeSum+=(int)(edges.x+edges.y+edges.z+edges.w);}
            Check(edgeSum==16,"Only the 16 external edges of the 4x4 region are emphasized");
            Check(warnings.All(r=>Mathf.Abs(r.bounds.size.x-.35f)<.001f&&Mathf.Abs(r.bounds.size.z-.35f)<.001f),"Danger surfaces exactly cover authored cells, not arbitrary particle bounds");
            var before=Snapshot(field);var transforms=root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
            var focus=field.Origin+new Vector3(.7f,.2f,.7f);
            var imageA=Capture(FirePresentationValidation.Evidence+"/flow-a.png",focus,2.3f);
            var fixedPosition=first.position;var fixedScale=first.localScale;
            for(int i=0;i<5;i++)fx.TickPresentation(.05f);
            var imageB=Capture(FirePresentationValidation.Evidence+"/flow-b.png",focus,2.3f);
            int pixels=Changed(imageA,imageB);
            Check(pixels>60,"Actual flame pixels move while gameplay and root geometry remain fixed");
            Check(first.position==fixedPosition&&first.localScale==fixedScale,"Fire motion is surface flow, not whole-object position/scale jitter");
            Check(before.SequenceEqual(Snapshot(field)),"Advancing fire presentation never changes intensity, fuel or residues");
            float clock=material.GetFloat(FireSurface.Clock);fx.TickPresentation(float.NaN);fx.TickPresentation(float.PositiveInfinity);fx.TickPresentation(-1);
            Check(material.GetFloat(FireSurface.Clock)==clock,"Invalid presentation time cannot poison flame animation");
            string frames="Logs/FireIntegratedFrames";Directory.CreateDirectory(frames);
            for(int frame=0;frame<48;frame++){fx.TickPresentation(1f/15);Capture(frames+"/frame"+frame.ToString("D3")+".png",focus,2.3f);}
            Check(transforms.SequenceEqual(root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh)),"Continuous animation reuses its mesh pool instead of spawning new objects");
            Check(root.GetComponentsInChildren<Light>().Length==0,"Dense fire does not create a dynamic light per cell");
            fx.enabled=false;Check(fx.ActiveFireVisualCount==0&&field.BurningCount==16&&field.IsBurningAt(a),"Disabling all fire visuals leaves gameplay hazards intact");
            fx.enabled=true;fx.TickPresentation(0);Check(fx.ActiveHeatMarkCount==16,"Re-enabling reconstructs the visible dangerous region");
            field.ApplyWater(a,10,.12f);fx.TickPresentation(0);field.IgniteFromHeat(a);fx.TickPresentation(0);
            Check(field.GetCell(0,0).IsBurning&&fx.ActiveHeatMarkCount==field.BurningCount,"A real relight restores its warning in the same presentation tick");
            Capture(FirePresentationValidation.Evidence+"/floor-region.png",focus,2.3f);
            metrics.Add("FLOOR changed-pixels="+pixels+" exact-edges="+edgeSum+" live="+field.BurningCount);
            root.SetActive(false);Destroy(root);
        }

        void PotChecks()
        {
            var root=new GameObject("Temporary pot fire integration");root.transform.position=new Vector3(85,.1f,60);
            var field=CreateField(root,root.transform.position,9,9);Floor(root,field.Origin+new Vector3(1.5f,0,1.5f));
            var pan=new GameObject("Pot fixture");pan.transform.SetParent(root.transform);pan.transform.position=field.CellCenter(4,4)+Vector3.up*.5f;
            var pot=pan.AddComponent<ObjectFire>();pot.Configure(FireKind.CookingOil,1);pot.ConfigureWaterEscalation(.65f,3);
            var food=new GameObject("Food fixture").AddComponent<CarryItem>();food.transform.SetParent(root.transform);food.transform.position=pan.transform.position+Vector3.right*.7f;
            var incident=pan.AddComponent<CookingOilIncident>();incident.AutomaticTick=false;incident.Initialize(pot,field,food);
            var config=new SerializedObject(incident);config.FindProperty("spillStart").vector3Value=field.CellCenter(4,4);config.ApplyModifiedPropertiesWithoutUndo();
            var view=pan.AddComponent<OilPanFireView>();view.AutomaticTick=false;view.Initialize(pot);view.SetIncident(incident);view.TickPresentation(0);
            Check(view.ActiveTongueCount==5&&view.BurstAmount==0,"Pot uses a bounded five-tongue cluster at its real anchor");
            Check(pan.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial.shader.name=="FireRescue/FlowingFire"),"Pot and floor use the same flowing fire surface");
            float original=pot.Intensity;pot.Receive(ElementKind.Water,.1f);incident.Tick(.08f);view.TickPresentation(.08f);
            Check(pot.Intensity>original&&incident.ExplosionCount==1&&food.IsBurnt,"Water still escalates the actual pot, triggers one explosion and burns nearby pudding");
            Check(view.BurstAmount>.7f,"The real explosion produces a brief expanding visual flare");
            Capture(FirePresentationValidation.Evidence+"/pot-burst.png",pan.transform.position+Vector3.up*.2f,1.3f);
            string frames="Logs/FirePotReactionFrames";Directory.CreateDirectory(frames);
            for(int frame=0;frame<48;frame++)
            {
                if(frame==28)pot.Receive(ElementKind.DryPowder,10);
                incident.Tick(1f/15);view.TickPresentation(1f/15);
                Capture(frames+"/frame"+frame.ToString("D3")+".png",pan.transform.position+Vector3.up*.2f,1.3f);
            }
            Check(view.BurstAmount==0&&view.ActiveTongueCount==0&&!pot.IsBurning,"Powder puts out the pot and its flare/tongues disappear");
            view.enabled=false;pot.Ignite(1);Check(pot.IsBurning&&view.ActiveTongueCount==0,"Hidden pot presentation does not extinguish its gameplay fire");
            view.enabled=true;view.TickPresentation(0);Check(view.ActiveTongueCount==5,"Pot view safely reuses its pool after disable/re-enable");
            float active=pan.transform.Find("Flame view 0").localScale.y;view.TickPresentation(float.NaN);view.TickPresentation(float.PositiveInfinity);
            Check(pan.transform.Find("Flame view 0").localScale.y==active,"Invalid pot view time is ignored");
            root.SetActive(false);Destroy(root);
        }

        void ParityChecks()
        {
            var rootA=new GameObject("Fire parity with view");var rootB=new GameObject("Fire parity without view");
            var a=CreateField(rootA,new Vector3(110,.1f,60),4,4);var b=CreateField(rootB,a.Origin,4,4);
            var fx=rootA.AddComponent<FirstRescueEffects>();fx.Initialize(null,a);
            a.Ignite(a.CellCenter(1,1),1);b.Ignite(b.CellCenter(1,1),1);
            for(int step=0;step<180;step++)
            {
                if(step==30){a.ApplyTimedWater(a.CellCenter(1,1),.8f,.3f,1);b.ApplyTimedWater(b.CellCenter(1,1),.8f,.3f,1);}
                if(step==90){a.ApplyTimedDryPowder(a.CellCenter(1,1),1.2f,.6f,2);b.ApplyTimedDryPowder(b.CellCenter(1,1),1.2f,.6f,2);}
                a.Step(1f/60);b.Step(1f/60);fx.TickPresentation(1f/60);
            }
            Check(Snapshot(a).SequenceEqual(Snapshot(b)),"Three seconds of actual spread/water/powder stay identical with and without new visuals");
            rootA.SetActive(false);rootB.SetActive(false);Destroy(rootA);Destroy(rootB);
        }

        IEnumerator SceneChecks()
        {
            var field=FindAnyObjectByType<FireField>();var fx=FindAnyObjectByType<FirstRescueEffects>();var incident=FindAnyObjectByType<CookingOilIncident>();
            var pot=FindObjectsByType<ObjectFire>().First(f=>f.Kind==FireKind.CookingOil);var potView=pot.GetComponent<OilPanFireView>();potView.AutomaticTick=false;
            Check(potView&&fx&&field,"Current authored level wires both floor and pot views without scene migration");
            // Exercise the real five-second first wave, not an instant all-fire replacement.
            for(int step=0;step<300;step++){incident.Tick(1f/60);field.Step(1f/60);fx.TickPresentation(1f/60);potView.TickPresentation(1f/60);}
            Check(incident.FirstWaveProcessedCount==incident.FirstWaveTargetCount&&field.BurningCount>20,"Real level still reaches its authored first-wave kitchen footprint");
            Check(fx.ActiveHeatMarkCount==field.BurningCount,"Every live first-wave kitchen cell has a visible warning");
            var player=FindAnyObjectByType<LocalPlayerInput>();player.GetComponent<FirefighterMotor>().enabled=false;
            yield return null;
            var focus=FirstReview1027Setup.Pan+new Vector3(1.4f,-.5f,-.6f);
            Capture(FirePresentationValidation.Evidence+"/level-game-camera.png",focus,6);
            Capture(FirePresentationValidation.Evidence+"/level-kitchen-close.png",focus,3);
            string frames="Logs/FireLevelFrames",closeFrames="Logs/FireLevelCloseFrames";Directory.CreateDirectory(frames);Directory.CreateDirectory(closeFrames);
            for(int frame=0;frame<48;frame++)
            {
                if(frame==24)pot.Receive(ElementKind.Water,.1f);
                if(frame==36)pot.Receive(ElementKind.DryPowder,10);
                incident.Tick(1f/15);field.Step(1f/15);
                fx.TickPresentation(1f/15);potView.TickPresentation(1f/15);
                Capture(frames+"/frame"+frame.ToString("D3")+".png",focus,6);
                Capture(closeFrames+"/frame"+frame.ToString("D3")+".png",focus,3);
            }
            Check(incident.ExplosionCount==1&&!pot.IsBurning&&potView.ActiveTongueCount==0,"Real level pot plays water flare then powder extinction from existing element APIs");
            Check(player.GetComponent<SprayEmitter>().GetComponent<HoseConnection>().MaximumLength==10,"Fire appearance does not relax the real hose limit");
            metrics.Add("LEVEL live="+field.BurningCount+" heat="+fx.ActiveHeatMarkCount+" flames="+fx.ActiveFlameCount+" view=6/50/0");
        }

        static float[] Snapshot(FireField field)
        {
            var values=new List<float>();for(int z=0;z<field.Height;z++)for(int x=0;x<field.Width;x++)
            {var c=field.GetCell(x,z);values.AddRange(new[]{c.Intensity,c.Fuel,c.Oil,c.Wetness,c.WaterRemaining,c.Powder,c.PowderRemaining});}
            return values.ToArray();
        }
        static int Changed(Color32[] a,Color32[] b)=>a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)>12).Count(v=>v);
        static FireField CreateField(GameObject root,Vector3 origin,int width,int height)
        {
            var field=root.AddComponent<FireField>();field.Simulate=false;field.SetIgnitionSeeds(Array.Empty<Vector2Int>());field.Configure(origin,width,height,.35f);return field;
        }
        static void Floor(GameObject root,Vector3 center)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(root.transform);go.transform.position=center-Vector3.up*.06f;go.transform.localScale=new Vector3(10,.1f,10);
            go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/Kitchen floor.mat");
        }
        void Study(bool volume, string name)
        {
            var origin = new Vector3(volume ? 80 : 60, .1f, 60);
            var root = new GameObject("Temporary fire " + name); root.transform.position = origin;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform);
            floor.transform.position = origin + Vector3.down * .06f; floor.transform.localScale = new Vector3(12, .1f, 12);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder + "/Kitchen floor.mat");
            var material = FireSurface.CreateMaterial(); var mesh = FireSurface.CreateTongueMesh(volume); var heat = FireSurface.CreateFootprintMesh();
            var block = new MaterialPropertyBlock();
            for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            {
                var p = origin + new Vector3((x - 1) * .7f, 0, z * .7f + 1.1f);
                Tongue(p, .7f, .42f, x * 1.618f + z * 2.414f);
                var footprint = Surface("Heat footprint", p, Vector3.one * .7f, heat);
                block.Clear(); block.SetVector(FireSurface.Edges, new Vector4(x == 0 ? 1 : 0, x == 2 ? 1 : 0, z == 0 ? 1 : 0, z == 2 ? 1 : 0));
                footprint.SetPropertyBlock(block);
            }
            Tongue(origin + Vector3.left * 1.4f, .7f, .42f, .7f);
            Tongue(origin + Vector3.right * 1.4f, .7f * Mathf.Sqrt(.2f), .42f * Mathf.Sqrt(.2f), 3.9f);
            string frames = "Logs/FireStudyFrames-" + name; Directory.CreateDirectory(frames);
            for (int frame = 0; frame < 48; frame++)
            {
                material.SetFloat(FireSurface.Clock, frame / 15f);
                Capture(frames + "/frame" + frame.ToString("D3") + ".png", origin + Vector3.forward * 1.2f, 3, 50, 0);
            }
            Capture(FirePresentationValidation.Evidence + "/" + name + "-game-distance.png", origin + Vector3.forward * 1.2f, 6, 50, 0);
            Check(mesh.vertexCount > 20 && material.shader.name == "FireRescue/FlowingFire", name + " candidate uses the shared flowing surface");
            root.SetActive(false); Destroy(root); Destroy(material); Destroy(mesh); Destroy(heat);
            void Tongue(Vector3 point, float width, float height, float phase)
            {
                var r = Surface("Fire tongue", point, new Vector3(width * .38f, height, width * .38f), mesh);
                FireSurface.SetPhase(r, block, phase);
            }
            Renderer Surface(string label, Vector3 position, Vector3 scale, Mesh geometry)
            {
                var go = new GameObject(label); go.transform.SetParent(root.transform); go.transform.position = position + Vector3.up * .02f; go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = geometry; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return r;
            }
        }
        public static Color32[] Capture(string path, Vector3 focus, float size, float pitch = 50, float yaw = 0)
        {
            var camera = new GameObject("Temporary fire camera").AddComponent<Camera>();
            camera.enabled=false;
            camera.orthographic = true; camera.orthographicSize = size; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0); camera.transform.position = focus - camera.transform.forward * 25;
            var rt = new RenderTexture(1280, 720, 24); var old = RenderTexture.active; camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG()); var pixels = image.GetPixels32();
            RenderTexture.active = old; camera.targetTexture = null; rt.Release(); Destroy(rt); Destroy(image); Destroy(camera.gameObject); return pixels;
        }
    }
}
