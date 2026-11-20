using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace FireRescue.Editor
{
    [InitializeOnLoad]
    public static class WaterLabValidation
    {
        const string Key = "FireRescue.WaterLabValidation";
        const string ModeKey = Key + ".Mode";
        internal enum Mode { Water, Width, Steam, Aim }

        static WaterLabValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                SessionState.SetBool(Key, false);
                var driver = new GameObject("Temporary water lab verification").AddComponent<WaterLabDriver>();
                driver.TestMode = (Mode)SessionState.GetInt(ModeKey, 0);
                SessionState.SetInt(ModeKey, 0);
            };
        }

        public static void BatchRun() => Begin(Mode.Water);
        public static void BatchCompareWidths() => Begin(Mode.Width);
        public static void BatchSteam() => Begin(Mode.Steam);
        public static void BatchAim() => Begin(Mode.Aim);

        static void Begin(Mode mode)
        {
            double ready = EditorApplication.timeSinceStartup + 3;
            void EnterLab()
            {
                if (EditorApplication.timeSinceStartup < ready) return;
                EditorApplication.update -= EnterLab;
                EditorSceneManager.OpenScene(mode == Mode.Steam ? WaterFireLabSetup.ScenePath : WaterLabSetup.ScenePath);
                SessionState.SetBool(Key, true);
                SessionState.SetInt(ModeKey, (int)mode);
                EditorApplication.EnterPlaymode();
            }
            EditorApplication.update += EnterLab;
        }
    }

    sealed class WaterLabDriver : MonoBehaviour
    {
        const string Evidence = "Logs/WaterLab";
        const float Dt = 1f / 60f;
        readonly List<string> lines = new();
        readonly List<double> cpuMs = new();
        LocalPlayerInput input;
        SprayEmitter emitter;
        WaterLab lab;
        Camera view;
        RenderTexture capture;
        Texture2D image;
        int frame, peakDrops, peakSplashes;
        double peakError;
        bool finished, failed;
        float began;
        string evidence, captureFolder;
        public WaterLabValidation.Mode TestMode { get; set; }

        struct WidthSample
        {
            public double Dose, Floor, Counter, Discharged;
            public int Droplets, Frames, WetCells;
            public float LateralRms;
            public SpraySettings Settings;
        }

        WidthSample widthSample;

        IEnumerator Start()
        {
            string output = Environment.GetEnvironmentVariable("FIRERESCUE_WATER_LAB_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Evidence;
            evidence = TestMode switch
            {
                WaterLabValidation.Mode.Width => Path.Combine(output, "width-comparison"),
                WaterLabValidation.Mode.Steam => Path.Combine(output, "steam"),
                WaterLabValidation.Mode.Aim => Path.Combine(output, "aim"),
                _ => output
            };
            captureFolder = evidence;
            Directory.CreateDirectory(evidence);
            DontDestroyOnLoad(gameObject);
            began = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 2f * Dt;
            yield return null;
            Bind();
            Check(view.orthographic && Mathf.Abs(view.orthographicSize - 6f) < .001f, "Uses the normal first-level orthographic distance");
            Check(emitter.Connection.IsConnected && lab.SourceRemaining == 120, "Lab connects the real toilet and starts with a fresh budget");
            capture = new RenderTexture(1280, 720, 24);
            image = new Texture2D(capture.width, capture.height, TextureFormat.RGB24, false);
            if (TestMode == WaterLabValidation.Mode.Width)
            {
                yield return Compare();
                Finish(!failed);
                yield break;
            }
            if (TestMode == WaterLabValidation.Mode.Steam)
            {
                yield return RecordSteam();
                Finish(!failed);
                yield break;
            }
            if (TestMode == WaterLabValidation.Mode.Aim)
            {
                yield return RecordAim();
                Finish(!failed);
                yield break;
            }
            var divider = GameObject.Find("Divider wall").GetComponent<Collider>();
            var counter = GameObject.Find("Counter receiving surface").GetComponent<Collider>();
            var shadow = new Bounds(new Vector3(3.08f, .1f, 0f), new Vector3(1.9f, 1f, 6.4f));
            Vector3 near = new Vector3(-.55f, .1f, -1.22f);
            Vector3 far = new Vector3(-.55f, .1f, .78f);
            Vector3 wallFoot = new Vector3(divider.bounds.min.x - .16f, .1f, -.65f);
            Vector3 wallFace = new Vector3(divider.bounds.min.x, .8f, -.65f);
            Vector3 top = new Vector3(counter.bounds.center.x, counter.bounds.max.y, counter.bounds.center.z);
            Capture();
            yield return Shot(near, near, 60, true);
            double beforeRelease = lab.FloorVolume;
            Check(lab.Airborne > 0, "Water is still airborne before releasing");
            yield return Shot(near, near, 42, false);
            Check(lab.Airborne < .00001 && lab.FloorVolume > beforeRelease, "Release drains in-flight water into the floor");
            int initialWetCells = lab.FloorWetCells;
            yield return Shot(far, far, 66, true);
            yield return Shot(far, far, 42, false);
            int separated = lab.FloorWetRegions;
            Check(separated >= 2, $"Two separated floor pools are visible (regions={separated})");
            Check(lab.FloorWetCells > initialWetCells, "Additional water increases the wet area");
            yield return Shot(far, near, 72, true);
            yield return Shot(near, far, 72, true);
            yield return Shot(far, far, 42, false);
            int joined = lab.FloorWetRegions;
            Check(joined < separated, $"Sweeping joins the floor pools (regions={joined})");
            yield return Shot(wallFoot, wallFoot, 72, true);
            // A point command gives the full-height wall face an unambiguous sighting
            // reference. Ballistic collision still takes place against the real BoxCollider.
            yield return Shot(wallFace, wallFace, 42, true, surfacePoint: true);
            yield return Shot(wallFoot, wallFoot, 54, false);
            var beforeTop = lab.CounterVolume;
            yield return Shot(top, top, 88, true);
            yield return Shot(top, top, 66, false);
            Check(lab.CounterVolume > beforeTop + .10, $"Tabletop receives its own water (volume={lab.CounterVolume:F4})");
            Check(lab.FloorVolumeIn(counter.bounds) < .00001, "No water accumulates under the cabinet footprint");
            Check(lab.FloorVolumeIn(shadow) < .00001, "The divider keeps the opposite floor dry");
            double settled = lab.FloorVolume + lab.CounterVolume;
            yield return Shot(top, top, 120, false, record: false);
            Capture();
            Check(Math.Abs(settled - lab.FloorVolume - lab.CounterVolume) < .00001, "Settled volume persists after a further two seconds");
            Check(peakDrops <= SprayEmitter.ParticleCapacity && peakSplashes <= 128, $"Particles stay bounded ({peakDrops} primary, {peakSplashes} secondary)");
            lines.Add($"CAPTURE frames={frame} fps=30, original scene camera distance and lighting");
            lines.Add($"LEDGER source={lab.SourceRemaining:F5} airborne={lab.Airborne:F5} floor={lab.FloorVolume:F5} counter={lab.CounterVolume:F5} discharged={lab.Discharged:F5}");

            yield return Reload();
            var first = RunAtRate(30);
            yield return Reload();
            var second = RunAtRate(120);
            Check(Math.Abs(first.used - second.used) < .001 && Math.Abs(first.landed - second.landed) < .05,
                $"30/120 Hz conserve the same dose (used {first.used:F5}/{second.used:F5}, landed {first.landed:F5}/{second.landed:F5})");
            Check(peakError < .001, $"Ledger closes throughout (max absolute error={peakError:F7})");
            cpuMs.Sort();
            lines.Add($"CPU step + water mesh/texture updates (excludes render and capture): p95 {cpuMs[(int)(cpuMs.Count * .95)]:F3} ms, max {cpuMs[^1]:F3} ms");
            Finish(!failed);
        }

        IEnumerator RecordAim()
        {
            var floor = GameObject.Find("Floor receiving surface").GetComponent<Collider>();
            var counter = GameObject.Find("Counter receiving surface").GetComponent<Collider>();
            var divider = GameObject.Find("Divider wall").GetComponent<Collider>();
            Vector3 first = new(-.95f, .1f, -1.38f);
            Vector3 second = new(.45f, .1f, .34f);
            Vector3 top = new(counter.bounds.center.x, counter.bounds.max.y, counter.bounds.center.z);
            var contacts = new List<(int tick, SprayEmitter.ParticleImpact impact)>();
            var cursors = new List<string> { "frame,x,y" };
            int tick = 0;
            Vector3 initial = emitter.NozzlePosition;
            void OnImpact(SprayEmitter.ParticleImpact impact) => contacts.Add((tick, impact));
            emitter.ParticleImpacted += OnImpact;

            for (tick = 0; tick < 284; tick++)
            {
                Vector3 target = tick < 36 ? first : tick < 140 ? second : top;
                bool hold = (tick >= 30 && tick < 81) || (tick >= 158 && tick < 205);
                Step(target, tick == 30 || tick == 158, hold, Dt);
                if (tick == 30)
                {
                    Check(emitter.ActiveParticleCount > 0 && contacts.Count == 0 && lab.FloorVolume == 0d &&
                        Vector3.Distance(emitter.GetParticle(0).Position, emitter.NozzlePosition) < .20f,
                        "Right press immediately launches from the nozzle; no instant target wetness");
                }
                if (tick == 36)
                    Check(contacts.Count == 0, "Original water is still airborne when the cursor jumps");
                if (tick == 81)
                    Check(!emitter.IsSpraying && lab.Airborne > 0d, "Release stops new emission while drops are in flight");
                if (tick % 2 == 0)
                {
                    yield return null;
                    view.targetTexture = capture;
                    Vector3 cursor = view.WorldToScreenPoint(target);
                    view.targetTexture = null;
                    cursors.Add(FormattableString.Invariant($"{frame},{cursor.x:F1},{capture.height - cursor.y:F1}"));
                    Capture();
                }
            }
            Check(contacts.Any(x => x.tick > 81 && x.impact.Collider == floor),
                "Water reaches the floor after the button was released");
            CheckArrival(first, floor, 30, 36, 57, "First click after redirect");
            CheckArrival(second, floor, 36, 46, 81, "New cursor position");
            CheckArrival(top, counter, 158, 173, 205, "Raised tabletop");
            Check(lab.CounterVolume > .1 && lab.FloorVolumeIn(counter.bounds) < .00001 && lab.Airborne < .00001,
                "Raised target receives real water without a floor deposit beneath it");
            Check(Vector3.Distance(initial, emitter.NozzlePosition) > .1f,
                "Nozzle changed heading between the floor and raised target");
            Check(peakError < .001, $"Water account still balances ({peakError:F7})");
            File.WriteAllLines(Path.Combine(evidence, "cursor.csv"), cursors);

            // Close enough to reach this point with the same bounded launch. The
            // divider must intercept the trajectory, including when manually aimed beyond it.
            var hidden = new Vector3(2.6f, .1f, -.2f);
            yield return WalkTo(new Vector3(-.78f, .125f, -.28f), hidden, record: false);
            contacts.Clear();
            yield return Shot(hidden, hidden, 40, true, surfacePoint: true, record: false);
            yield return Shot(hidden, hidden, 68, false, record: false);
            emitter.ParticleImpacted -= OnImpact;
            Check(contacts.Any(x => x.impact.Collider == divider) &&
                contacts.All(x => x.impact.Position.x < divider.bounds.max.x),
                "Wall physically intercepts a reachable target behind it");
            lines.Add($"CAPTURE frames={frame}; source dose={120d - lab.SourceRemaining:F5}; evaporated={lab.Evaporated:F5}");

            void CheckArrival(Vector3 target, Collider expected, int fired, int minTick, int maxTick, string label)
            {
                var hit = contacts.FirstOrDefault(x => x.tick <= maxTick && x.impact.Collider == expected &&
                    Vector3.Distance(x.impact.Position, target) < .075f);
                Check(hit.impact.Collider && hit.tick >= minTick,
                    $"{label} arrives at its selected surface after {(hit.tick - fired) * Dt:F3} s " +
                    $"(miss {Vector3.Distance(hit.impact.Position, target):F3} m)");
            }
        }

        IEnumerator RecordSteam()
        {
            var field = FindAnyObjectByType<FireField>();
            field.Simulate = false; // isolate suppression from natural fire decay for the comparison
            var steam = FindAnyObjectByType<WaterSteamView>();
            var pan = FindObjectsByType<ObjectFire>().Single(fire => fire.Kind == FireKind.CookingOil);
            var incident = pan.GetComponent<CookingOilIncident>();
            var floorFireView = FindAnyObjectByType<FirstRescueEffects>();
            Vector3 small = WaterFireLabSetup.SmallFire, large = WaterFireLabSetup.LargeFire;
            Vector3 cold = new(-.32f, .1f, -2.17f);
            var potBounds = pan.GetComponent<Collider>().bounds;
            Vector3 oil = new(potBounds.center.x, potBounds.max.y, potBounds.center.z);
            float beforeSmall = Intensity(small);
            float beforeLarge = Intensity(large);
            Check(beforeSmall > 0f && beforeLarge > beforeSmall && floorFireView.ActiveFireAtmosphereCount > 0,
                "Small and large fires and their combustion atmosphere exist before water");
            Capture();
            yield return Shot(cold, cold, 42, true);
            yield return Shot(cold, cold, 42, false);
            Check(steam.EmittedCount == 0 && lab.Evaporated == 0 && lab.FloorVolume > 0 &&
                Intensity(small) == beforeSmall && Intensity(large) == beforeLarge,
                "Cold floor wets without steam or changing nearby fires");

            yield return Shot(small, small, 29, true);
            Check(steam.EmittedCount > 0 && Intensity(small) > 0f && Intensity(small) < beforeSmall &&
                Mathf.Abs(steam.LastOrigin.y - .1f) < .015f &&
                field.TryGetCell(steam.LastOrigin, out var steamCell) && field.TryGetCell(small, out var smallCell) && steamCell == smallCell,
                $"Small fire weakens during contact; steam comes from the contacted floor cell (remaining {Intensity(small):F3})");
            yield return Shot(small, small, 91, true);
            yield return Shot(small, small, 55, false);
            Check(Intensity(small) == 0f && steam.ActiveCount == 0 && lab.FloorVolumeIn(new Bounds(small, Vector3.one * .4f)) > 0,
                "Small fire is out and steam clears, leaving water on its floor cell");

            int beforeBigSteam = steam.EmittedCount;
            yield return Shot(large, large, 38, true);
            Check(steam.EmittedCount > beforeBigSteam && Intensity(large) > 0f && Intensity(large) < beforeLarge,
                $"Large fire weakens over time (remaining {Intensity(large):F3})");
            yield return Shot(large, large, 152, true);
            yield return Shot(large, large, 62, false);
            Check(Intensity(large) == 0f && steam.ActiveCount == 0 && lab.Evaporated > 0 && lab.FloorVolume > 0,
                $"Large fire is out; steam disperses and surface water remains (evaporated {lab.Evaporated:F4})");

            var hidden = WaterFireLabSetup.WallBack;
            int beforeOilSteam = steam.EmittedCount;

            float beforePan = pan.Intensity;
            yield return Shot(oil, oil, 56, true);
            yield return Shot(oil, oil, 63, false);
            Check(pan.WaterApplied > 0f && pan.IsBurning && pan.Intensity >= beforePan &&
                incident.ExplosionCount == 1 && steam.EmittedCount == beforeOilSteam,
                $"Oil-pan water contact triggers one existing flare and no ordinary extinguishing steam (dose {pan.WaterApplied:F3})");
            double sourceLeft = lab.SourceRemaining;
            var powder = FindObjectsByType<FluidSource>().Single(item => item.Element == ElementKind.DryPowder);
            emitter.Connection.Disconnect();
            Check(emitter.Connection.TryConnect(powder), "Can switch to the existing dry-powder source");
            yield return WalkForPowder(new Vector3(-.66f, .1f, .84f), oil);
            yield return Shot(oil, oil, 450, true, surfacePoint: true);
            yield return Shot(oil, oil, 61, false);
            // New heat must spend standing water instead of reusing a timed spray dose.
            double puddleBefore = lab.FloorVolume, evaporatedBefore = lab.Evaporated;
            field.Ignite(small, .25f);
            yield return Shot(small, small, 24, false, record: false);
            Check(Intensity(small) < .25f && lab.Evaporated > evaporatedBefore &&
                Math.Abs((puddleBefore-lab.FloorVolume)-(lab.Evaporated-evaporatedBefore)) < .0001d,
                "Standing water cools new heat and transfers exactly its boiled volume to evaporation");
            Vector3 powderFire = new(-3f, .1f, -.2f);
            field.TryGetCell(powderFire,out var powderCell);
            powderFire = field.CellCenter(powderCell.x,powderCell.y);
            Check(lab.FloorVolumeIn(new Bounds(powderFire, Vector3.one * field.CellSize)) < .00001d,
                $"The entire ordinary powder target cell starts on dry floor at {powderFire:F2}");
            field.Ignite(powderFire, .25f);
            int powderSteam = steam.EmittedCount;
            double powderEvaporated = lab.Evaporated;
            yield return Shot(powderFire, powderFire, 130, true, surfacePoint: true, record: false);
            yield return Shot(powderFire, powderFire, 72, false, record: false);
            Check(!pan.IsBurning && Intensity(powderFire) <= .05f && steam.EmittedCount == powderSteam,
                $"Real dry-powder packets extinguish oil and ordinary fire without steam (pan={pan.Intensity:F3}, floor={Intensity(powderFire):F3})");
            Check(lab.SourceRemaining == sourceLeft && lab.Evaporated == powderEvaporated && peakError < .001,
                $"Powder does not change the water account (max absolute error {peakError:F7})");
            lines.Add($"CAPTURE frames={frame}; steam puffs={steam.EmittedCount}; evaporated={lab.Evaporated:F5}; floor={lab.FloorVolume:F5}; discharged={lab.Discharged:F5}");

            var divider = GameObject.Find("Divider wall").GetComponent<Collider>();
            var toilet = FindObjectsByType<FluidSource>().Single(item => item.Element == ElementKind.Water);
            field.Ignite(hidden, .7f);
            emitter.Connection.Disconnect();
            Vector3 stance = new(-.78f, .125f, -.28f);
            var playerCollider = input.GetComponent<CharacterController>();
            var spawn = new Vector3(-2.56f, .125f, -.25f);
            playerCollider.enabled = false;
            input.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(Vector3.right));
            playerCollider.enabled = true;
            Physics.SyncTransforms();
            Check(emitter.Connection.TryConnect(toilet), "Reconnects to the water source for the divider check");
            yield return WalkTo(stance, hidden, record: false);
            int wallHits = 0;
            void TrackWall(SprayEmitter.ParticleImpact impact)
            {
                if (impact.Collider == divider && impact.Element == ElementKind.Water) wallHits++;
            }
            emitter.ParticleImpacted += TrackWall;
            yield return Shot(hidden, hidden, 66, true, surfacePoint: true, record: false);
            yield return Shot(hidden, hidden, 60, false, record: false);
            emitter.ParticleImpacted -= TrackWall;
            Check(wallHits > 0 && Intensity(hidden) == .7f && steam.EmittedCount == powderSteam && peakError < .001,
                $"Divider physically intercepts water while far-side fire stays at full strength ({wallHits} wall hits)");

            yield return Reload(withFire:true);
            var slow = HeatAtRate(30);
            yield return Reload(withFire:true);
            var fast = HeatAtRate(120);
            Check(Math.Abs(slow.Drawn-fast.Drawn)<.001 && Math.Abs(slow.Vapor-fast.Vapor)<.002 &&
                Math.Abs(slow.Surface-fast.Surface)<.002 && slow.Heat<=.05f && fast.Heat<=.05f && peakError<.001,
                $"30/120Hz water-fire coupling agrees (draw {slow.Drawn:F5}/{fast.Drawn:F5}, vapor {slow.Vapor:F5}/{fast.Vapor:F5}, surface {slow.Surface:F5}/{fast.Surface:F5})");

            float Intensity(Vector3 position) => field.TryGetCell(position, out var cell) ? field.GetCell(cell.x, cell.y).Intensity : -1f;
        }

        IEnumerator WalkForPowder(Vector3 stance, Vector3 aim)
        {
            yield return WalkTo(stance, aim, record: true);
            Vector3 reached = stance - input.transform.position;
            Check(new Vector2(reached.x, reached.z).magnitude < .18f, "Player walks into the shorter powder range");
        }

        IEnumerator WalkTo(Vector3 stance, Vector3 aim, bool record)
        {
            for (int i = 0; i < 90; i++)
            {
                Vector3 delta = stance - input.transform.position;
                Vector2 planar = new(delta.x, delta.z);
                if (planar.magnitude < .07f) break;
                Step(aim, false, false, Dt, movement: Vector2.ClampMagnitude(planar * 3f, 1f));
                if (record && i % 2 == 1)
                {
                    yield return null;
                    Capture();
                }
            }
            yield return Shot(aim, aim, 12, false, record: record);
        }

        IEnumerator Compare()
        {
            yield return RecordWidth(WaterLab.SprayWidth.Concentrated);
            var a = widthSample;
            yield return Reload();
            yield return RecordWidth(WaterLab.SprayWidth.Wide);
            var b = widthSample;

            var expected = a.Settings;
            expected.SpreadAngle = b.Settings.SpreadAngle;
            Check(a.Settings.SpreadAngle == 18f && b.Settings.SpreadAngle == 34f && expected.Equals(b.Settings),
                "Profiles differ only in spread angle");
            Check(Math.Abs(a.Dose - 9d) < .001 && Math.Abs(a.Dose - b.Dose) < .00001 &&
                a.Droplets == b.Droplets && a.Frames == b.Frames,
                $"Equal dose ({a.Dose:F5}/{b.Dose:F5}), primary packets ({a.Droplets}/{b.Droplets}) and frames ({a.Frames}/{b.Frames})");
            Check(b.LateralRms > a.LateralRms * 1.5f,
                $"Wide cone fans out in actual flight ({a.LateralRms:F3}/{b.LateralRms:F3} m lateral RMS)");
            Check(peakError < .001, $"Ledger closes throughout (max absolute error={peakError:F7})");
        }

        IEnumerator RecordWidth(WaterLab.SprayWidth width)
        {
            lab.SetSprayWidth(width);
            captureFolder = Path.Combine(evidence, width.ToString());
            Directory.CreateDirectory(captureFolder);
            frame = 0;
            var source = emitter.Connection.Source;
            var counter = GameObject.Find("Counter receiving surface").GetComponent<Collider>();
            var farSide = new Bounds(new Vector3(3.08f, .1f, 0f), new Vector3(1.9f, 1f, 6.4f));
            Vector3 near = new Vector3(.10f, .1f, -1.22f);
            Vector3 far = new Vector3(.10f, .1f, .78f);
            Vector3 top = new Vector3(counter.bounds.center.x, counter.bounds.max.y, counter.bounds.center.z);
            Capture();
            yield return Shot(near, near, 18, false);
            yield return Shot(near, near, 42, true);
            float lateralRms = FlightWidth();
            yield return Shot(near, far, 64, true);
            yield return Shot(far, far, 24, true);
            yield return Shot(far, far, 48, false);
            yield return Shot(top, top, 50, true);
            yield return Shot(top, top, 84, false);
            Check(lab.Airborne < .00001 && lab.CounterVolume > 0 && lab.FloorVolumeIn(farSide) < .00001 &&
                lab.FloorVolumeIn(counter.bounds) < .00001,
                width + " settles on the two actual surfaces and leaves the blocked areas dry");
            widthSample = new WidthSample
            {
                Dose = 120 - lab.SourceRemaining, Droplets = emitter.TotalEmitted, Frames = frame,
                Floor = lab.FloorVolume, Counter = lab.CounterVolume, Discharged = lab.Discharged,
                LateralRms = lateralRms, WetCells = lab.FloorWetCells, Settings = source.SpraySettings
            };
            lines.Add($"{width}: angle={widthSample.Settings.SpreadAngle:F0} dose={widthSample.Dose:F5} packets={widthSample.Droplets} " +
                $"floor={widthSample.Floor:F4} tabletop={widthSample.Counter:F4} discharge={widthSample.Discharged:F4} " +
                $"wet floor cells={widthSample.WetCells} lateral rms={widthSample.LateralRms:F3} m");
        }

        float FlightWidth()
        {
            Vector3 forward = input.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 nozzle = emitter.NozzlePosition;
            float squared = 0f;
            int count = 0;
            for (int i = 0; i < emitter.ActiveParticleCount; i++)
            {
                Vector3 offset = emitter.GetParticle(i).Position - nozzle;
                float distance = Vector3.Dot(offset, forward);
                if (distance < 1.1f || distance > 2.1f) continue;
                float lateral = Vector3.Dot(offset, side);
                squared += lateral * lateral;
                count++;
            }
            return count == 0 ? 0f : Mathf.Sqrt(squared / count);
        }

        void Bind()
        {
            lab = FindAnyObjectByType<WaterLab>();
            lab.AutomaticTick = false;
            input = FindAnyObjectByType<LocalPlayerInput>();
            input.ReadDevices = false;
            input.SetInputFocus(true);
            emitter = input.GetComponent<SprayEmitter>();
            emitter.AutomaticSimulation = false;
            view = Camera.main;
        }

        IEnumerator Shot(Vector3 from, Vector3 to, int steps, bool hold, bool surfacePoint = false, bool record = true)
        {
            for (int i = 0; i < steps; i++)
            {
                Vector3 target = Vector3.Lerp(from, to, i / (float)Mathf.Max(1, steps - 1));
                Step(target, i == 0 && hold, hold, Dt, surfacePoint);
                if (record && i % 2 == 1)
                {
                    yield return null;
                    Capture();
                }
            }
        }

        void Step(Vector3 target, bool pressed, bool hold, float dt, bool surfacePoint = false, Vector2 movement = default)
        {
            long started = Stopwatch.GetTimestamp();
            if (surfacePoint)
                input.TickPointerCommand(movement, target, false, false, false, false, pressed, hold, dt, true);
            else
                input.TickScreenPointerCommand(movement, view.WorldToScreenPoint(target), false, false, false, pressed, hold, dt);
            emitter.Advance(dt);
            lab.Simulate(dt);
            lab.RefreshView();
            cpuMs.Add((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
            peakDrops = Mathf.Max(peakDrops, emitter.ActiveParticleCount);
            peakSplashes = Mathf.Max(peakSplashes, lab.ActiveSplashCount);
            peakError = Math.Max(peakError, Math.Abs(lab.BalanceError));
        }

        IEnumerator Reload(bool withFire=false)
        {
            EditorSceneManager.LoadSceneInPlayMode(withFire ? WaterFireLabSetup.ScenePath : WaterLabSetup.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            Bind();
            Check(lab.SourceRemaining == 120 && lab.FloorVolume + lab.CounterVolume + lab.Airborne == 0,
                "Reopening the lab resets all water state");
        }

        (double Drawn,double Vapor,double Surface,float Heat) HeatAtRate(int rate)
        {
            var fire = FindAnyObjectByType<FireField>();
            fire.Simulate = false;
            Vector3 target = WaterFireLabSetup.LargeFire;
            for (int i=0;i<rate*2;i++) Step(target,i==0,true,1f/rate);
            for (int i=0;i<rate*2;i++) Step(target,false,false,1f/rate);
            fire.TryGetCell(target,out var cell);
            return (120d-lab.SourceRemaining,lab.Evaporated,lab.FloorVolume+lab.CounterVolume,fire.GetCell(cell.x,cell.y).Intensity);
        }

        (double used, double landed) RunAtRate(int rate)
        {
            Vector3 target = new Vector3(-.55f, .1f, -.35f);
            for (int i = 0; i < rate * 2; i++) Step(target, i == 0, true, 1f / rate);
            bool disconnect = rate == 30;
            var tools = input.GetComponent<FirefighterTools>();
            input.TickScreenPointerCommand(Vector2.zero, view.WorldToScreenPoint(target), disconnect,
                !disconnect, !disconnect, false, false, 1f / rate);
            Check(!emitter.IsSpraying && lab.Airborne > 0 && (disconnect
                ? !emitter.Connection.IsConnected : tools.SelectedTool == FirefighterTools.Tool.Axe),
                disconnect ? "Disconnect stops new water and keeps existing flights" : "Switching to axe keeps existing flights");
            for (int i = 0; i < rate * 2; i++) Step(target, false, false, 1f / rate);
            Check(lab.Airborne < .00001, $"All existing flights settle at {rate} Hz");
            return (120 - lab.SourceRemaining, lab.FloorVolume + lab.CounterVolume);
        }

        void Capture()
        {
            var previous = RenderTexture.active;
            view.targetTexture = capture;
            view.Render();
            RenderTexture.active = capture;
            image.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0);
            File.WriteAllBytes(Path.Combine(captureFolder, $"frame{frame++:D4}.png"), image.EncodeToPNG());
            view.targetTexture = null;
            RenderTexture.active = previous;
        }

        void Check(bool pass, string message)
        {
            failed |= !pass;
            lines.Add((pass ? "PASS " : "FAIL ") + message);
        }

        void Update()
        {
            if (!finished && began > 0f && Time.realtimeSinceStartup - began > 120f)
            {
                lines.Add("FAIL timed out");
                Finish(false);
            }
        }

        void OnLog(string message, string stack, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
            lines.Add(message + "\n" + stack);
            Finish(false);
        }

        void Finish(bool pass)
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= OnLog;
            if (capture) { capture.Release(); Destroy(capture); }
            if (image) Destroy(image);
            string label = TestMode switch
            {
                WaterLabValidation.Mode.Width => "WATER_WIDTH",
                WaterLabValidation.Mode.Steam => "WATER_STEAM",
                WaterLabValidation.Mode.Aim => "WATER_AIM",
                _ => "WATER_LAB"
            };
            File.WriteAllLines(Path.Combine(evidence, "results.txt"),
                new[] { label + (pass ? "_PASS" : "_FAIL") }.Concat(lines));
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
