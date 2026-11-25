using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    public sealed partial class ReviewWaterSmoke
    {
        bool nativeChecking;
        int nativeEvidenceStep, nativePhysicsSteps;
        float nativeGap, nativePenetration;
        Collider[] nativeScenery;
        readonly List<double> nativeCpu=new();

        IEnumerator VerifyNativeCornerReel(BreakableDoor bathroom, BreakableDoor kitchen)
        {
            simulateFire = false;
            Time.captureDeltaTime = nativePlayerLoop ? Dt : 4f * Dt;
            yield return WalkFromSpawnToBathroom();
            yield return Axe(bathroom);
            yield return Walk(ToiletApproach);
            Step(interact: true);
            Need(hose.IsConnected && hose.Physical.IsActive, "Corner reel starts from the real toilet connection");
            yield return Walk(new Vector3(.45f, .1f, .18f));
            yield return Walk(new Vector3(.48f, .1f, -3.70f));
            yield return Walk(new Vector3(-2.84f, .1f, -3.66f));
            yield return Walk(new Vector3(-2.84f, .1f, -1.96f));
            yield return Axe(kitchen);

            var driver = hose.Physical;
            var tube = input.GetComponent<PhysicalHoseTube>();
            float initialLength = driver.DeployedLength;
            nativeScenery = FindObjectsByType<Collider>().Where(c => c.enabled && !c.isTrigger &&
                !c.attachedRigidbody && !c.transform.IsChildOf(input.transform)).ToArray();
            if (captureEvidence) Capture("corner-start");
            nativeChecking = true;
            Step(aim: kitchen.transform.position, interact: true);
            if (hose.StowInstantly)
            {
                Need(!hose.IsConnected && !driver.HasDeployedMaterial && !driver.IsReeling && initialLength > 7f,
                    "E at the kitchen wall immediately stows the long hose and releases its rigidbodies");
                yield return null;
                Need(!tube.IsVisible, "The next rendered frame has no tube at the kitchen corner");
                if (captureEvidence) Capture("corner-stowed");
                yield return Walk(new Vector3(-2.84f, .1f, -.28f));
                yield return Walk(new Vector3(-4.65f, .1f, -.43f));
                Need(!tube.IsVisible && !driver.HasDeployedMaterial, "The player can cross and turn along the wall with the hose stowed");
                if (captureEvidence) Capture("corner-clear");
                yield break;
            }
            Need(driver.IsReeling && !hose.IsConnected && initialLength > 7f, "E releases the long hose at the kitchen corner");
            Vector3[] route = { new(-2.84f, .1f, -.28f), new(-4.65f, .1f, -.43f), new(-4.65f, .1f, .40f) };
            int stop = 0, ticks = 0, inletBlockedTicks = 0;
            float peakGrip = 0f, peakBend = 0f, worstGripAt = 0f, worstBendAt = 0f, halfwayAt = 0f;
            Vector3 worstGripPosition = default;
            Vector3 worstBendPosition = default;
            while (driver.IsReeling && ticks < 600)
            {
                Vector3 target = route[stop];
                Vector3 delta = target - input.transform.position;
                if (new Vector2(delta.x, delta.z).sqrMagnitude < .01f && stop < route.Length - 1)
                    delta = (target = route[++stop]) - input.transform.position;
                Step(Vector2.ClampMagnitude(new Vector2(delta.x, delta.z) * 4f, 1f), target);
                ticks++;
                if (nativePlayerLoop || ticks % 4 == 0) yield return null;
                if (!driver.IsReeling) break;
                var chain = driver.Chain;
                if (halfwayAt == 0f && chain.MaterialLength <= initialLength * .5f)
                    halfwayAt = ticks * Dt;
                if (chain.GripError > peakGrip)
                {
                    peakGrip = chain.GripError;
                    worstGripAt = ticks * Dt;
                    worstGripPosition = input.transform.position;
                }
                if (Physics.CheckSphere(hose.InletPosition, driver.CollisionRadius, 1 << 8, QueryTriggerInteraction.Ignore))
                    inletBlockedTicks++;
                for (int i = 2; i < chain.NodeCount - 1; i++)
                {
                    Vector3 joint = chain.GetPoint(i - 1);
                    float bend = Vector3.Angle(joint - chain.GetPoint(i - 2), chain.GetPoint(i) - joint);
                    if (bend <= peakBend) continue;
                    peakBend = bend;
                    worstBendAt = ticks * Dt;
                    worstBendPosition = joint;
                }
            }
            yield return null;
            if (captureEvidence) Capture("corner-stowed");
            File.AppendAllText(Path.Combine(output, "results.txt"),
                $"CORNER {(nativePlayerLoop ? "player loop" : "manual fixed steps")} {initialLength:F2}m in {ticks * Dt:F2}s; " +
                $"halfway at {halfwayAt:F2}s; " +
                $"grip {peakGrip:F3}m at {worstGripAt:F2}s ({worstGripPosition:F3}); inlet overlap {inletBlockedTicks} ticks; " +
                $"link gap {nativeGap:F3}m; solid overlap {nativePenetration:F3}m; " +
                $"bend {peakBend:F1} degrees at {worstBendAt:F2}s ({worstBendPosition:F3}).\n");
            Need(stop >= 1, "The player crosses the broken kitchen door and turns along the wall during retrieval");
            Need(!driver.IsReeling && !tube.IsVisible, "The deployed bodies physically return to the reel");
            Need(peakGrip < .28f && nativeGap < .08f && nativePenetration < .02f && peakBend < 125f,
                "The hose remains coupled and clear of the kitchen wall during retrieval");
        }

        void SampleNativeMetrics(double milliseconds)
        {
            if(!nativeChecking) return;
            if(milliseconds>=0d) nativeCpu.Add(milliseconds);
            if(!hose.Physical.IsActive) return;
            var chain=hose.Physical.Chain;
            nativeGap=Mathf.Max(nativeGap,chain.MaximumJointGap);
            if(nativePhysicsSteps++%4!=0) return;
            foreach(var cap in chain.GetComponentsInChildren<CapsuleCollider>())
                foreach(var obstacle in nativeScenery)
                    if(obstacle && obstacle.enabled && !Physics.GetIgnoreCollision(cap,obstacle) && Physics.ComputePenetration(cap,cap.transform.position,cap.transform.rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float depth))
                    {
                        if(depth>nativePenetration+.003f)
                            File.AppendAllText(Path.Combine(output,"results.txt"),$"CONTACT {cap.name} with={obstacle.name} depth={depth:F4} reel={hose.Physical.IsReeling}\n");
                        nativePenetration=Mathf.Max(nativePenetration,depth);
                    }
        }
        IEnumerator VerifyNativeHose(BreakableDoor bathroom, BreakableDoor exit)
        {
            simulateFire=false;
            nativeChecking=true;
            nativeScenery=FindObjectsByType<Collider>().Where(c=>c.enabled && !c.isTrigger && !c.attachedRigidbody && !c.transform.IsChildOf(input.transform)).ToArray();
            var driver=hose.Physical;
            var tube=input.GetComponent<PhysicalHoseTube>();
            Time.captureDeltaTime=nativePlayerLoop?Dt:4f*Dt;
            File.AppendAllText(Path.Combine(output,"results.txt"),$"Native hose simulation {(nativePlayerLoop?"Unity player loop":"manual fixed steps")}, timestep {Time.fixedDeltaTime:F3}s.\n");
            yield return Walk(new Vector3(2.6f,.1f,-4.94f));
            yield return Walk(new Vector3(5.44f,.1f,-4.94f));
            yield return Axe(exit);
            yield return Walk(new Vector3(.48f,.1f,-4.94f));
            yield return WalkFromSpawnToBathroom();
            yield return Axe(bathroom);
            yield return Walk(ToiletApproach);
            Step(interact:true);
            Need(hose.IsConnected && driver.IsActive,"The original toilet supplies a native hose through the original E interaction");
            var source=hose.Source;
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.6f,.1f,-.30f));
            yield return Walk(new Vector3(5.44f,.1f,-.30f));
            yield return Walk(new Vector3(5.44f,.1f,-4.94f));
            Need(hose.IsConnected && hose.BreakCount==0,"The native hose follows the bathroom exit without early detachment");
            Vector3 aim=new(9f,.1f,-4.94f);
            for(int i=0;i<420 && hose.IsConnected && !driver.IsTaut;i++)
            {
                Step(Vector2.right,aim,pressed:i==0,held:true);
                if(nativePlayerLoop || i%4==3) yield return null;
            }
            Need(driver.IsTaut && hose.IsConnected && driver.Chain.ReserveLength<.01f,
                $"Actual source load limits movement at full deployment: {hose.UsedLength:F3}m, {driver.SourceLoad:F1}N at {input.transform.position:F2}");
            for(int i=0;i<10;i++)
            {
                Step(Vector2.right,aim,held:true);
                if(nativePlayerLoop || i%4==3) yield return null;
            }
            Need(hose.IsConnected,"The initial tug does not detach immediately");
            replayHasFocus = false;
            for(int i=0;i<60;i++)
            {
                Step(Vector2.right,aim,held:true);
                if(nativePlayerLoop || i%4==3) yield return null;
            }
            Need(hose.IsConnected && !driver.IsPulling && !spray.IsSpraying,"Losing input focus clears pull intent and supply emission without detaching");
            replayHasFocus = true;
            for(int i=0;i<200 && hose.IsConnected;i++)
            {
                Step(Vector2.right,aim,pressed:i==0,held:true);
                if(i%30==0 && driver.IsActive) File.AppendAllText(Path.Combine(output,"results.txt"),$"MAIN PULL pos={input.transform.position:F3} load={driver.SourceLoad:F1}N fatigue={driver.PullSeconds:F3} tangent={driver.Chain.LastTangent:F3} error={driver.Chain.GripError:F3}\n");
                if(nativePlayerLoop || i%4==3) yield return null;
            }
            bool expectedStow = hose.StowInstantly ? !driver.HasDeployedMaterial && !driver.IsReeling : driver.IsReeling;
            Need(!hose.IsConnected && hose.BreakCount == 1 && expectedStow && !spray.IsSpraying,
                $"Sustained native load detaches once, stops emission and stows the hose as configured (connected={hose.IsConnected}, breaks={hose.BreakCount}, reel={driver.IsReeling}, spray={spray.IsSpraying})");
            double drawn=source.WaterDrawn;
            int airborne=spray.ActiveParticleCount;
            var reelLength=new List<float> { driver.DeployedLength };
            for(int i=0;i<1000 && driver.IsReeling;i++)
            {
                Step(aim:input.transform.position+Vector3.right*3f);
                if(nativePlayerLoop || i%4==3) yield return null;
                reelLength.Add(driver.DeployedLength);
            }
            // In the normal player loop the last fixed step precedes the tube's
            // LateUpdate. Let presentation consume the empty chain before checking it.
            if(nativePlayerLoop) yield return null;
            Need(!driver.IsReeling && !tube.IsVisible && Math.Abs(source.WaterDrawn-drawn)<.00001,
                $"The hose is stowed and the source stays stopped (airborne at release {airborne}, reeling={driver.IsReeling}, visible={tube.IsVisible}, extra drawn={source.WaterDrawn-drawn:F6})");
            water.RefreshView();
            Need(Math.Abs(water.BalanceError)<.002,"Disconnect and physical retrieval preserve the water ledger");
            Need(nativePenetration<.02f,$"The native hose respects original level contacts (maximum overlap {nativePenetration:F4}m)");
            Need(nativeGap<.08f,$"The original-level native chain stays connected (maximum gap {nativeGap:F4}m)");
            if (!hose.StowInstantly)
            {
                int interval = Mathf.Min(45, (reelLength.Count - 1) / 3);
                int middle = (reelLength.Count - interval) / 2;
                File.AppendAllText(Path.Combine(output,"results.txt"),$"REEL {(reelLength.Count-1)*Dt:F2}s; beginning/middle/ending distance over {interval*Dt:F2}s intervals: {reelLength[0]-reelLength[interval]:F3}/{reelLength[middle]-reelLength[middle+interval]:F3}/{reelLength[reelLength.Count-1-interval]-reelLength[reelLength.Count-1]:F3}m.\n");
            }
            if(nativeCpu.Count>0)
            {
                nativeCpu.Sort();
                File.AppendAllText(Path.Combine(output,"results.txt"),$"NATIVE physics CPU p95 {nativeCpu[(int)((nativeCpu.Count-1)*.95f)]:F3}ms incl. world Simulate, excluding mesh/drawing/captures/contact verification.\n");
            }
            if(captureEvidence) Capture($"sequence-{nativeEvidenceStep+4:D5}");
        }
    }
}
