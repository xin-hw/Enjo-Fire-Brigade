using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FireRescue
{
    public sealed class PhysXHoseLabValidation : MonoBehaviour
    {
        const float Dt=.02f;
        LocalPlayerInput input;
        HoseConnection hose;
        PhysXHoseDriver driver;
        PhysicalHoseTube tube;
        Camera view;
        string output;
        bool failed, finished;
        int steps, frame;
        float peakGap, peakError, peakPenetration;
        SimulationMode previousMode;
        RenderTexture target;
        Texture2D screenshot;
        readonly List<double> cpu=new();
        Collider[] scenery;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string path=Environment.GetEnvironmentVariable("FIRERESCUE_PHYSX_HOSE_OUTPUT");
            if (string.IsNullOrEmpty(path) || !Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--verify-physx")) return;
            var run=new GameObject("Temporary native hose verification").AddComponent<PhysXHoseLabValidation>();
            run.output=path;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"results.txt"),$"Unity {Application.unityVersion}, game {Application.version}, {SystemInfo.graphicsDeviceType}\n");
            Application.logMessageReceived+=OnLog;
            input=FindAnyObjectByType<LocalPlayerInput>();
            input.ReadDevices=false;
            input.SetInputFocus(true);
            hose=input.GetComponent<HoseConnection>();
            tube=input.GetComponent<PhysicalHoseTube>();
            view=Camera.main;
            Time.captureDeltaTime=Dt*2;
            yield return null;
            yield return null;
            driver=hose.Physical;
            Need(driver && hose.IsConnected && driver.IsActive,"Connects a real native rigidbody hose in the independent lab");
            driver.AutomaticPhysics=false;
            tube.AutomaticSimulation=false;
            previousMode=Physics.simulationMode;
            Physics.simulationMode=SimulationMode.Script;
            var world=new List<Collider>();
            foreach(var c in FindObjectsByType<Collider>())
                if (c.enabled && !c.isTrigger && !c.attachedRigidbody &&
                    !c.transform.IsChildOf(input.transform)) world.Add(c);
            scenery=world.ToArray();
            yield return Hold(150,"01-rest");
            var chain=driver.Chain;
            float lowest=float.PositiveInfinity;
            for(int i=1;i<chain.NodeCount-1;i++) lowest=Mathf.Min(lowest,chain.GetPoint(i).y);
            Need(lowest<Mathf.Min(chain.GetPoint(0).y,chain.GetPoint(chain.NodeCount-1).y)-.12f,
                $"Slack drops under native gravity instead of remaining a straight guide (lowest {lowest:F3})");
            Need(chain.ReserveLength>5f && chain.MaterialLength<5f && hose.BreakCount==0,
                $"Undeployed hose remains in the reel without premature tension (out {chain.MaterialLength:F2}, reserve {chain.ReserveLength:F2})");
            int dynamic=0;
            foreach(var body in chain.GetComponentsInChildren<Rigidbody>()) if (!body.isKinematic) dynamic++;
            Need(dynamic==chain.ActiveBodyCount && dynamic>2,"The displayed hose is made of live dynamic rigidbodies and native joints");

            yield return Walk(new Vector3(.7f,.125f,-.25f),"02-drag");
            yield return Walk(new Vector3(.7f,.125f,-4.1f),"03-bend");
            yield return Walk(new Vector3(3.2f,.125f,-4.1f),"04-corner");
            Need(hose.IsConnected && hose.BreakCount==0,"Dragging and turning around a real wall does not detach available hose");
            for(int i=0;i<400 && !driver.IsTaut;i++)
            {
                Step(Vector2.right,new Vector3(12f,.1f,-4.1f));
                if(i%2==1) { yield return null; Capture("05-pull"); }
            }
            Need(driver.IsTaut && driver.Chain.ReserveLength<.02f && driver.Chain.MaterialLength>9.8f,
                $"Movement meets the native load only after reel reserve is exhausted (out {chain.MaterialLength:F3}, reserve {chain.ReserveLength:F3}, load {driver.SourceLoad:F1}N)");
            for(int i=0;i<9;i++) Step(Vector2.right,new Vector3(12f,.1f,-4.1f));
            Need(hose.IsConnected,"A short pull stops at tension without instant detachment");
            yield return Hold(25,"06-release");
            Need(hose.IsConnected && !hose.IsPullingTaut,"Releasing movement resets the pull fatigue");
            Vector3 stopped=input.transform.position;
            for(int i=0;i<200 && hose.IsConnected;i++)
            {
                Step(Vector2.right,new Vector3(12f,.1f,-4.1f));
                if(i%2==1) { yield return null; Capture("07-detach"); }
            }
            Need(!hose.IsConnected && hose.BreakCount==1 && driver.IsReeling,
                $"Sustained real source load releases the coupling once (player moved {Vector3.Distance(stopped,input.transform.position):F3}m)");
            int reelSteps=0;
            while(driver.IsReeling && reelSteps<700)
            {
                Step(Vector2.zero,input.transform.position+Vector3.left*2f);
                reelSteps++;
                if(reelSteps%2==0) { yield return null; Capture("08-reel"); }
            }
            Need(!driver.IsReeling && !tube.IsVisible,"The native bodies physically reach the hand and are retrieved without a timer hide");
            Need(peakPenetration<.02f,$"Native capsule contacts respect the original scenery (maximum overlap {peakPenetration:F4}m)");
            Need(peakGap<.08f,$"The native chain stays connected under load (maximum joint gap {peakGap:F4}m)");
            cpu.Sort();
            File.AppendAllText(Path.Combine(output,"results.txt"),$"PhysX steps {steps}, p95 {cpu[(int)((cpu.Count-1)*.95f)]:F3}ms including native world simulation, peak grip error {peakError:F4}m; reel {reelSteps*Dt:F2}s.\n");
            var a=new GameObject("Temporary source A").AddComponent<FluidSource>();
            var b=new GameObject("Temporary source B").AddComponent<FluidSource>();
            a.transform.position=input.transform.position+Vector3.right*.4f;
            b.transform.position=input.transform.position+Vector3.left*.4f;
            Need(hose.TryConnect(a),"An empty reel can connect to a new nearby source");
            var live=driver.Chain;
            Need(!hose.TryConnect(b) && !hose.IsConnected && driver.IsReeling && driver.Chain==live,
                "Changing source releases the existing physical chain instead of deleting it");
            Need(!hose.TryConnect(b) && driver.Chain==live,"Reconnection is blocked while deployed material is returning");
            for(int i=0;i<300 && driver.IsReeling;i++) { Step(Vector2.zero,input.transform.position+Vector3.right*2f); if(i%2==1) yield return null; }
            Need(!driver.IsReeling && hose.TryConnect(b),"Connection becomes available only after physical retrieval");
            live=driver.Chain;
            b.gameObject.SetActive(false);
            Step(Vector2.zero,input.transform.position+Vector3.right*2f);
            Need(!hose.IsConnected && driver.IsReeling && driver.Chain==live,"A disabled source releases the same live bodies safely");
            for(int i=0;i<300 && driver.IsReeling;i++) { Step(Vector2.zero,input.transform.position+Vector3.right*2f); if(i%2==1) yield return null; }
            Need(!driver.IsReeling,"A lost source also physically retrieves to the hand");
            Destroy(a.gameObject);Destroy(b.gameObject);
            Finish();
        }

        IEnumerator Walk(Vector3 destination,string phase)
        {
            for(int i=0;i<450;i++)
            {
                Vector3 d=destination-input.transform.position;d.y=0;
                if(d.magnitude<.12f) break;
                Step(Vector2.ClampMagnitude(new Vector2(d.x,d.z)*4f,1f),destination);
                if(i%2==1) { yield return null; Capture(phase); }
            }
            Vector3 error=destination-input.transform.position;error.y=0;
            Need(error.magnitude<.2f && hose.IsConnected,$"Walk {phase} reaches {input.transform.position:F2}, deployed {driver.Chain.MaterialLength:F2}, reserve {driver.Chain.ReserveLength:F2}, load {driver.SourceLoad:F1}N");
        }

        IEnumerator Hold(int count,string phase)
        {
            for(int i=0;i<count;i++)
            {
                Step(Vector2.zero,input.transform.position+Vector3.right*3f);
                if(i%2==1) { yield return null; Capture(phase); }
            }
        }

        void Step(Vector2 move,Vector3 aim)
        {
            input.TickPointerCommand(move,aim,false,false,false,false,false,false,Dt);
            Physics.SyncTransforms();
            long began=System.Diagnostics.Stopwatch.GetTimestamp();
            int substeps=Mathf.Max(1,Mathf.RoundToInt(Dt/Time.fixedDeltaTime));
            float step=Dt/substeps;
            for(int i=0;i<substeps;i++)
            {
                driver.PreparePhysics(step);
                Physics.Simulate(step);
                driver.AfterPhysics(step);
            }
            cpu.Add((System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000d/System.Diagnostics.Stopwatch.Frequency);
            tube.TickPresentation(Dt);
            steps++;
            if (!driver.IsActive) return;
            var chain=driver.Chain;
            peakGap=Mathf.Max(peakGap,chain.MaximumJointGap);
            peakError=Mathf.Max(peakError,chain.GripError);
            if(steps%10!=0) return;
            foreach(var capsule in chain.GetComponentsInChildren<CapsuleCollider>())
            {
                if(!capsule.enabled) continue;
                foreach(var obstacle in scenery)
                    if(obstacle && !Physics.GetIgnoreCollision(capsule,obstacle) && Physics.ComputePenetration(capsule,capsule.transform.position,capsule.transform.rotation,
                        obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float depth))
                    {
                        if(depth>peakPenetration+.001f)
                            File.AppendAllText(Path.Combine(output,"results.txt"),$"CONTACT t={steps*Dt:F2} body={capsule.name} at={capsule.transform.position:F3} obstacle={obstacle.name} at={obstacle.transform.position:F3} depth={depth:F4} reel={driver.IsReeling}\n");
                        peakPenetration=Mathf.Max(peakPenetration,depth);
                    }
            }
            if(steps%50==0)
                File.AppendAllText(Path.Combine(output,"results.txt"),$"STATE t={steps*Dt:F2} pos={input.transform.position:F2} out={chain.MaterialLength:F3} reserve={chain.ReserveLength:F3} load={driver.SourceLoad:F1}N error={chain.GripError:F3} gap={chain.MaximumJointGap:F4} pull={driver.PullSeconds:F2}\n");
        }

        void Capture(string phase)
        {
            if(!target) { target=new RenderTexture(1024,576,24); screenshot=new Texture2D(1024,576,TextureFormat.RGB24,false); }
            var previous=RenderTexture.active;
            view.targetTexture=target;
            view.Render();
            RenderTexture.active=target;
            screenshot.ReadPixels(new Rect(0,0,1024,576),0,0);
            File.WriteAllBytes(Path.Combine(output,$"{phase}-{frame++:D4}.png"),screenshot.EncodeToPNG());
            view.targetTexture=null;
            RenderTexture.active=previous;
        }

        void Need(bool condition,string message)
        {
            File.AppendAllText(Path.Combine(output,"results.txt"),(condition?"PASS ":"FAIL ")+message+"\n");
            if(!condition) throw new InvalidOperationException(message);
        }
        void OnLog(string message,string stack,LogType kind)
        {
            if(kind!=LogType.Error && kind!=LogType.Exception && kind!=LogType.Assert) return;
            failed=true;
            File.AppendAllText(Path.Combine(output,"results.txt"),message+"\n"+stack+"\n");
            Finish();
        }
        void Finish()
        {
            if(finished)return;
            finished=true;
            Application.logMessageReceived-=OnLog;
            Physics.simulationMode=previousMode;
            File.AppendAllText(Path.Combine(output,"results.txt"),failed?"PHYSX_HOSE_FAIL\n":"PHYSX_HOSE_PASS\n");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall+=()=>UnityEditor.EditorApplication.Exit(failed?1:0);
#else
            Application.Quit(failed?1:0);
#endif
        }
        void OnDestroy()
        {
            if(target)Destroy(target);
            if(screenshot)Destroy(screenshot);
        }
    }
}
