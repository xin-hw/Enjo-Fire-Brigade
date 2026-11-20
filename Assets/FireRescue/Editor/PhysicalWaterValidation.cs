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
    public static class PhysicalWaterValidation
    {
        const string Key="FireRescue.PhysicalWaterValidation";
        public static bool CaseRun=>SessionState.GetBool(Key+".Case",false);
        static bool FireRegression=>SessionState.GetBool(Key+".FireRegression",false);
        public static bool SurfaceRun=>CaseRun||SessionState.GetBool(Key+".Surface",false);
        public static bool CoreRun=>SurfaceRun||SessionState.GetBool(Key+".Core",false);
        public static bool OrganicRun=>CoreRun||SessionState.GetBool(Key+".Organic",false);
        public static string Evidence=>SessionState.GetBool(Key+".Polish",false)?"Docs/Validation/2026-11-09-fire-water-polish/water-regression":SessionState.GetBool(Key+".NaturalFire",false)?"Docs/Validation/2026-11-01-natural-fire/water-regression":FireRegression?"Docs/Validation/2026-10-31-fire-presentation/water-regression":CaseRun?"Docs/Validation/2026-10-31-case-water-spray":SurfaceRun?"Docs/Validation/2026-10-31-water-surface":CoreRun?"Docs/Validation/2026-10-31-main-water-jet":OrganicRun?"Docs/Validation/2026-10-31-organic-water":"Docs/Validation/2026-10-31-physical-water";
        static bool done;
        static PhysicalWaterValidation(){EditorApplication.playModeStateChanged+=s=>
        {
            if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
            done=false;Application.logMessageReceived+=OnLog;new GameObject("Temporary physical water checks").AddComponent<PhysicalWaterDriver>();
        };}
        // Narrow authoring migration, separate from the tests. No map regeneration.
        public static void BatchApplyAndRun()
        {
            var scene=EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);
            var tube=UnityEngine.Object.FindAnyObjectByType<PhysicalHoseTube>();
            if(!tube)throw new InvalidOperationException("Current scene has no physical hose.");
            var so=new SerializedObject(tube);so.FindProperty("radius").floatValue=.05f;so.ApplyModifiedPropertiesWithoutUndo();
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save hose diameter.");
            BatchRun();
        }
        public static void BatchRun(){SessionState.SetBool(Key+".Case",false);SessionState.SetBool(Key+".Surface",false);SessionState.SetBool(Key+".Core",false);SessionState.SetBool(Key+".Organic",false);Begin();}
        public static void BatchRunOrganic(){SessionState.SetBool(Key+".Case",false);SessionState.SetBool(Key+".Surface",false);SessionState.SetBool(Key+".Core",false);SessionState.SetBool(Key+".Organic",true);Begin();}
        public static void BatchRunCore(){SessionState.SetBool(Key+".Case",false);SessionState.SetBool(Key+".Surface",false);SessionState.SetBool(Key+".Core",true);Begin();}
        public static void BatchRunSurface(){SessionState.SetBool(Key+".Case",false);SessionState.SetBool(Key+".Surface",true);Begin();}
        public static void BatchRunCase(){SessionState.SetBool(Key+".FireRegression",false);SessionState.SetBool(Key+".Case",true);Begin();}
        public static void BatchRunFireRegression(){SessionState.SetBool(Key+".FireRegression",true);SessionState.SetBool(Key+".Case",true);Begin();}
        public static void BatchRunNaturalFireRegression(){SessionState.SetBool(Key+".NaturalFire",true);SessionState.SetBool(Key+".Case",true);Begin();}
        public static void BatchRunPolishRegression(){SessionState.SetBool(Key+".Polish",true);SessionState.SetBool(Key+".Case",true);Begin();}
        static void Begin(){Directory.CreateDirectory(Evidence);EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void OnLog(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Finish(false,text+"\n"+stack);}
        public static void Finish(bool pass,string text)
        {
            if(done)return;done=true;SessionState.SetBool(Key,false);Application.logMessageReceived-=OnLog;
            File.WriteAllText(Evidence+"/targeted.txt",(pass?"PHYSICAL_WATER_PASS\n":"PHYSICAL_WATER_FAIL\n")+text);
            SessionState.SetBool(Key+".FireRegression",false);
            SessionState.SetBool(Key+".Polish",false);
            EditorApplication.delayCall+=()=>EditorApplication.Exit(pass?0:1);
        }
    }

    public sealed class PhysicalWaterDriver:MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),metrics=new List<string>();float began;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(string.Join("\n",checks)+"\nFAIL "+label+"\n"+string.Join("\n",metrics));checks.Add("PASS "+label);}
        void Update(){if(Time.realtimeSinceStartup-began>55)PhysicalWaterValidation.Finish(false,"Timeout\n"+string.Join("\n",checks));}
        IEnumerator Start()
        {
            began=Time.realtimeSinceStartup;
            foreach(var i in FindObjectsByType<LocalPlayerInput>())i.ReadDevices=false;
            foreach(var f in FindObjectsByType<FireField>())f.Simulate=false;
            foreach(var m in FindObjectsByType<FirstReviewMission>())m.AutomaticTick=false;
            foreach(var c in FindObjectsByType<CookingOilIncident>())c.AutomaticTick=false;
            foreach(var n in FindObjectsByType<RescueNpc>())n.Autonomous=false;
            foreach(var r in FindObjectsByType<NpcItemRequest>())r.AutomaticTick=false;
            yield return null;
            Check(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"D3D rendering is available");
            var sceneTube=FindAnyObjectByType<PhysicalHoseTube>();
            Check(sceneTube&&sceneTube.RenderRadius==.05f&&sceneTube.GetComponent<HoseConnection>().MaximumLength==10,"Scene hose is 10cm wide without relaxing 10m length");
            yield return Momentum();
            ElementParity();
            if(PhysicalWaterValidation.OrganicRun)Distribution();
            Water();
            yield return Hose();
            yield return SceneWater();
            PhysicalWaterValidation.Finish(true,"Checks="+checks.Count+"\n"+string.Join("\n",checks)+"\n"+string.Join("\n",metrics)+"\nNo Windows/WebGL build, publishing or screen control. Test fixtures were temporary; no scene saved during validation.");
        }
        void Distribution()
        {
            var root=new GameObject("Temporary irregular distribution");root.transform.position=new Vector3(180,30,180);
            var source=new GameObject("Temporary distribution source").AddComponent<FluidSource>();source.transform.SetParent(root.transform,false);source.Configure(ElementKind.Water);
            source.SetSprayProfile(AssetDatabase.LoadAssetAtPath<SprayProfile>("Assets/FireRescue/Settings/ReviewWater.asset"));
            var hose=root.AddComponent<HoseConnection>();Check(hose.TryConnect(source),"Irregular-distribution fixture connects");
            var emitter=root.AddComponent<SprayEmitter>();emitter.Initialize(hose,root.AddComponent<ElementReaction>());emitter.EnableDirectionalSpray(true);emitter.AutomaticSimulation=false;
            var randomBefore=UnityEngine.Random.state;
            for(int i=0;i<80;i++)emitter.Tick(root.transform.position+Vector3.forward*10,true,1f/60);
            var samples=Enumerable.Range(0,emitter.ActiveParticleCount).Select(emitter.GetParticle).ToArray();
            int speedCount=samples.Select(s=>Mathf.RoundToInt(s.Velocity.magnitude*100)).Distinct().Count();
            int angleCount=samples.Select(s=>Mathf.RoundToInt(Mathf.Atan2(s.Velocity.x,s.Velocity.z)*Mathf.Rad2Deg*100)).Distinct().Count();
            int sizeCount=samples.Select(s=>Mathf.RoundToInt(s.VisualScale*100)).Distinct().Count();
            Check(speedCount>50,"Water has continuously varied launch speeds, not five fixed lanes");
            var angles=samples.Select(s=>Mathf.Atan2(s.Velocity.x,s.Velocity.z)*Mathf.Rad2Deg).ToArray();
            Check(angleCount>40&&angles.Max()-angles.Min()>source.SpraySettings.SpreadAngle*.8f&&
                angles.All(a=>Mathf.Abs(a)<=source.SpraySettings.SpreadAngle*.5f+.001f),"Water directions vary throughout, and stay within, the authored spray cone");
            Check(sizeCount>30,"Water volume sizes are varied, not identical necklace beads");
            var settings=source.SpraySettings;
            Check(samples.All(s=>s.Velocity.magnitude>=settings.MuzzleSpeed*settings.MinimumSpeedFraction-.001f&&s.Velocity.magnitude<=settings.MuzzleSpeed+.001f),"Random water stays inside the authored speed envelope");
            Check(UnityEngine.Random.state.Equals(randomBefore),"Water randomness does not alter global Unity Random.state");
            var before=emitter.GetParticle(10);var after=emitter.GetParticle(10);
            Check(before.Seed==after.Seed&&before.VisualScale==after.VisualScale&&before.Position==after.Position,"Rendering does not reroll water shape or position each frame");
            emitter.Advance(.12f);
            int tailLengths=Enumerable.Range(0,emitter.ActiveParticleCount).Select(i=>Mathf.RoundToInt(Vector3.Distance(emitter.GetParticle(i).TrailStart,emitter.GetParticle(i).Position)*1000)).Distinct().Count();
            Check(tailLengths>30,"Water has varied physical trail lengths instead of equal five-step spacing");
            metrics.Add("DISTRIBUTION samples="+samples.Length+" speeds="+speedCount+" angles="+angleCount+" sizes="+sizeCount+" trailLengths="+tailLengths);
            if(PhysicalWaterValidation.CaseRun)
            {
                var view=root.AddComponent<FirstRescueEffects>();view.Initialize(emitter,null);view.TickPresentation(.02f);
                Check(view.WaterVolumes.CoreSampleCount>0&&view.ActiveImpactMistCount==0,"Open-air jet creates no impact cloud without an actual collision");
            }
            if(PhysicalWaterValidation.CoreRun)
            {
                var core=samples.Where(s=>s.CoreWater).ToArray();var scatter=samples.Where(s=>!s.CoreWater).ToArray();
                Check(core.Length>0&&scatter.Length>0,"Water has separately simulated core and peripheral scatter");
                Check(core.All(s=>Mathf.Abs(s.Velocity.magnitude-settings.MuzzleSpeed)<.001f&&Mathf.Abs(s.Velocity.x)<.001f),"Core packets share one stable central trajectory, not multiple speed lanes");
                Check(scatter.Select(s=>Mathf.RoundToInt(s.Velocity.magnitude*100)).Distinct().Count()>40,"Peripheral scatter remains physically irregular");
                Check(Mathf.Abs(emitter.TotalEmittedAmount-80f/60*2)<.001f,"Core plus scatter share, rather than double, source flow");
                for(int i=0;i<500;i++)emitter.Tick(root.transform.position+Vector3.forward*10,true,1f/60);
                emitter.Stop();
                Check(emitter.ActiveParticleCount<=SprayEmitter.ParticleCapacity&&
                    Mathf.Abs(emitter.TotalEmittedAmount+emitter.DroppedAmount-580f/60*2)<.003f,"Paired core/scatter emissions stay bounded and conserve water at capacity");
            }
            root.SetActive(false);Destroy(root);
        }
        IEnumerator Momentum()
        {
            var reaction=new GameObject("Temporary momentum reaction").AddComponent<ElementReaction>();
            var fast=Body(new Vector3(90,3,90),1);var slow=Body(new Vector3(93,3,90),1);var heavy=Body(new Vector3(96,3,90),2);
            foreach(var pair in new[]{(fast,10f),(slow,5f),(heavy,10f)})
                reaction.ApplyBallisticImpact(ElementKind.Water,pair.Item1.position+new Vector3(0,.3f,-.5f),Vector3.forward*pair.Item2,.1f,pair.Item1.GetComponent<Collider>(),.9f,5, SprayEffectSettings.Neutral);
            yield return new WaitForFixedUpdate();yield return null;
            Check(fast.linearVelocity.z>0&&fast.angularVelocity.magnitude>.01f,"Real Rigidbody receives water momentum and off-center torque");
            Check(Mathf.Abs(fast.linearVelocity.z/slow.linearVelocity.z-2)<.05f,"Twice water speed produces twice momentum at equal volume");
            Check(Mathf.Abs(fast.linearVelocity.z/heavy.linearVelocity.z-2)<.05f,"Heavier object accelerates less under the same water impulse");
            metrics.Add("Momentum fast="+fast.linearVelocity+" slow="+slow.linearVelocity+" heavy="+heavy.linearVelocity+" angular="+fast.angularVelocity);
            var prior=slow.linearVelocity;reaction.ApplyBallisticImpact(ElementKind.Water,slow.position,Vector3.one*float.NaN,1,slow.GetComponent<Collider>(),1,5,SprayEffectSettings.Neutral);
            yield return new WaitForFixedUpdate();Check((slow.linearVelocity-prior).magnitude<.01f,"Invalid water velocity cannot poison rigidbody motion");
            Destroy(reaction.gameObject);Destroy(fast.gameObject);Destroy(slow.gameObject);Destroy(heavy.gameObject);
        }
        void ElementParity()
        {
            var a=new GameObject("Temporary legacy dose").AddComponent<FireField>();a.Simulate=false;a.SetIgnitionSeeds(Array.Empty<Vector2Int>());a.Configure(new Vector3(140,.1f,140),3,3,.5f);
            var b=new GameObject("Temporary ballistic dose").AddComponent<FireField>();b.Simulate=false;b.SetIgnitionSeeds(Array.Empty<Vector2Int>());b.Configure(new Vector3(150,.1f,140),3,3,.5f);
            var ar=a.gameObject.AddComponent<ElementReaction>();ar.Initialize(a);var br=b.gameObject.AddComponent<ElementReaction>();br.Initialize(b);
            foreach(var kind in new[]{ElementKind.Water,ElementKind.DryPowder})
            {
                a.Ignite(a.CellCenter(1,1),.9f);b.Ignite(b.CellCenter(1,1),.9f);
                ar.ApplySpray(kind,a.CellCenter(1,1),Vector3.forward,.4f,null,.65f,5,SprayEffectSettings.Neutral);
                br.ApplyBallisticImpact(kind,b.CellCenter(1,1),Vector3.forward*10,.4f,null,.65f,5,SprayEffectSettings.Neutral);
                var ac=a.GetCell(1,1);var bc=b.GetCell(1,1);
                Check(ac.Intensity==bc.Intensity&&ac.Wetness==bc.Wetness&&ac.WaterRemaining==bc.WaterRemaining&&ac.Powder==bc.Powder&&ac.PowderRemaining==bc.PowderRemaining,kind+" extinguish/residue are unchanged by momentum integration");
            }
            Destroy(a.gameObject);Destroy(b.gameObject);
        }
        void Water()
        {
            var origin=new Vector3(60,.1f,60);var root=new GameObject("Temporary water fixture");var floor=Cube(root.transform,origin+Vector3.down*.05f,new Vector3(14,.1f,14),Color.white);
            var wall=Cube(root.transform,origin+new Vector3(0,.8f,3.1f),new Vector3(3,1.6f,.2f),new Color(.91f,.87f,.78f));wall.layer=8;
            var actor=new GameObject("Temporary spray actor");actor.transform.SetParent(root.transform);actor.transform.position=origin;
            var source=new GameObject("Temporary water source").AddComponent<FluidSource>();source.transform.SetParent(root.transform);source.transform.position=origin;source.Configure(ElementKind.Water);
            source.SetSprayProfile(AssetDatabase.LoadAssetAtPath<SprayProfile>("Assets/FireRescue/Settings/ReviewWater.asset"));
            var hose=actor.AddComponent<HoseConnection>();Check(hose.TryConnect(source),"Physical water fixture connects");
            var emitter=actor.AddComponent<SprayEmitter>();emitter.Initialize(hose,root.AddComponent<ElementReaction>());emitter.EnableDirectionalSpray(true);emitter.AutomaticSimulation=false;
            var fx=root.AddComponent<FirstRescueEffects>();fx.Initialize(emitter,null);
            int wallHits=0;float wallAmount=0,allImpacts=0;emitter.ParticleImpacted+=h=>{allImpacts+=h.Amount;if(h.Normal.z<-.5f){wallHits++;wallAmount+=h.Amount;}};
            Physics.SyncTransforms();
            for(int i=0;i<120;i++){emitter.Tick(origin+Vector3.forward*8,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            Check(wallHits>0&&wallAmount>0,"Swept water collides with the physical wall instead of passing through");
            Check(Mathf.Abs(emitter.TotalEmittedAmount-2)<.001f,"Solid 3D water retains exactly the source flow (2 units/s)");
            var volume=fx.WaterVolumes;
            if(PhysicalWaterValidation.CaseRun)
            {
                var uv=new List<Vector4>();volume.DisplayMesh.GetUVs(0,uv);
                Check(uv.Count>0&&uv.All(v=>v.z==1),"Normal water mesh contains only the coherent core, no separate glass beads");
                var particles=fx.transform.Find("Water Billboards").GetComponent<ParticleSystem>();
                Check(particles.GetComponent<ParticleSystemRenderer>().renderMode==ParticleSystemRenderMode.Stretch&&
                    !particles.collision.enabled&&!particles.emission.enabled,"Peripheral streaks use the view renderer, not a competing gameplay simulation");
                Check(fx.ActiveImpactMistCount>0&&fx.ActiveInFlightSprayCount<emitter.ActiveParticleCount/2,"Real impacts create short mist while in-flight scatter remains subordinate");
                Check(!fx.transform.Find("Water Impact 0/Splash").GetComponent<Renderer>().enabled&&fx.ActiveDropletCount==0,"Hybrid rendering hides the old spherical splash/droplet meshes");
                var sprayRenderer=particles.GetComponent<ParticleSystemRenderer>();
                var focus=origin+new Vector3(0,.45f,1.5f);
                var visible=CapturePath(PhysicalWaterValidation.Evidence+"/spray-visible.png",focus,2.5f,50,-25,true);
                sprayRenderer.enabled=false;
                var hidden=CapturePath(PhysicalWaterValidation.Evidence+"/spray-hidden.png",focus,2.5f,50,-25,true);
                sprayRenderer.enabled=true;
                int sprayPixels=visible.Zip(hidden,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)>9).Count(x=>x);
                Check(sprayPixels>30,"Impact mist contributes visible rendered pixels, not just particle counters");
                metrics.Add("MIST visible-pixels="+sprayPixels);
                metrics.Add("SPRAY inflight="+fx.ActiveInFlightSprayCount+" impact="+fx.ActiveImpactMistCount+" coreVertices="+volume.DisplayMesh.vertexCount);
            }
            Check(volume.IsVisible&&volume.StreamBodyCount>8&&volume.DisplayMesh.vertexCount>100,"Water is a lit closed 3D mesh, not just billboard particles");
            Check(volume.SecondaryCount>0&&volume.SecondaryCollisions>0,"Impact droplets have gravity, collision and bounded bounces");
            Check(volume.DisplayMesh.vertices.All(Finite)&&volume.DisplayMesh.normals.All(Finite),"Water geometry and normals are finite");
            Check(volume.DisplayMesh.bounds.max.z<63.13f,"Water volume stops at the wall surface");
            if(PhysicalWaterValidation.CoreRun)
            {
                Check(volume.CoreSectionCount==1&&volume.CoreRingCount>8&&volume.CoreLength>2,"One continuous volumetric core dominates the steady spray");
                Check(volume.CoreAttachedToNozzle&&Vector3.Distance(volume.CoreStart,emitter.NozzlePosition)<.001f,"Main water column starts at the actual nozzle");
                Check(volume.CoreEnd.z<=63.01f,"Main water column ends at the blocking wall, never behind it");
                metrics.Add("CORE samples="+volume.CoreSampleCount+" sections="+volume.CoreSectionCount+" rings="+volume.CoreRingCount+" length="+volume.CoreLength);
            }
            var amount=emitter.TotalEmittedAmount;fx.TickPresentation(float.NaN);fx.TickPresentation(float.PositiveInfinity);fx.TickPresentation(.016f);
            Check(emitter.TotalEmittedAmount==amount,"Presentation and secondary droplets do not apply extra water");
            if(PhysicalWaterValidation.SurfaceRun)SurfaceChecks(fx,emitter,origin);
            Capture("water-wall-impact.png",origin+new Vector3(0,.5f,1.6f),3.1f,50,-25);
            metrics.Add("WATER streamBodies="+volume.StreamBodyCount+" droplets="+volume.SecondaryCount+" secondaryContacts="+volume.SecondaryCollisions+" vertices="+volume.DisplayMesh.vertexCount+" wallHits="+wallHits);
            if(PhysicalWaterValidation.SurfaceRun)
            {
                Directory.CreateDirectory("Logs/WaterSurfaceFrames");
                for(int frame=0;frame<24;frame++)
                {
                    if(frame==12)emitter.Stop();
                    for(int step=0;step<8;step++){if(frame<12)emitter.Tick(origin+Vector3.forward*8,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
                    CapturePath("Logs/WaterSurfaceFrames/frame"+frame.ToString("D3")+".png",origin+new Vector3(0,.45f,1.5f),2.5f,50,-25);
                }
                // Restore the original immediate-release wet-surface precondition after recording.
                for(int i=0;i<30;i++){emitter.Tick(origin+Vector3.forward*8,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            }
            emitter.Stop();for(int i=0;i<240;i++){emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            Check(emitter.ActiveParticleCount==0&&volume.StreamBodyCount==0&&volume.SecondaryCount==0&&!volume.IsVisible,"Release drains in-flight water and splash droplets, not an infinite beam");
            if(PhysicalWaterValidation.CoreRun)Check(Mathf.Abs(allImpacts-emitter.TotalEmittedAmount)<.002f,"Core/scatter total collision dose equals emitted water, with no visual bonus dose");
            Check(fx.ActiveSplashCount>0,"Wet surface remains briefly after water stops");
            Capture("water-puddles.png",origin+new Vector3(0,.1f,1.6f),2.6f,65,0);
            if(PhysicalWaterValidation.CoreRun)
            {
                emitter.Tick(origin+Vector3.forward*8,true,1f/60);emitter.Advance(1f/60);fx.TickPresentation(1f/60);
                emitter.Stop();fx.TickPresentation(0);Check(!volume.CoreAttachedToNozzle,"Release detaches the remaining water from the nozzle");
                emitter.Tick(origin+Vector3.right*8,true,1f/60);emitter.Advance(1f/60);fx.TickPresentation(1f/60);
                Check(volume.CoreSectionCount>=2,"Release and re-aim produce separate water bursts, not a bridge between directions");
                emitter.Stop();for(int i=0;i<240;i++){emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            }
            source.Configure(ElementKind.DryPowder);source.SetSprayProfile(null);
            for(int i=0;i<20;i++){emitter.Tick(origin+Vector3.forward*8,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            Check(!volume.IsVisible&&volume.StreamBodyCount==0,"Dry powder is not accidentally rendered as liquid water");
            fx.enabled=false;Check(!fx.IsSubscribed&&!volume.IsVisible,"Disable clears volumes and removes event subscriptions");
            fx.enabled=true;Check(fx.IsSubscribed,"Re-enable reconnects presentation safely");
            root.SetActive(false);Destroy(root);
        }
        void SurfaceChecks(FirstRescueEffects fx,SprayEmitter emitter,Vector3 origin)
        {
            var volume=fx.WaterVolumes;var material=volume.DisplayMaterial;var shader=Resources.Load<Shader>("FlowingWater");
            Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader)&&material.shader==shader,"Real renderer uses the supported custom flowing-water shader");
            Check(material.renderQueue>=3000&&material.GetFloat("_Opacity")>0&&material.GetFloat("_Opacity")<1,"Water uses translucent density, not an opaque plastic material");
            Check(volume.DisplayMesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0)==4,"Water mesh carries real flow-distance and surface-role coordinates");
            Check(fx.transform.Find("Physical water volumes").GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"Water no longer casts solid rod/pebble shadows");
            float clock=volume.PresentationTime;var particles=Enumerable.Range(0,emitter.ActiveParticleCount).Select(i=>emitter.GetParticle(i).Position).ToArray();float dose=emitter.TotalEmittedAmount;
            fx.TickPresentation(.08f);
            Check(Mathf.Abs(volume.PresentationTime-clock-.08f)<.0001f&&Mathf.Abs(material.GetFloat("_PresentationTime")-volume.PresentationTime)<.0001f,"Surface flow advances with the local presentation clock");
            Check(emitter.TotalEmittedAmount==dose&&particles.SequenceEqual(Enumerable.Range(0,emitter.ActiveParticleCount).Select(i=>emitter.GetParticle(i).Position)),"Animating the surface leaves physical particle positions and dose unchanged");
            var focus=origin+new Vector3(0,.45f,1.5f);float phase=material.GetFloat("_PresentationTime");
            var a=CapturePath(PhysicalWaterValidation.Evidence+"/flow-phase-a.png",focus,2.5f,50,-25,true);
            material.SetFloat("_PresentationTime",phase+.23f);
            var b=CapturePath(PhysicalWaterValidation.Evidence+"/flow-phase-b.png",focus,2.5f,50,-25,true);
            material.SetFloat("_PresentationTime",phase);
            int changed=a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)>9).Count(x=>x);
            Check(changed>300,"Actual shader pixels flow even when mesh and gameplay are frozen");
            metrics.Add("SURFACE changed-pixels="+changed+" / "+a.Length+" shader="+shader.name);
        }
        IEnumerator Hose()
        {
            var origin=new Vector3(110,.1f,110);var root=new GameObject("Temporary thick hose fixture");
            Cube(root.transform,origin+Vector3.down*.05f,new Vector3(12,.1f,10),Color.white);
            var wall=Cube(root.transform,origin+Vector3.up*.5f,new Vector3(.18f,1,2),new Color(.91f,.87f,.78f));wall.layer=8;
            var geometry=root.AddComponent<SprayWallOcclusion>();geometry.Configure(new[]{wall.GetComponent<Collider>()},Array.Empty<HoseWindow>());
            var source=new GameObject("Source").AddComponent<FluidSource>();source.transform.SetParent(root.transform);source.transform.position=origin+new Vector3(-1,0,-.8f);
            var actor=new GameObject("Actor");actor.transform.SetParent(root.transform);actor.transform.position=origin+Vector3.left;
            var inlet=new GameObject("Inlet").transform;inlet.SetParent(actor.transform,false);inlet.localPosition=Vector3.up*.9f;
            var hose=actor.AddComponent<HoseConnection>();var so=new SerializedObject(hose);so.FindProperty("maximumLength").floatValue=10;so.ApplyModifiedPropertiesWithoutUndo();hose.ConfigurePhysics(geometry,inlet);hose.SetAutomaticWrapping(true);
            var emitter=actor.AddComponent<SprayEmitter>();emitter.Initialize(hose,null);emitter.SetHoseInletAnchor(inlet);emitter.AutomaticSimulation=false;
            var tube=actor.AddComponent<PhysicalHoseTube>();tube.Initialize(emitter);tube.AutomaticSimulation=false;
            Physics.SyncTransforms();Check(hose.TryConnect(source),"Thick hose fixture connects beside the wall");tube.TickPresentation(0);
            foreach(var target in new[]{new Vector3(-1,0,1.7f),new Vector3(1,0,1.7f),new Vector3(1,0,0)})
            {
                for(int i=0;i<160;i++){var desired=Vector3.MoveTowards(actor.transform.position,origin+target,.025f);actor.transform.position=hose.ConstrainPosition(desired);tube.TickPresentation(1f/120);}
                Check(Vector3.Distance(actor.transform.position,origin+target)<.03f,"Wrapped hose reaches "+target);
            }
            for(int i=0;i<120;i++)tube.TickPresentation(1f/120);
            Check(hose.AutomaticBendCount>0&&hose.UsedLength<10,"Thick hose retains real wall bends and bounded length");
            Check(tube.IsVisible&&tube.NodeCount==128&&tube.DisplayMesh.vertexCount==128*12,"Tube is one continuous 12-sided mesh with stable material nodes");
            bool clear=true;for(int i=1;i<tube.NodeCount;i++)if(hose.IsCableBlocked(tube.NodePosition(i-1),tube.NodePosition(i),tube.RenderRadius-.002f))clear=false;
            Check(clear,"Rendered tube centerline clears the solid wall at its new thickness");
            Check(actor.GetComponentsInChildren<Renderer>().Count(r=>r.name.StartsWith("Hose end coupling")&&r.enabled&&r.gameObject.activeSelf)==4,"Both ends have physical metal couplings and dark seals");
            Capture("hose-wall-wrap.png",origin+Vector3.up*.3f,2.5f,48,-25);
            hose.Disconnect();tube.TickPresentation(0);Check(!tube.IsVisible&&!actor.GetComponentsInChildren<Renderer>().Any(r=>r.name.StartsWith("Hose end coupling")&&r.gameObject.activeSelf),"Disconnect hides tube and all coupling pieces");
            root.SetActive(false);Destroy(root);yield return null;
        }
        IEnumerator SceneWater()
        {
            var player=FindAnyObjectByType<LocalPlayerInput>();var emitter=player.GetComponent<SprayEmitter>();var hose=player.GetComponent<HoseConnection>();
            emitter.AutomaticSimulation=false;player.GetComponent<FirefighterTools>().EquipHose();var source=FindObjectsByType<FluidSource>().First(s=>s.Element==ElementKind.Water);
            var cc=player.GetComponent<CharacterController>();cc.enabled=false;player.transform.position=source.transform.position+new Vector3(.05f,0,-.7f);player.transform.rotation=Quaternion.Euler(0,180,0);cc.enabled=true;
            Physics.SyncTransforms();Check(hose.TryConnect(source),"Current real level still connects the user-provided toilet");
            var fx=FindAnyObjectByType<FirstRescueEffects>();
            for(int i=0;i<65;i++){emitter.Tick(player.transform.position+Vector3.back*7,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
            yield return null;
            Capture("level-water-hose.png",source.transform.position+Vector3.back*.7f+Vector3.up*.4f,2.7f,55,0);
            Check(fx.WaterVolumes.StreamBodyCount>0&&player.GetComponent<PhysicalHoseTube>().IsVisible,"Real house scene uses the new water volume and hose");
            var legacy=fx.GetComponent<SprayParticleView>();
            Check(legacy&&legacy.transform.Find("Directional spray particles").GetComponent<ParticleSystem>().particleCount==0,"Real scene does not overlay the legacy opaque water sprites");
            if(PhysicalWaterValidation.CaseRun)
            {
                // Review at the authored gameplay zoom as well as the diagnostic close-up.
                var main=Camera.main;var angles=main.transform.eulerAngles;
                var focus=player.transform.position+Vector3.up*.7f;
                CapturePath(PhysicalWaterValidation.Evidence+"/level-game-camera.png",focus,main.orthographicSize,angles.x,angles.y,false,1280,720);
                Directory.CreateDirectory("Logs/WaterCaseLevelFrames");
                for(int frame=0;frame<24;frame++)
                {
                    if(frame==12)emitter.Stop();
                    for(int step=0;step<8;step++){if(frame<12)emitter.Tick(player.transform.position+Vector3.back*7,true,1f/120);emitter.Advance(1f/120);fx.TickPresentation(1f/120);}
                    CapturePath("Logs/WaterCaseLevelFrames/frame"+frame.ToString("D3")+".png",focus,main.orthographicSize,angles.x,angles.y,false,1280,720);
                }
                metrics.Add("GAME CAMERA ortho="+main.orthographicSize+" pitch="+angles.x+" yaw="+angles.y+" aspect=16:9 (no IMGUI)");
            }
            emitter.Stop();hose.Disconnect();
        }
        static Rigidbody Body(Vector3 position,float mass)
        {
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<Rigidbody>();body.position=position;body.mass=mass;body.useGravity=false;body.linearDamping=0;body.angularDamping=0;return body;
        }
        static GameObject Cube(Transform root,Vector3 position,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(root);go.transform.position=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(FirstReview1027Setup.Folder+"/"+(color==Color.white?"Kitchen floor":"Warm plaster")+".mat");return go;
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        static void Capture(string file,Vector3 focus,float size,float pitch,float yaw)
            => CapturePath(PhysicalWaterValidation.Evidence+"/"+file,focus,size,pitch,yaw);
        static Color32[] CapturePath(string path,Vector3 focus,float size,float pitch,float yaw,bool pixels=false,int width=1280,int height=900)
        {
            var camera=new GameObject("Temporary water evidence camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=size;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.72f,.73f,.72f);
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);camera.transform.position=focus-camera.transform.forward*25;
            var rt=new RenderTexture(width,height,24);var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());var data=pixels?image.GetPixels32():Array.Empty<Color32>();
            RenderTexture.active=old;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(image);Destroy(camera.gameObject);
            return data;
        }
    }
}
