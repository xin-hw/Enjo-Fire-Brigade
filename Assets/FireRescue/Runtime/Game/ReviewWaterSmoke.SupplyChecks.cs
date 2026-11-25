using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FireRescue
{
    public sealed partial class ReviewWaterSmoke
    {
        IEnumerator VerifySupply(BreakableDoor bathroom)
        {
            simulateFire = false;
            var toilet = supplies.Single(source => source.Element == ElementKind.Water);
            var powder = supplies.Single(source => source.Element == ElementKind.DryPowder);
            var hud = FindAnyObjectByType<ReviewMissionHud>();
            Need(toilet.HasCapacity && toilet.CanRefill && toilet.RemainingAmount == toilet.Capacity &&
                powder.HasCapacity && !powder.CanRefill && powder.RemainingAmount == powder.Capacity,
                "The toilet and powder start full with distinct refill rules");
            Need(hud && hud.SupplySlots.SequenceEqual(new[] { toilet, powder, bookRescue.Soda }) && !hud.FocusedSupply,
                "Water, powder and locked soda are visible at spawn without a connection");
            yield return SaveScreen("reserve-at-spawn");

            yield return WalkFromSpawnToBathroom();
            yield return Axe(bathroom);
            yield return Walk(ToiletApproach);
            Need(hud.FocusedSupply == toilet, "The meter highlights the nearby toilet before connecting");
            Step(interact: true);
            Need(hose.Source == toilet, "The reserve probe connects the real toilet");
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.6f,.1f,-1.34f));
            var ground = new Vector3(1.65f,.1f,-1.45f);
            int halfTank = Mathf.CeilToInt((float)toilet.Capacity / (2f * toilet.FlowRate * Dt));
            yield return Hold(halfTank, ground, true);
            Need(hud.FocusedSupply == toilet && toilet.RemainingFraction > .48f && toilet.RemainingFraction < .52f,
                "The connected meter reads the real depleted reserve");
            yield return SaveScreen("water-held");
            yield return Hold(halfTank + 120, ground, false);
            Need(toilet.RemainingAmount == 0d && Math.Abs(toilet.DrawnAmount - toilet.Capacity) < .001d &&
                toilet.RefilledAmount == 0d && !spray.IsSpraying && water.Airborne < .00001d,
                "Holding spray at empty stops new water and lets existing water settle");
            yield return SaveScreen("water-empty");

            for (int i = 0; i < 180; i++) { Step(aim: ground); if (i % 12 == 11) yield return null; }
            Need(toilet.IsRefilling && toilet.RemainingAmount > 0d && Math.Abs(water.BalanceError) < .002d,
                "Release refills the toilet without adding water to the field");
            yield return SaveScreen("water-refilling");
            yield return Hold(24, ground, true);
            Need(spray.IsSpraying && toilet.DrawnAmount > toilet.Capacity, "A new press can spray the refilled water");
            Step(interact: true);
            double afterDetach = toilet.RemainingAmount;
            for (int i = 0; i < 300; i++) { Step(); if (i % 12 == 11) yield return null; }
            Need(!hose.IsConnected && !hose.IsReeling && !hud.FocusedSupply && hud.SupplySlots.Contains(toilet) &&
                toilet.RemainingAmount > afterDetach &&
                Math.Abs(water.BalanceError) < .002d, "The toilet keeps refilling after the hose detaches");
            yield return SaveScreen("water-refilling-detached");

            // Start the independent powder probe at the same approach as the rescue walkthrough.
            var body = input.GetComponent<CharacterController>();
            body.enabled = false;
            body.transform.position = new Vector3(-5.4f,.125f,3.95f);
            body.enabled = true;
            Physics.SyncTransforms();
            Step();
            Need(hud.FocusedSupply == powder, "The meter highlights the nearby powder before connecting");
            Step(interact: true);
            Need(hose.Source == powder, "The reserve probe connects the real powder supply");
            var storeExit = new Vector3(-5.37f,.12f,2.9f);
            int halfBag = Mathf.CeilToInt((float)powder.Capacity / (2f * powder.FlowRate * Dt));
            yield return Hold(halfBag, storeExit, true);
            yield return SaveScreen("powder-held");
            yield return Hold(halfBag + 120, storeExit, false);
            Need(powder.RemainingAmount == 0d && Math.Abs(powder.DrawnAmount - powder.Capacity) < .001d &&
                !spray.IsSpraying && spray.ActiveParticleCount == 0, "Powder stops at empty after a finite number of packets");
            yield return SaveScreen("powder-empty");
            Step(interact: true);
            for (int i = 0; i < 300; i++) { Step(); if (i % 12 == 11) yield return null; }
            Step(interact: true);
            Need(hose.Source == powder, "The empty powder source can still be reconnected");
            float emitted = spray.TotalEmittedAmount;
            yield return Hold(60, storeExit, true);
            Need(powder.RemainingAmount == 0d && powder.RefilledAmount == 0d && spray.TotalEmittedAmount == emitted,
                "Waiting, reconnecting, and pressing again do not restore powder");

            IEnumerator Hold(int ticks, Vector3 aim, bool press)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Step(aim: aim, pressed: press && i == 0, held: true);
                    if (i % 12 == 11) { water.RefreshView(); yield return null; }
                }
            }

            IEnumerator SaveScreen(string name)
            {
                if (!captureEvidence) yield break;
                water.RefreshView();
                yield return null;
                Capture(name + "-world");
                if (!Application.isEditor && !Application.isBatchMode)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
                    yield return null;
                    yield return null;
                }
            }
        }
    }
}
