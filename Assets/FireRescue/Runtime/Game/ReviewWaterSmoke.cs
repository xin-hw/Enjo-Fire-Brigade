using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // The same command-driven walkthrough runs in the Editor and packaged player.
    public sealed partial class ReviewWaterSmoke : MonoBehaviour
    {
        const float Dt = 1f / 60f;
        LocalPlayerInput input;
        FirefighterTools tools;
        HoseConnection hose;
        SprayEmitter spray;
        WaterLab water;
        FireField field;
        FirstReviewMission mission;
        PlayerFireContact contact;
        CookingOilIncident incident;
        ObjectFire pan;
        PlayerCarryInteraction carry;
        CarryItem pudding;
        RescueNpc resident;
        NpcItemRequest request;
        Camera view;
        string output;
        int frames;
        RenderTexture captureTarget;
        Texture2D captureImage;
        bool failed, simulateFire = true, captureEvidence;
        readonly List<double> waterCpu = new();
        int peakWaterDrops, peakWaterSplashes;
        float nativePhysicsClock;
        bool manualNativePhysics;
        bool nativePlayerLoop;
        bool loggedHoseFault;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string path = Environment.GetEnvironmentVariable("FIRERESCUE_WATER_SMOKE");
            if (!string.IsNullOrEmpty(path) && Environment.GetCommandLineArgs().Contains("--verify-water"))
                StartRun(path);
        }

        public static void StartRun(string path)
        {
            var run = new GameObject("Temporary water walkthrough").AddComponent<ReviewWaterSmoke>();
            run.output = path;
        }

        IEnumerator Start()
        {
            captureEvidence = (Application.isEditor || Environment.GetCommandLineArgs().Contains("--capture-evidence")) &&
                !Environment.GetCommandLineArgs().Contains("--no-capture");
            nativePlayerLoop=Environment.GetCommandLineArgs().Contains("--native-player-loop");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "results.txt"), $"Unity {Application.unityVersion}, game {Application.version}, {SystemInfo.graphicsDeviceType}\n");
            Application.logMessageReceived += OnLog;
            input = FindAnyObjectByType<LocalPlayerInput>();
            input.ReadDevices = false;
            input.SetInputFocus(true);
            tools = input.GetComponent<FirefighterTools>();
            hose = input.GetComponent<HoseConnection>();
            spray = input.GetComponent<SprayEmitter>(); spray.AutomaticSimulation = false;
            water = FindAnyObjectByType<WaterLab>(); water.AutomaticTick = false;
            field = FindAnyObjectByType<FireField>(); field.Simulate = false;
            mission = FindAnyObjectByType<FirstReviewMission>(); mission.AutomaticTick = false;
            incident = FindAnyObjectByType<CookingOilIncident>(); incident.AutomaticTick = false;
            pan = mission.OilFire;
            contact = input.GetComponent<PlayerFireContact>(); contact.AutomaticTick = false;
            carry = input.GetComponent<PlayerCarryInteraction>();
            pudding = FindAnyObjectByType<CarryItem>();
            resident = FindAnyObjectByType<RescueNpc>(); resident.Autonomous = false;
            request = mission.Request; request.AutomaticTick = false;
            view = Camera.main;
            Time.captureDeltaTime = 4f * Dt;
            yield return null;
            if(hose.Physical && !nativePlayerLoop)
            {
                manualNativePhysics=true;
                hose.Physical.AutomaticPhysics=false;
                input.GetComponent<PhysicalHoseTube>().AutomaticSimulation=false;
                Physics.simulationMode=SimulationMode.Script;
            }
            Need(hose.MaximumLength == 10 && mission.Remaining == 120 && !hose.IsConnected &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Real level loads at spawn with 10m hose and 120s deadline");
            string expectedVersion = Environment.GetEnvironmentVariable("FIRERESCUE_EXPECTED_VERSION");
            if (!Application.isEditor && !string.IsNullOrEmpty(expectedVersion))
                Need(Application.version == expectedVersion, $"Local build version is {expectedVersion}");
            var waterShader = water.GetComponentsInChildren<MeshRenderer>(true)
                .Select(renderer => renderer.sharedMaterial.shader).FirstOrDefault(shader => shader.name == "FireRescue/WaterLab");
            Need(waterShader && waterShader.isSupported, "Transparent water shader resolves on the graphics device");
            var doors = FindObjectsByType<BreakableDoor>().ToDictionary(door => door.name);
            if (Environment.GetCommandLineArgs().Contains("--verify-water-physics"))
            {
                yield return VerifyWaterPhysics(doors["door.003"]);
                Finish();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-water-art"))
            {
                yield return VerifyWaterArt(doors["door.003"]);
                Finish();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-counter"))
            {
                yield return VerifyCounter();
                Finish();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-hose"))
            {
                if(hose.Physical) yield return VerifyNativeHose(doors["door.003"]);
                else
                {
                    yield return VerifyHose(doors["door.003"]);
                    yield return VerifyHoseDoorEdges();
                }
                Finish();
                yield break;
            }
            yield return Walk(new Vector3(.45f, .1f, .18f));
            yield return Axe(doors["door.003"]);
            yield return Walk(new Vector3(.45f, .1f, 2.65f));
            Step(interact: true);
            Need(hose.IsConnected && hose.Source.Element == ElementKind.Water, "Connects to the real toilet with E");
            yield return Walk(new Vector3(.45f, .1f, .18f));
            yield return WetExitCorridor();
            yield return Walk(new Vector3(.6f, .1f, -3.42f));
            yield return SprayPath(new Vector3(.45f,.1f,-2.8f), new Vector3(.45f,.1f,.4f), 240);
            int before = field.BurningCount;
            Vector3 nearApproach = new(-.3f,.1f,-3.42f);
            yield return SprayPath(nearApproach,nearApproach,120);
            yield return SprayPath(nearApproach, new Vector3(-2.8f,.1f,-3.42f), 240);
            yield return Walk(new Vector3(-1.45f,.1f,-3.42f));
            yield return SprayPath(new Vector3(-2.4f,.1f,-3.43f), new Vector3(-4.65f,.1f,-3.43f), 162);
            Need(water.FloorVolume > 0d && water.Evaporated > 0d && pan.WaterApplied == 0f &&
                !pudding.IsBurnt && !contact.IsDead, $"Floor water leaves puddles and produces contact steam without hitting the pot (burning cells {before} to {field.BurningCount})");
            yield return Walk(new Vector3(-2.84f,.1f,-3.42f));
            yield return SprayPath(new Vector3(-2.84f,.1f,-2.92f), new Vector3(-2.84f,.1f,-1.5f), 126);
            yield return Walk(new Vector3(-2.84f,.1f,-2.0f));
            yield return Axe(doors["door.004"]);
            yield return SprayPath(new Vector3(-2.84f,.1f,-1.5f), new Vector3(-2.84f,.1f,-.15f), 180);
            Step(interact: true);
            yield return Walk(new Vector3(-2.84f,.1f,-1.55f));
            if (!doors["door.004"].IsBroken) yield return Axe(doors["door.004"]);
            yield return Walk(new Vector3(-2.84f,.1f,-.25f));
            yield return Walk(new Vector3(-5.4f,.1f,2.8f));
            yield return Walk(new Vector3(-5.4f,.1f,3.95f));
            float swapWait=0f;
            for(int i=0;hose.IsReeling && i<900 && mission.AllowsInput;i++)
            {
                Step(aim:new Vector3(-5.4f,.65f,4.25f));
                swapWait+=Dt;
                if(i%4==3) yield return null;
                if(i%120==119 && hose.IsReeling)
                {
                    var chain=hose.Physical.Chain;
                    File.AppendAllText(Path.Combine(output,"results.txt"),$"SWAP REEL t={swapWait:F2}s material={chain.MaterialLength:F3}m gap={chain.MaximumJointGap:F3}m grip={chain.GripError:F3}m force={chain.GripForce:F1}N free={chain.GetPoint(0):F3} end={chain.GetPoint(chain.NodeCount-1):F3} inlet={hose.InletPosition:F3}\n");
                    if(captureEvidence) Capture($"swap-wait-{i:D4}");
                }
            }
            Need(!hose.IsReeling,$"Water hose physically returns before connecting the powder source (wait on arrival {swapWait:F2}s)");
            Step(interact: true);
            Need(hose.IsConnected && hose.Source.Element == ElementKind.DryPowder, "Cleared route reaches the storage powder source");
            yield return Walk(new Vector3(-5.4f,.1f,2.8f));
            yield return Walk(new Vector3(-2.84f,.1f,-.25f));
            yield return Walk(new Vector3(-2.84f,.1f,-1.55f));
            yield return SprayPath(new Vector3(-3.4f,.1f,-1.9f), new Vector3(-4.1f,.1f,-2.2f), 258);
            yield return WalkSafe(new Vector3(-3.5f,.1f,-1.9f));
            yield return SprayPath(pan.transform.position, pan.transform.position, 330, until: () => !pan.IsBurning);
            Need(!pan.IsBurning, $"Actual powder extinguishes the oil pan (pan={pan.Intensity:F2})");
            yield return SprayPath(new Vector3(-3.46f,.1f,-3.6f), new Vector3(-4.25f,.1f,-3.9f), 240);
            yield return Walk(new Vector3(-3.52f,.1f,-2.62f));
            yield return SprayPath(new Vector3(-4.15f,.1f,-3.48f), new Vector3(-5.12f,.1f,-4.13f), 230);
            yield return Walk(new Vector3(-4.0f,.1f,-3.35f));
            yield return SprayPath(new Vector3(-5.25f,.1f,-3.70f), new Vector3(-5.20f,.1f,-4.35f), 210, until: () => pudding.IsSafeToTake);
            var nearFood = from z in Enumerable.Range(0, field.Height)
                           from x in Enumerable.Range(0, field.Width)
                           let cell = field.CellCenter(x, z)
                           let offset = cell - pudding.transform.position
                           where Mathf.Abs(offset.x) < 1f && Mathf.Abs(offset.z) < 1f && field.GetCell(x, z).IsBurning
                           select cell.ToString("F2");
            File.AppendAllText(Path.Combine(output, "results.txt"), "Active cells around pudding: " + string.Join(", ", nearFood) + "\n");
            Need(pudding.IsSafeToTake && !pudding.IsBurnt, $"Powder clears the floor around the original pudding at {pudding.transform.position:F2}");
            yield return SprayPath(new Vector3(-2.35f,.1f,-3.49f), new Vector3(-1.75f,.1f,-3.65f), 180);
            yield return Walk(new Vector3(-4.05f,.1f,-3.48f));
            yield return Walk(new Vector3(-1.65f,.1f,-3.46f), tolerance: .22f);
            yield return SprayPath(new Vector3(.5f,.1f,-3.95f), new Vector3(2.6f,.1f,-3.95f), 180);
            Step(interact: true);
            Need(!hose.IsConnected, "Disconnects powder before returning for pudding");
            yield return Walk(new Vector3(-4.1f,.1f,-3.4f));
            yield return Walk(new Vector3(-4.78f,.1f,-4.09f));
            Step(interact: true);
            Need(carry.IsCarrying, "Picks up the original pudding with E");
            yield return Walk(new Vector3(-4.05f,.1f,-3.48f));
            yield return Walk(new Vector3(-1.45f,.1f,-3.46f));
            yield return WalkSafe(new Vector3(5.28f,.1f,-.05f));
            if (!doors["door.002"].IsBroken)
            {
                Step(interact: true);
                yield return Axe(doors["door.002"]);
                Step(interact: true);
                Need(carry.IsCarrying, "Picks pudding up after axing the bedroom door");
            }
            yield return Walk(new Vector3(5.28f,.1f,1.65f));
            yield return Walk(new Vector3(4.15f,.1f,2.3f));
            Step(interact: true);
            Need(request.IsFulfilled && !request.IsPoisoned, $"Delivers edible pudding before deadline ({mission.Remaining:F1}s remaining)");
            if (!doors["door.001"].IsBroken)
            {
                yield return Walk(new Vector3(5.28f,.1f,1.65f));
                yield return WalkSafe(new Vector3(5.45f,.1f,-4.98f));
                yield return Axe(doors["door.001"]);
                yield return Walk(new Vector3(7.08f,.1f,-4.98f));
                yield return Walk(new Vector3(7.08f,.1f,-4.30f));
            }
            float evacuationTime = 0f;
            Time.captureDeltaTime = 2f * Dt;
            for (int i = 0; i < 1800 && mission.AllowsInput && !resident.IsRescued; i++)
            {
                Step();
                evacuationTime += Dt;
                if (i % 2 == 1)
                {
                    water.RefreshView();
                    yield return null;
                    if (captureEvidence) Capture($"rescue-{i/2:D3}");
                }
            }
            Step();
            Need(mission.CurrentStatus == FirstReviewMission.Status.Won && resident.IsRescued &&
                mission.CompletionCount == 1, $"Resident walks the prepared corridor within 30s, actual wait {evacuationTime:F1}s (NPC {resident.transform.position:F2}, route={resident.HasSafePath}, status={mission.CurrentStatus}/{mission.FailureCause})");
            Need(Math.Abs(water.BalanceError) < .002, $"Water ledger closes after full rescue (error {water.BalanceError:F7})");
            Finish();
        }

        IEnumerator WetExitCorridor()
        {
            Need(hose.IsConnected && hose.Source.Element == ElementKind.Water, "Prepares an actual wet escape corridor during the original water stage");
            yield return Walk(new Vector3(.6f,.1f,-.3f));
            Vector3 firstWet = new(1.45f,.1f,-.3f);
            yield return SprayPath(firstWet, firstWet, 120);
            yield return SprayPath(firstWet, new Vector3(5.25f,.1f,-.3f), 240);
            yield return WalkSafe(new Vector3(2.8f,.1f,-.3f));
            yield return SprayPath(new Vector3(5.25f,.1f,-.5f), new Vector3(5.25f,.1f,-4.6f), 420);
            Vector3 exitThreshold = new(5.85f,.1f,-4.95f);
            yield return SprayPath(exitThreshold,exitThreshold,180);
            yield return SprayPath(new Vector3(5.25f,.1f,-.3f), new Vector3(5.25f,.1f,.45f), 120);
            yield return WalkSafe(new Vector3(.6f,.1f,-.3f));
            Vector3 landing = new(.6f,.1f,-3.42f);
            yield return SprayPath(landing,landing,120);
        }

        IEnumerator VerifyWaterPhysics(BreakableDoor bathroom)
        {
            VerifySurfacePhysicsEdges();
            simulateFire = false;
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Axe(bathroom);
            yield return Walk(new Vector3(.45f,.1f,2.65f));
            Step(interact: true);
            Need(hose.IsConnected, "Physics probe connects the original toilet");
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.6f,.1f,-3.42f));
            yield return Walk(new Vector3(-2.84f,.1f,-3.42f));
            var wet = new Vector3(-1.85f,.1f,-3.22f);
            var steam = water.GetComponentInChildren<WaterSteamView>();
            yield return SprayPath(wet, wet, 120);
            for (int i=0;i<360;i++) { Step(); if (i%24==23) yield return null; }
            double coldVolume = water.FloorVolume;
            Need(coldVolume > 1d && steam.EmittedCount == 0 && water.Airborne < .00001d,
                "Cold standing water persists beyond the old five-second wetness timer without steam");
            double beforeWarm = water.Evaporated;
            for (int z=-1;z<=2;z++) for (int x=-1;x<=2;x++)
                field.IgniteFromHeat(wet+new Vector3(x,0,z)*field.CellSize,.04f);
            for (int i=0;i<60;i++) { Step(); if (i%4==3) yield return null; }
            Need(water.Evaporated-beforeWarm > .014d && steam.EmittedCount==0,
                "Multiple pre-burning heat cells spend water without accumulating visible white steam");
            coldVolume = water.FloorVolume;
            field.IgniteFromHeat(wet, .65f);
            field.TryGetCell(wet, out var cell);
            float initial = field.GetCell(cell.x,cell.y).Intensity;
            double vapor = water.Evaporated;
            for (int i=0;i<120;i++) { Step(); if (i%2==1) { yield return null; if (captureEvidence) Capture($"standing-{i/2:D3}"); } }
            float remaining = field.GetCell(cell.x,cell.y).Intensity;
            File.AppendAllText(Path.Combine(output,"results.txt"),
                $"Standing contact intensity {initial:F4}->{remaining:F4}, surface {coldVolume:F6}->{water.FloorVolume:F6}, vapor {vapor:F6}->{water.Evaporated:F6}\n");
            Need(remaining < initial && water.FloorVolume < coldVolume && water.Evaporated > vapor && steam.EmittedCount > 0,
                "Heat contacting standing water cools the real cell and spends surface volume as steam");
            Need(Math.Abs((coldVolume-water.FloorVolume)-(water.Evaporated-vapor))<.00001d && Math.Abs(water.BalanceError)<.002d,
                "Standing contact transfers surface water to evaporation exactly once");
        }

        IEnumerator VerifyWaterArt(BreakableDoor bathroom)
        {
            simulateFire = false;
            bool captureFrames = captureEvidence;
            Bounds doorway = bathroom.BlockingCollider.bounds;
            int beforeDoor = water.OpenFloorCellsIn(doorway);
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Axe(bathroom);
            Need(water.OpenFloorCellsIn(doorway) > beforeDoor, "The real broken doorway reopens floor water cells");
            yield return Walk(new Vector3(.45f,.1f,2.65f));
            Step(interact: true);
            Need(hose.IsConnected, "Visual sample connects the original toilet");
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.6f,.1f,-3.42f));
            yield return Walk(new Vector3(-2.84f,.1f,-3.42f));
            var steam = water.GetComponentInChildren<WaterSteamView>();
            Vector3 cold = new(-1.85f,.1f,-3.22f);
            Vector3 hot = new(-4.28f,.1f,-3.34f);
            yield return Record("01-cold", 149, (tick) =>
            {
                if (tick == 110) Need(water.Airborne > 0d, "Water is still airborne on release");
                Vector3 aim = Vector3.Lerp(cold, cold + Vector3.right * .72f, Mathf.InverseLerp(51, 98, tick));
                Step(aim: aim, pressed: tick == 15, held: tick >= 15 && tick < 110);
            }, holdAfter: 44);
            Need(water.FloorVolume > .5d && steam.EmittedCount == 0,
                $"Cold spray leaves a real puddle with no steam (floor {water.FloorVolume:F3}, steam {steam.EmittedCount})");

            foreach (var offset in new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0),
                new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1),
                new Vector2Int(-2, 1), new Vector2Int(-1, 2), new Vector2Int(0, 2) })
                field.Ignite(hot + new Vector3(offset.x, 0, offset.y) * field.CellSize, .65f);
            int originalFire = field.BurningCount;
            Need(originalFire >= 8, "Isolated ordinary fire uses the scene's real burning cells");
            yield return null;
            var fireBed = GameObject.Find("Ground Fire Glow").GetComponent<Renderer>();
            Need(fireBed.enabled, "The burning floor renderer is active before any water reaches it");
            if (captureFrames) Capture("fire-bed");

            yield return Record("02-fire", 260, (tick) =>
            {
                Vector3 aim = Vector3.Lerp(hot + Vector3.back * .10f, hot + Vector3.forward * .25f, Mathf.InverseLerp(25, 174, tick));
                Step(aim: aim, pressed: tick == 18, held: tick >= 18 && tick < 216);
            }, holdAfter: 32);
            Need(water.Evaporated > 0d && steam.EmittedCount > 0 && field.BurningCount == 0,
                "Direct contact produces steam and extinguishes the isolated floor fire");
            Need(!fireBed.enabled && water.FloorVolume > .5d, "Extinguished area shows the remaining water only");

            Vector3 top = new(-5.644f,.95f,-4.415f);
            yield return Walk(new Vector3(-3.19f,.1f,-4.35f), tolerance: .19f);
            Need(hose.IsConnected, "Counter is in reach with the unchanged hose");
            yield return Record("03-top", 185, tick =>
            {
                Vector3 aim = Vector3.Lerp(top, top + Vector3.back * .42f, Mathf.InverseLerp(75, 127, tick));
                Step(aim: aim, pressed: tick == 18, held: tick >= 18 && tick < 137);
            }, holdAfter: 32);
            Need(water.CounterVolume > .4d && pan.WaterApplied == 0f, "Aimed spray pools on the actual counter top");
            Need(water.FloorVolumeUnderCounter == 0d, "The true cabinet footprint stays dry after the counter sweep");
            Need(Math.Abs(water.BalanceError) < .002, $"Visual sequence water ledger closes ({water.BalanceError:F7})");

            IEnumerator Record(string label, int steps, Action<int> command, int holdAfter)
            {
                float previousCaptureTime = Time.captureDeltaTime;
                Time.captureDeltaTime = 2f * Dt;
                for (int i = 0; i < steps + holdAfter; i++)
                {
                    if (i < steps) command(i);
                    else Step();
                    if (i % 2 == 0) continue;
                    water.RefreshView();
                    yield return null;
                    if (captureFrames) Capture($"{label}-{i / 2:D3}");
                }
                Time.captureDeltaTime = previousCaptureTime;
            }
        }

        IEnumerator VerifyCounter()
        {
            var source = FindObjectsByType<FluidSource>().Single(item => item.Element == ElementKind.Water);
            var counter = GameObject.Find("kitchen counter").GetComponent<Collider>();
            bool connected = false;
            for (int i = 0; i < 16 && !connected; i++)
            {
                float angle = i * Mathf.PI / 8f;
                Place(source.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                connected = hose.TryConnect(source);
            }
            Need(connected, "Counter probe connects to the original toilet source");
            tools.EquipHose();
            float frontX = -5.644f, frontZ = -4.415f;
            bool topHit = counter.Raycast(new Ray(new Vector3(frontX, 2f, frontZ), Vector3.down), out var top, 2f);
            Need(topHit && top.normal.y > .7f, $"Real counter has a top at {top.point:F3}");
            Place(top.point + Vector3.right * 2.45f);
            int topContacts = 0;
            spray.ParticleImpacted += hit =>
            {
                if (hit.Collider == counter && hit.Normal.y > .7f && hit.Position.y > .8f) topContacts++;
            };
            yield return SprayPath(top.point, top.point, 100);
            double counterWater = water.CounterVolume;
            Need(topContacts > 0 && counterWater > 0d, $"First hit reaches counter top ({topContacts} contacts, volume {counterWater:F4})");
            var footprint = new Bounds(top.point, new Vector3(.32f, 2f, .32f));
            Need(water.FloorVolumeIn(footprint) == 0d, "Directly under the wetted top remains dry");
            Need(water.FloorVolumeUnderCounter == 0d, "Dry floor under the full cabinet mesh footprint");
            var panCollider = pan.GetComponent<Collider>();
            Vector3 pot = panCollider.bounds.center;
            Place(pot + Vector3.right * 2.85f);
            float oldIntensity = pan.Intensity;
            pot.y = panCollider.bounds.max.y;
            yield return SprayPath(pot, pot, 84, until: () => incident.ExplosionCount > 0);
            Need(pan.WaterApplied > 0f && pan.Intensity > oldIntensity && pan.IsBurning && incident.ExplosionCount == 1,
                "Real water reaches oil pan and triggers the existing wrong-agent reaction once");
            Need(Math.Abs(water.BalanceError) < .002, $"Counter probe water ledger closes (error {water.BalanceError:F7})");

            void Place(Vector3 stance)
            {
                stance.y = .12f;
                input.transform.position = stance;
                Physics.SyncTransforms();
                view.GetComponent<RescueCamera>().Initialize(input.transform);
            }
        }

        IEnumerator VerifyHose(BreakableDoor bathroom)
        {
            simulateFire = false;
            var tube = input.GetComponent<PhysicalHoseTube>();
            var motor = input.GetComponent<FirefighterMotor>();
            tube.AutomaticSimulation = false;
            float maximumLength = hose.MaximumLength;
            bool capture=captureEvidence;
            int frame=0, blockedSpans=0, inspectedFrames=0;
            var timings=new List<double>();
            double returnTravel=0d;
            // Independently query the rendered wall mesh as well as the existing
            // collision model. This temporary shape only exists in this test run.
            var model=GameObject.Find("First Review 1027/User House 1027/wall/Visual");
            var visibleWall=model.AddComponent<MeshCollider>();
            visibleWall.sharedMesh=model.GetComponent<MeshFilter>().sharedMesh;
            model.layer=30;
            Physics.SyncTransforms();
            int visualWallHits=0;
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Axe(bathroom);
            yield return Walk(new Vector3(.45f,.1f,2.65f));
            Step(interact: true);
            Need(hose.IsConnected && hose.BreakUnderTension, "Original toilet connects with tension break enabled");
            var source = hose.Source;
            double waterDrawnBefore = source.WaterDrawn;
            tube.TickPresentation(Dt);

            yield return Walk(new Vector3(.45f,.1f,.18f));
            tube.TickPresentation(Dt);
            CheckSpans();
            yield return Walk(new Vector3(.6f,.1f,-3.42f));
            tube.TickPresentation(Dt);
            CheckSpans();
            float shortLength = hose.UsedLength;
            for (int i=0;i<52;i++)
            {
                motor.Move(Vector3.back,Dt);
                if (i%4==3) yield return null;
            }
            Vector3 wallStop=input.transform.position;
            for (int i=0;i<45;i++)
            {
                motor.Move(Vector3.back,Dt);
                if (i%4==3) yield return null;
            }
            Need(shortLength<maximumLength-.5f && hose.IsConnected && !hose.IsPullingTaut && hose.BreakCount==0 &&
                hose.UsedLength<maximumLength-.3f && Vector3.Distance(wallStop,input.transform.position)<.06f,
                $"Pressing into a wall with slack cannot arm or break the hose (length {hose.UsedLength:F2}m, move {Vector3.Distance(wallStop,input.transform.position):F3}m)");
            yield return Walk(new Vector3(.60f,.1f,-3.42f));
            yield return Walk(new Vector3(-2.84f,.1f,-3.42f));
            Vector3 aim = new(-4.7f,.1f,-3.42f);
            tube.TickPresentation(Dt);
            Need(tube.GetComponentsInChildren<CapsuleCollider>().Length == 24 && hose.AutomaticBendCount>0,
                "24 physical spans are present on the wrapped hose");
            for (int i = 0; i < 140 && !hose.IsAtLengthLimit; i++)
            {
                Step(Vector2.left,aim,pressed:i==0,held:true);
                yield return ReviewFrame();
            }
            float shownLength = VisibleLength();
            Need(hose.IsAtLengthLimit && hose.BreakCount == 0 && Mathf.Abs(shownLength-hose.UsedLength)<.015f &&
                shownLength>=maximumLength-.02f,
                $"Visible hose is taut on its true route before it can tear (shown {shownLength:F3} m, route {hose.UsedLength:F3} m)");
            Vector3 blockedAt = input.transform.position;
            for (int i = 0; i < 16; i++)
            {
                Step(Vector2.left, aim, held: true);
                yield return ReviewFrame();
            }
            Need(hose.IsConnected && hose.IsPullingTaut && Vector3.Distance(blockedAt,input.transform.position)<.12f,
                "A short pull stops the player while the hose stays connected");
            for (int i = 0; i < 44; i++)
            {
                Step(aim: aim, held: true);
                yield return ReviewFrame();
            }
            Need(hose.IsConnected && !hose.IsPullingTaut, "Releasing movement resets the tension without a break");
            for (int i=0;i<20;i++)
            {
                Step(aim:aim+Vector3.forward*.8f,held:true);
                yield return ReviewFrame();
            }
            Vector3 aimDirection=Vector3.ProjectOnPlane(aim+Vector3.forward*.8f-input.transform.position,Vector3.up);
            Need(Vector3.Angle(input.transform.forward,aimDirection)<4f && hose.IsConnected && !hose.IsPullingTaut,
                "The player can aim the nozzle while the extended hose keeps its valid length");

            int heldTicks = 0;
            while (hose.IsConnected && heldTicks<66)
            {
                heldTicks++;
                Step(Vector2.left,aim,held:true);
                if (hose.IsConnected) yield return ReviewFrame();
            }
            Need(!hose.IsConnected && hose.BreakCount==1 && !spray.IsSpraying && heldTicks>=38 && tube.IsReeling,
                $"The source-side coupling releases after sustained pull ({heldTicks*Dt:F2}s)");
            Need(water.Airborne>0d && source.WaterDrawn>waterDrawnBefore,
                "Previously emitted water stays airborne as the reel starts");
            double waterDrawnAtBreak=source.WaterDrawn;
            float initialLength=VisibleLength();
            Vector3 looseEnd=tube.NodePosition(0);
            Vector3 previousFreeEnd=looseEnd;
            Need(Vector3.Distance(looseEnd,source.Port)<.035f,
                "The returning end starts at the disconnected source coupling");

            float earlyDistance=0f, middleDistance=0f, lateDistance=0f;
            float previousLength=initialLength;
            for (int i=1;i<=82;i++)
            {
                // Hold still while measuring the speed curve, then move and turn
                // so the final tail also has to follow a changing player anchor.
                Step(i>=57 && i<=74 ? Vector2.right : Vector2.zero,
                    i>=57 ? aim+Vector3.forward : aim);
                yield return ReviewFrame();
                if (i==15) earlyDistance=initialLength-VisibleLength();
                if (i>=30 && i<45) middleDistance+=previousLength-VisibleLength();
                if (i>=63 && i<78) lateDistance+=previousLength-VisibleLength();
                if (tube.IsVisible)
                {
                    NeedAnchor();
                    Vector3 free=tube.NodePosition(0);
                    returnTravel+=Vector3.Distance(free,previousFreeEnd);
                    previousFreeEnd=free;
                }
                previousLength=VisibleLength();
            }
            Need(earlyDistance>0f && middleDistance>earlyDistance*1.8f && middleDistance>lateDistance*1.8f,
                $"Retraction speeds up then settles (equal 0.25s spans {earlyDistance:F2}/{middleDistance:F2}/{lateDistance:F2} m)");
            Need(!tube.IsReeling && !tube.IsVisible && hose.BreakCount==1 && Vector3.Distance(blockedAt,input.transform.position)>.25f,
                "The hose reaches the moving player and the released player remains mobile");
            Need(returnTravel>initialLength*.87d, $"Free end is visible traversing the route before it reaches the player ({returnTravel:F2}m)");
            Need(source.WaterDrawn==waterDrawnAtBreak && water.FloorVolume>0d && Math.Abs(water.BalanceError)<.002,
                "Retraction draws no water and preserves the landing water ledger");
            Need(blockedSpans==0 && visualWallHits==0,
                "Every inspected live and retracting tube span clears both physical and rendered level geometry");
            timings.Sort();
            double p95=timings[Mathf.FloorToInt((timings.Count-1)*.95f)];
            File.AppendAllText(Path.Combine(output,"results.txt"),
                $"TUBE valid span sampling {inspectedFrames} frames; CPU tube update p95 {p95:F3}ms (excludes capture and geometry QA); visual mesh blocked spans {visualWallHits}\n");

            yield return Walk(new Vector3(-1.10f,.1f,-3.42f));
            yield return Walk(new Vector3(.60f,.1f,-3.42f));
            yield return Walk(new Vector3(.45f,.1f,.18f));
            yield return Walk(new Vector3(.45f,.1f,2.65f));
            Step(interact:true);
            tube.TickPresentation(Dt);
            Need(hose.Source==source && tube.IsVisible && !tube.IsReeling && !hose.IsPullingTaut,
                "The same source reconnects with a fresh contact path after retraction");

            IEnumerator ReviewFrame()
            {
                long start=System.Diagnostics.Stopwatch.GetTimestamp();
                tube.TickPresentation(Dt);
                timings.Add((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency);
                CheckSpans();
                if (frame++%2==0) yield break;
                water.RefreshView();
                Time.captureDeltaTime = 2f * Dt;
                yield return null;
                if (capture) Capture($"hose-{frame/2:D3}");
                Time.captureDeltaTime = 4f * Dt;
            }

            void CheckSpans()
            {
                if (!tube.IsVisible) return;
                inspectedFrames++;
                for (int i=1;i<tube.NodeCount;i++)
                {
                    Vector3 a=tube.NodePosition(i-1), b=tube.NodePosition(i);
                    if (tube.RenderedSpanIsBlocked(a,b,tube.RenderRadius-.004f))
                        blockedSpans++;
                    if (visibleWall && Physics.CheckCapsule(a,b,tube.RenderRadius-.004f,1<<30,QueryTriggerInteraction.Ignore))
                        visualWallHits++;
                }
            }

            void NeedAnchor()
            {
                if (Vector3.Distance(tube.NodePosition(tube.NodeCount-1),hose.InletPosition)>.085f)
                    throw new InvalidOperationException("Reel stopped tracking the player's hose inlet");
            }

            float VisibleLength()
            {
                if (!tube.IsVisible) return 0f;
                float length=0f;
                for (int i=1;i<tube.NodeCount;i++) length+=Vector3.Distance(tube.NodePosition(i-1),tube.NodePosition(i));
                return length;
            }
        }

        void Step(Vector2 movement = default, Vector3 aim = default, bool pressed = false, bool held = false,
            bool axePressed = false, bool axeHeld = false, bool interact = false)
        {
            if(nativePlayerLoop) SampleNativeMetrics(-1d);
            bool waterAim = held && spray.UsesSurfaceAim;
            if (waterAim)
                input.TickScreenPointerCommand(movement, view.WorldToScreenPoint(aim), interact, axePressed, axeHeld, pressed, held, Dt);
            else
                input.TickPointerCommand(movement, aim, interact, false, axePressed, axeHeld, pressed, held, Dt);
            if(manualNativePhysics)
            {
                nativePhysicsClock+=Dt;
                while(nativePhysicsClock+0.000001f>=Time.fixedDeltaTime)
                {
                    Physics.SyncTransforms();
                    long nativeBegan=System.Diagnostics.Stopwatch.GetTimestamp();
                    hose.Physical.PreparePhysics(Time.fixedDeltaTime);
                    Physics.Simulate(Time.fixedDeltaTime);
                    hose.Physical.AfterPhysics(Time.fixedDeltaTime);
                    if (!loggedHoseFault && hose.Physical.IsActive && (hose.Physical.Chain.MaximumJointGap>.12f || hose.Physical.Chain.GripError>.5f))
                    {
                        loggedHoseFault=true;
                        var chain=hose.Physical.Chain;
                        File.AppendAllText(Path.Combine(output,"results.txt"),$"NATIVE INSTABILITY t={120-mission.Remaining:F2} connected={hose.IsConnected} reel={hose.IsReeling} length={chain.MaterialLength:F3} gap={chain.MaximumJointGap:F3} error={chain.GripError:F3} sourceForce={chain.SourceTension:F1} handForce={chain.GripForce:F1} player={input.transform.position:F3} tip={chain.GetPoint(chain.NodeCount-1):F3} inlet={hose.InletPosition:F3} move={movement:F2}\n");
                    }
                    SampleNativeMetrics((System.Diagnostics.Stopwatch.GetTimestamp()-nativeBegan)*1000d/System.Diagnostics.Stopwatch.Frequency);
                    nativePhysicsClock-=Time.fixedDeltaTime;
                }
                input.GetComponent<PhysicalHoseTube>().TickPresentation(Dt);
            }
            spray.Advance(Dt);
            long waterStart = System.Diagnostics.Stopwatch.GetTimestamp();
            water.Simulate(Dt);
            waterCpu.Add((System.Diagnostics.Stopwatch.GetTimestamp()-waterStart)*1000d/System.Diagnostics.Stopwatch.Frequency);
            peakWaterDrops = Mathf.Max(peakWaterDrops,spray.ActiveParticleCount);
            peakWaterSplashes = Mathf.Max(peakWaterSplashes,water.ActiveSplashCount);
            if (simulateFire)
            {
                incident.Tick(Dt);
                field.Step(Dt);
            }
            contact.Evaluate();
            request.Tick(Dt);
            resident.Tick(Dt);
            mission.Tick(Dt);
            if(nativeChecking && nativeEvidenceStep++%4==0 && captureEvidence)
            {
                water.RefreshView();
                Capture($"sequence-{nativeEvidenceStep:D5}");
            }
        }

        IEnumerator Walk(Vector3 target, float tolerance = .085f)
        {
            for (int i = 0; i < 420 && mission.AllowsInput; i++)
            {
                Vector3 delta = target - input.transform.position;
                Vector2 planar = new(delta.x, delta.z);
                if (planar.magnitude < tolerance) break;
                Step(Vector2.ClampMagnitude(planar * 4f, 1f), target);
                if (nativePlayerLoop || i % 4 == 3) yield return null;
            }
            Vector3 error = target - input.transform.position;
            Need(new Vector2(error.x,error.z).magnitude < Mathf.Max(.15f, tolerance) && !contact.IsDead,
                $"Walk to {target:F2}, reached {input.transform.position:F2}, t={120-mission.Remaining:F1}, fire={field.BurningCount}, hose={hose.UsedLength:F2}, status={mission.CurrentStatus}/{mission.FailureCause}");
        }

        IEnumerator WalkSafe(Vector3 target)
        {
            Need(field.TryGetCell(input.transform.position, out var start) && field.TryGetCell(target, out _), "Route is inside the fire grid");
            field.TryGetCell(target, out var goal);
            var from = new System.Collections.Generic.Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new System.Collections.Generic.Queue<Vector2Int>();
            queue.Enqueue(start);
            var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            const int mask = 1 << 8;
            while (queue.Count > 0 && !from.ContainsKey(goal))
            {
                var cell = queue.Dequeue();
                Vector3 point = field.CellCenter(cell.x, cell.y);
                foreach (var direction in directions)
                {
                    var next = cell + direction;
                    if (next.x < 0 || next.y < 0 || next.x >= field.Width || next.y >= field.Height || from.ContainsKey(next)) continue;
                    Vector3 center = field.CellCenter(next.x, next.y);
                    if (Unsafe(center) ||
                        Physics.Linecast(point + Vector3.up * .4f, center + Vector3.up * .4f, mask, QueryTriggerInteraction.Ignore)) continue;
                    from[next] = cell;
                    queue.Enqueue(next);
                }
            }
            if (!from.ContainsKey(goal))
            {
                using var map = File.CreateText(Path.Combine(output, "route.txt"));
                map.WriteLine($"Origin {field.Origin:F2}; spacing {field.CellSize:F2}; start {start}; goal {goal}");
                for (int z = field.Height - 1; z >= 0; z--)
                {
                    for (int x = 0; x < field.Width; x++)
                    {
                        Vector2Int cell = new(x, z);
                        Vector3 point = field.CellCenter(x, z);
                        map.Write(cell == start ? 'S' : cell == goal ? 'G' : from.ContainsKey(cell) ? '+' :
                            Unsafe(point) ? (IsHot(point) ? 'F' : '#') : '.');
                    }
                    map.WriteLine();
                }
            }
            Need(from.ContainsKey(goal), $"Safe route through active fire to {target:F2} at t={120-mission.Remaining:F1}");
            var path = new System.Collections.Generic.List<Vector2Int>();
            for (var cell = goal; cell != start; cell = from[cell]) path.Add(cell);
            path.Reverse();
            for (int i = 0; i < path.Count; i++)
            {
                if (i + 1 < path.Count && i > 0 && path[i] - path[i - 1] == path[i + 1] - path[i]) continue;
                yield return Walk(field.CellCenter(path[i].x, path[i].y), tolerance: .07f);
            }
            yield return Walk(target);

            bool Unsafe(Vector3 point) => IsHot(point) ||
                Physics.CheckCapsule(point + Vector3.up * .28f, point + Vector3.up * 1.05f, .24f, mask, QueryTriggerInteraction.Ignore);
            bool IsHot(Vector3 point)
            {
                point.y = field.Origin.y + .16f;
                return field.IsBurningAt(point, .16f, fire =>
                {
                    fire.y = point.y;
                    return !Physics.Linecast(point, fire, mask, QueryTriggerInteraction.Ignore);
                });
            }
        }

        IEnumerator Axe(BreakableDoor door)
        {
            if (door.IsBroken) yield break;
            Vector3 center = door.BlockingCollider.bounds.center;
            Step(aim: center, axePressed: true, axeHeld: true);
            Step(aim: center);
            for (int i = 0; i < 123 && !door.IsBroken; i++)
            {
                Step(aim: center, axePressed: i == 0, axeHeld: true);
                if (nativePlayerLoop || i % 4 == 3) yield return null;
            }
            Step(aim: center);
            Need(door.IsBroken, $"Actual axe swing opens {door.name} (last hit {tools.LastHit.collider?.name ?? "none"})");
        }

        IEnumerator SprayPath(Vector3 from, Vector3 to, int ticks, Func<bool> until = null)
        {
            if (tools.SelectedTool != FirefighterTools.Tool.Hose)
            {
                Step(aim:from, pressed:true, held:true);
                Step(aim:from);
            }
            float emittedBefore = spray.TotalEmittedAmount;
            bool expectsEmission = ticks > 0 && !(until?.Invoke() ?? false);
            if (expectsEmission) Need(hose.IsConnected, "Requested spray has a connected physical supply");
            for (int i = 0; i < ticks && !(until?.Invoke() ?? false); i++)
            {
                Step(aim: Vector3.Lerp(from, to, i / (float)(ticks - 1)), pressed: i == 0, held: true);
                if (i % 4 != 3) continue;
                water.RefreshView();
                yield return null;
                if (i % 8 == 3 && frames < 180 && captureEvidence) Capture();
            }
            for (int i = 0; i < 44; i++)
            {
                Step(aim: to);
                if (i % 4 != 3) continue;
                water.RefreshView();
                yield return null;
                if (i % 8 == 3 && frames < 180 && captureEvidence) Capture();
            }
            if (expectsEmission) Need(spray.TotalEmittedAmount > emittedBefore, "Requested spray emits real packets");
        }

        void Capture(string name = null)
        {
            if (!captureTarget)
            {
                captureTarget = new RenderTexture(1024, 576, 24);
                captureImage = new Texture2D(1024, 576, TextureFormat.RGB24, false);
            }
            var previous = RenderTexture.active;
            view.targetTexture = captureTarget;
            view.Render();
            RenderTexture.active = captureTarget;
            captureImage.ReadPixels(new Rect(0, 0, 1024, 576), 0, 0);
            File.WriteAllBytes(Path.Combine(output, (name ?? $"frame{frames++:D3}") + ".png"), captureImage.EncodeToPNG());
            view.targetTexture = null;
            RenderTexture.active = previous;
        }

        void OnDestroy()
        {
            if (captureTarget) Destroy(captureTarget);
            if (captureImage) Destroy(captureImage);
        }

        void Need(bool condition, string message)
        {
            File.AppendAllText(Path.Combine(output, "results.txt"), (condition ? "PASS " : "FAIL ") + message + "\n");
            if (!condition) throw new InvalidOperationException(message);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            failed = true;
            File.AppendAllText(Path.Combine(output, "results.txt"), message + "\n" + stack + "\n");
            Finish();
        }

        void Finish()
        {
            Application.logMessageReceived -= OnLog;
            if (waterCpu.Count > 0)
            {
                waterCpu.Sort();
                File.AppendAllText(Path.Combine(output,"results.txt"),
                    $"WATER CPU Simulate p95 {waterCpu[Mathf.FloorToInt((waterCpu.Count-1)*.95f)]:F3}ms over {waterCpu.Count} steps, peaks {peakWaterDrops} drops/{peakWaterSplashes} splashes (excludes emitter, rendering, captures).\n");
            }
            File.AppendAllText(Path.Combine(output, "results.txt"), failed ? "REVIEW_WATER_FAIL\n" : "REVIEW_WATER_PASS\n");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(failed ? 1 : 0);
#else
            Application.Quit(failed ? 1 : 0);
#endif
        }
    }
}
