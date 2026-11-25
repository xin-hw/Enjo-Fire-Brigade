using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FireRescue
{
    public sealed partial class ReviewWaterSmoke
    {
        IEnumerator VerifyBookRescue(Dictionary<string, BreakableDoor> doors)
        {
            var story = bookRescue;
            bool verifySofaWater = Environment.GetCommandLineArgs().Contains("--verify-sofa-water");
            bool verifyComputerSpread = Environment.GetCommandLineArgs().Contains("--verify-computer-spread");
            var study = story.ComputerSpread;
            var book = FindObjectsByType<CarryItem>().Single();
            var gasView = FindAnyObjectByType<CarbonDioxideFlowView>();
            var art = view.GetComponent<FireWaterArtView>();
            var outlines = view.GetComponent<WorldOutlineView>();
            var pose = story.Man.GetComponent<BookRescueManPose>();
            var manEffects = story.Man.GetComponentInChildren<ClassifiedFireView>();
            var sofaEffects = story.SofaFire.GetComponentInChildren<ClassifiedFireView>();
            var sofa = story.SofaFire.GetComponent<BoxCollider>().bounds;
            Vector3 seatPoint = story.Man.position + story.Man.rotation * new Vector3(-.93f, 0f, .46f);
            bool seatCellExists = field.TryGetCell(seatPoint, out var seatCell);
            Need(!resident && !pan && book.ItemId == "book" && !book.IsSafeToTake &&
                story.CurrentStage == BookRescueSequence.Stage.Seated && story.SofaFire.IsBurning && !story.Soda.isActiveAndEnabled,
                "Original resident and cooking goal are gone; the new man starts seated by the sofa fire");
            Need(story.SofaFire.Kind == FireKind.Ordinary && story.ManFire.Kind == FireKind.CookingOil &&
                story.FridgeFire.Kind == FireKind.Electrical && story.ComputerFire.Kind == FireKind.Electrical,
                "The actual furniture, man and electronics have distinct fire classes");
            Need(seatCellExists && !field.GetCell(seatCell.x, seatCell.y).IsBurning,
                "The opening sofa fire starts on the sofa without duplicating its ignition on the floor");
            int sofaFloorCells = 0;
            for (int z = 0; z < field.Height; z++)
            for (int x = 0; x < field.Width; x++)
            {
                Vector3 center = field.CellCenter(x, z);
                if (center.x < sofa.min.x || center.x > sofa.max.x || center.z < sofa.min.z || center.z > sofa.max.z) continue;
                if (field.GetCell(x, z).Fuel == 0f) sofaFloorCells++;
                else Need(false, $"A ground fire can burn inside the sofa at {center:F3}");
            }
            Need(sofaFloorCells > 0, $"The sofa owns the flame over its occupied floor ({sofaFloorCells} fuel-free cells)");
            yield return Record("01-start");

            yield return Walk(new Vector3(5.16f,.1f,-4.72f));
            yield return Walk(new Vector3(5.16f,.1f,-.29f));
            yield return Walk(new Vector3(.45f,.1f,-.29f));
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Axe(doors["door.003"]);
            yield return Walk(ToiletApproach);
            Step(interact: true);
            Need(hose.Source && hose.Source.Element == ElementKind.Water, "Walks around the sofa and connects the original toilet");
            for (int tick = 0; tick < 1200 && story.CurrentStage != BookRescueSequence.Stage.Drinking && mission.AllowsInput; tick++)
            {
                Step();
                if (tick % 4 != 3) continue;
                yield return null;
                if (captureEvidence) Capture($"story-{tick/4:D3}");
            }
            Need(story.ManFire.IsBurning && story.TrailIgnitions > 3 && story.FridgeFire.IsBurning && story.ComputerFire.IsBurning &&
                story.Soda.gameObject.activeSelf && !story.Soda.isActiveAndEnabled,
                "The man leaves a fire trail, takes the soda, returns to the sofa and starts the computer fire; his source stays locked");
            int fridgeIgnitionsOnReturn = story.FridgeHeatIgnitions;
            Need(story.Man.lossyScale.y > 1.1f && story.Soda.transform.parent == pose.BottleHand &&
                Vector3.Distance(pose.BottleHand.position, story.Soda.transform.position) < .15f,
                "The larger man holds the soda model in his right hand");
            yield return Record("02-returned-burning");
            for (int tick = 0; tick < 36; tick++)
            {
                Step();
                if (tick % 4 != 3) continue;
                yield return null;
                if (captureEvidence) Capture($"sip-{tick/4:D3}");
            }
            Need(study && study.Fires.Count > 0 && study.IgnitedCount == 0 && !study.DoorwayBurning,
                "The computer lights first, leaving the entrance clear at the start");
            if (verifyComputerSpread)
            {
                bool sawIntermediateSpread = false;
                for (int tick = 0; tick < 575 && !study.DoorwayBurning; tick++)
                {
                    Step();
                    sawIntermediateSpread |= study.IgnitedCount > 0 && !study.DoorwayBurning;
                    if (tick % 4 != 3) continue;
                    yield return null;
                    if (captureEvidence) Capture($"study-spread-{tick / 4:D3}");
                }
                Need(sawIntermediateSpread && study.DoorwayBurning &&
                    study.Fires.All(fire => fire.IsBurning && fire.Kind == FireKind.Electrical),
                    "Electrical flames spread through intermediate points before filling the doorway");
                yield return Record("study-doorway-lit");
                yield break;
            }
            if (!verifySofaWater) yield return VerifyResourceFocus("resource-locked", false);

            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.47f,.1f,-1.40f));
            yield return SprayPath(new Vector3(.52f,.1f,-2.85f), new Vector3(.52f,.1f,-3.85f), 228);
            yield return WalkSafe(new Vector3(.48f,.1f,-3.75f));
            var sofaAim = new Vector3(1.02f,.58f,-2.781f);
            if (verifySofaWater)
            {
                float heatBefore = story.ManFire.Intensity;
                yield return HitFire(story.ManFire, AimAtMan(), ElementKind.Water, 34, label: "oil-contact");
                Need(story.ManFire.Intensity > heatBefore && manEffects.SmokePuffs > 0 && !story.Soda.isActiveAndEnabled,
                    "Real water contact on the man's oil fire raises intensity, emits dark smoke and cannot unlock the soda");
                yield return Record("03-oil-water-smoke");
                yield return HitFire(story.SofaFire, sofaAim, ElementKind.Water, 260,
                    () => !story.SofaFire.IsBurning, "sofa-water");
                Need(!story.SofaFire.IsBurning && sofaEffects.SmokePuffs > 0 && !field.GetCell(seatCell.x, seatCell.y).IsBurning,
                    "A continuous water spray extinguishes the sofa ignition with steam and no separate seat-front flame");
                yield return Record("sofa-water-out");
                yield break;
            }
            yield return SprayPath(new Vector3(-.46f,.1f,-3.61f), new Vector3(-3.17f,.1f,-3.63f), 250);
            yield return Record("water-path-to-storage");
            yield return WalkSafe(new Vector3(-2.75f,.1f,-3.75f));
            yield return SprayPath(new Vector3(-2.84f,.1f,-2.93f), new Vector3(-2.84f,.1f,-1.58f), 152);
            yield return WalkSafe(new Vector3(-2.84f,.1f,-1.96f));
            yield return Axe(doors["door.004"]);
            yield return SprayPath(new Vector3(-2.94f,.1f,-1.75f), new Vector3(-2.91f,.1f,-.14f), 132);
            Step(interact: true);
            yield return WalkSafe(new Vector3(-2.84f,.1f,-.25f));
            yield return Walk(new Vector3(-5.38f,.1f,2.80f));
            yield return Walk(new Vector3(-5.40f,.1f,3.95f));
            yield return FinishReel();
            Step(interact: true);
            Need(hose.Source && hose.Source.Element == ElementKind.DryPowder, "Water clears the original path to the storage baking soda");

            yield return Walk(new Vector3(-5.38f,.1f,2.80f));
            yield return Walk(new Vector3(-2.84f,.1f,-.25f));
            yield return WalkSafe(new Vector3(-2.84f,.1f,-1.55f));
            var fridgeAim = new Vector3(-5.34f,1.08f,-1.45f);
            Need(story.FridgeFire.IsBurning && story.FridgeHeatIgnitions > fridgeIgnitionsOnReturn && story.FridgeHeatIgnitions >= 2,
                $"The unattended refrigerator ignites more adjacent floor cells ({story.FridgeHeatIgnitions})");
            yield return Record("04-blue-refrigerator");
            yield return HitFire(story.FridgeFire, fridgeAim, ElementKind.DryPowder, 330, () => !story.FridgeFire.IsBurning, "powder-fridge");
            Need(!story.FridgeFire.IsBurning && story.ComputerFire.IsBurning, "Baking soda extinguishes the refrigerator while the computer still burns");
            int fridgeIgnitionsAtExtinction = story.FridgeHeatIgnitions;
            yield return Record("04-refrigerator-out");
            yield return WalkSafe(new Vector3(-2.83f,.1f,-3.60f));
            yield return WalkSafe(new Vector3(-1.40f,.1f,-3.66f));
            Need(Mathf.Approximately(story.SofaFire.Intensity, story.SofaFire.InitialIntensity),
                "The sofa is at full intensity before the powder-only test");
            var farArm = new Vector3(sofa.max.x - .20f, sofa.center.y + .16f, sofa.center.z);
            yield return Record("sofa-before-powder");
            yield return HitFire(story.SofaFire, farArm, ElementKind.DryPowder, 360,
                () => !story.SofaFire.IsBurning, "powder-sofa", captureHud: true);
            Need(!story.SofaFire.IsBurning && hose.Source && hose.Source.RemainingAmount > 0d,
                "A sustained click on the far sofa arm puts out the full-strength sofa with powder and supply to spare");
            yield return Record("sofa-powder-out");
            float raisedHand = pose.BookHand.position.y;
            yield return HitFire(story.ManFire, AimAtMan(), ElementKind.DryPowder, 600, () => !story.ManFire.IsBurning, "man-powder");
            Need(!story.ManFire.IsBurning && story.CanUseSoda && story.Soda.isActiveAndEnabled,
                "Actual powder extinguishes the man and enables the bottle source");
            Need(raisedHand - pose.BookHand.position.y > .20f,
                $"The man lowers his raised hand after the fire goes out ({raisedHand - pose.BookHand.position.y:F2}m)");
            yield return Record("05-man-rescued");
            yield return VerifyResourceFocus("resource-ready", true);

            Step(interact: true);
            yield return WalkSafe(new Vector3(2.04f,.1f,-3.68f));
            yield return FinishReel();
            File.AppendAllText(Path.Combine(output, "soda-connection.txt"),
                $"Player {input.transform.position:F3}, bottle {story.Soda.transform.position:F3}, port {story.Soda.Port:F3}, " +
                $"inlet {hose.InletPosition:F3}, reachable {hose.CanConnect(story.Soda)}, " +
                $"inlet path blocked {hose.IsCableBlocked(story.Soda.Port,hose.InletPosition,.0475f,story.Soda)}, " +
                $"hose deployed {hose.Physical.HasDeployedMaterial}\n");
            Step(interact: true);
            Need(hose.Source == story.Soda, "E connects the hose to the bottle held by the seated man");
            Need(story.Soda.HasCapacity && story.Soda.RemainingAmount == story.Soda.Capacity && !story.Soda.CanRefill,
                "The soda starts with a full finite reserve");
            yield return Record("06-soda-and-request");
            yield return WalkSafe(new Vector3(5.15f,.1f,-3.76f));
            yield return WalkSafe(new Vector3(5.25f,.1f,.24f));
            yield return Axe(doors["door.002"]);
            Need(study.DoorwayBurning && !book.IsSafeToTake, "The open doorway is filled by electrical fire and the book is unavailable");
            for (int tick = 0; tick < 54; tick++)
            {
                Step(Vector2.up, new Vector3(5.25f, .1f, 1.62f));
                if (tick % 4 == 3) yield return null;
            }
            Need(input.transform.position.z < .75f && input.transform.position.z > .51f &&
                !contact.IsDead && hose.Source == story.Soda, "Actual forward input stops at the blue flames with the axe-broken door open");
            yield return Record("07-doorway-blocked");
            yield return Walk(new Vector3(5.22f, .1f, .20f));
            for (int index = study.Fires.Count - 1; index >= 0; index--)
            {
                var patch = study.Fires[index];
                if (!patch.IsBurning) continue;
                var aim = patch.GetComponent<BoxCollider>().bounds.center;
                yield return HitFire(patch, aim, ElementKind.CarbonDioxide, 225, () => !patch.IsBurning, $"study-co2-{index}");
                Need(!patch.IsBurning, "Actual CO2 extinguishes a spread patch from outside the study");
            }
            Need(story.ComputerFire.IsBurning && !book.IsSafeToTake, "The computer remains the ignition source after the doorway is clear");
            var cpuAim = story.ComputerFire.transform.position + Vector3.back * .26f + Vector3.up * .18f;
            float air = 0f;
            int lines = 0, gasHits = 0;
            spray.ParticleImpacted += GasContact;
            if (tools.SelectedTool != FirefighterTools.Tool.Hose)
            {
                Step(aim: cpuAim, pressed: true, held: true); Step();
            }
            for (int tick = 0; tick < 240 && story.ComputerFire.IsBurning && mission.AllowsInput; tick++)
            {
                Step(aim: cpuAim, pressed: tick == 0, held: true);
                air = Mathf.Max(air, story.ComputerFire.Airflow.magnitude);
                lines = Mathf.Max(lines, gasView.VisibleLines);
                if (tick % 4 == 3) { yield return null; if (captureEvidence) Capture($"07-gas-{tick/4:D3}"); }
            }
            spray.ParticleImpacted -= GasContact;
            Step(aim: cpuAim);
            Need(gasHits > 0 && lines > 0 && air > .02f && !story.ComputerFire.IsBurning && book.IsSafeToTake,
                $"Actual CO2 reaches the computer, bends its flames, draws streamlines and unlocks the book ({gasHits} hits)");
            Need(story.FridgeHeatIgnitions == fridgeIgnitionsAtExtinction &&
                !story.FridgeFire.IsBurning, "The refrigerator stops starting new floor fires once extinguished");
            Need(story.Soda.RemainingAmount < story.Soda.Capacity && story.Soda.RefilledAmount == 0d,
                "Fighting the computer fire reduces the bottle supply");
            yield return Record("08-computer-out");
            yield return Walk(new Vector3(5.24f, .1f, 1.62f));
            Need(input.transform.position.z > 1.5f && !study.IsBurning, "The same doorway can be crossed after the blue fires are extinguished");
            yield return WalkSafe(new Vector3(3.90f,.1f,3.13f));
            Need(carry.NearbyItem == book, "The wall-side book can be reached with the current deployed hose length");
            Step(interact: true);
            Need(carry.HeldItem == book && !hose.IsConnected && !hose.Physical.HasDeployedMaterial,
                "E picks up the model book by the wall and immediately stows the hose");
            yield return Record("09-book-in-hand");
            yield return WalkSafe(new Vector3(5.26f,.1f,1.48f));
            yield return WalkSafe(new Vector3(5.26f,.1f,.25f));
            yield return WalkSafe(new Vector3(1.77f,.1f,-.23f));
            yield return WalkSafe(new Vector3(1.25f,.1f,-1.25f));
            Need(carry.CanDeliver, "The held book can be handed to the man from the clear side of the sofa");
            yield return Record("09b-before-delivery");
            Step(interact: true);
            Need(book.IsDelivered && book.transform.parent == request.ReceivingHand && request.IsFulfilled &&
                mission.CurrentStatus == FirstReviewMission.Status.Won && mission.CompletionCount == 1,
                "Handing the book to the seated man immediately completes the level");
            Need(!story.SofaFire.IsBurning, "The sofa is still extinguished at handoff");
            Need(Math.Abs(water.BalanceError) < .002d, "Only delivered water is spent; powder and gas leave the water ledger intact");
            yield return Record("10-finished");

            Vector3 AimAtMan() => story.Man.TransformPoint(new Vector3(0,.77f,.33f));

            IEnumerator VerifyResourceFocus(string label, bool captureTransition)
            {
                if (captureEvidence) Capture(label + "-off");
                for (int tick = 0; tick < 25; tick++)
                {
                    art.AdvanceResourceFocus(true, Dt);
                    if (tick == 5)
                        Need(outlines.ResourceFocusAmount > .10f && outlines.ResourceFocusAmount < .30f,
                            "Holding focus eases gradually into the scene");
                    if (captureTransition && captureEvidence && tick % 2 == 1)
                    { yield return null; Capture($"{label}-in-{tick / 2:D3}"); }
                }
                Need(outlines.ResourceFocusAmount > .999f, "Holding focus reaches the full highlight");
                if (captureEvidence) Capture(label + "-held");

                for (int tick = 0; tick < 31; tick++)
                {
                    art.AdvanceResourceFocus(false, Dt);
                    if (tick == 8)
                        Need(outlines.ResourceFocusAmount > .65f && outlines.ResourceFocusAmount < .88f,
                            "Releasing focus fades the highlight out gradually");
                    if (captureTransition && captureEvidence && tick % 2 == 1)
                    { yield return null; Capture($"{label}-out-{tick / 2:D3}"); }
                }
                Need(outlines.ResourceFocusAmount == 0f, "Releasing focus restores the scene");
                if (captureEvidence) Capture(label + "-released");
            }

            void GasContact(SprayEmitter.ParticleImpact contact)
            {
                if (IsHit(contact, story.ComputerFire, ElementKind.CarbonDioxide)) gasHits++;
            }

            IEnumerator HitFire(ObjectFire target, Vector3 aim, ElementKind element, int ticks, Func<bool> until = null,
                string label = null, bool captureHud = false)
            {
                int hits = 0;
                spray.ParticleImpacted += Count;
                yield return SprayPath(aim, aim, ticks, until: until, captureLabel: label, captureHud: captureHud);
                spray.ParticleImpacted -= Count;
                Need(hits > 0, $"{element} has measured contacts on {target.name} ({hits})");
                void Count(SprayEmitter.ParticleImpact contact)
                {
                    if (IsHit(contact, target, element)) hits++;
                }
            }

            static bool IsHit(SprayEmitter.ParticleImpact impact, ObjectFire target, ElementKind element) =>
                impact.Element == element && impact.Collider && impact.Collider.GetComponentInParent<ObjectFire>() == target;

            IEnumerator FinishReel()
            {
                for (int i = 0; hose.IsReeling && i < 600; i++) { Step(); if (i % 6 == 5) yield return null; }
                Need(!hose.IsReeling, "The native hose is fully reeled before the next supply");
            }

            IEnumerator Record(string label)
            {
                if (!captureEvidence) yield break;
                water.RefreshView();
                yield return null;
                Capture(label);
                var man = story.Man;
                File.AppendAllText(Path.Combine(output, "man-pose.txt"), label + " " + string.Join(" | ",
                    man.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Hips" || t.name == "Head" ||
                        t.name.EndsWith("Arm", StringComparison.Ordinal) || t.name.EndsWith("arm", StringComparison.Ordinal) ||
                        t.name.EndsWith("Hand", StringComparison.Ordinal)).Select(t => $"{t.name}={man.InverseTransformPoint(t.position):F3}")) + "\n");
                if (!Application.isEditor && !Application.isBatchMode)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(output,label + "-hud.png"));
                    yield return null;
                    yield return null;
                }
            }
        }

    }
}
