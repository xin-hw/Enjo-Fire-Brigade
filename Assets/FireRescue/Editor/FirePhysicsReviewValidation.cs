using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    [InitializeOnLoad]
    public static class FirePhysicsReviewValidation
    {
        const string RunKey = "FireRescue.FirePhysicsReviewValidation";

        static FirePhysicsReviewValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey, false)) return;
                SessionState.EraseBool(RunKey);
                new GameObject("Fire physics review capture").AddComponent<FirePhysicsReviewDriver>();
            };
        }

        public static void BatchRun()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(FirePhysicsReviewSetup.ScenePath))
                FirePhysicsReviewSetup.Create();
            EditorSceneManager.OpenScene(FirePhysicsReviewSetup.ScenePath);
            SessionState.SetBool(RunKey, true);
            EditorApplication.EnterPlaymode();
        }
    }

    sealed class FirePhysicsReviewDriver : MonoBehaviour
    {
        const float FrameTime = 1f / 30f;
        const string Output = "Logs/FirePhysicsReview/art-polish";
        const string Results = Output + "/results.txt";
        static readonly int PaintedDetail = Shader.PropertyToID("_PaintedDetail");
        FireField field;
        FireAirflowSolver airflow;
        FireAirflowFlameView flames;
        LocalPlayerInput input;
        SprayEmitter emitter;
        WaterLab water;
        Camera view;
        RenderTexture capture;
        Texture2D image;
        Material[] paintedMaterials;
        bool failed, finished;
        int frame;

        IEnumerator Start()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Results, $"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}\n");
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = FrameTime;
            yield return null;
            field = FindAnyObjectByType<FireField>();
            airflow = FindAnyObjectByType<FireAirflowSolver>();
            flames = FindAnyObjectByType<FireAirflowFlameView>();
            input = FindAnyObjectByType<LocalPlayerInput>();
            emitter = input.GetComponent<SprayEmitter>();
            water = FindAnyObjectByType<WaterLab>();
            view = Camera.main;
            field.Simulate = false;
            airflow.AutomaticTick = false;
            input.ReadDevices = false;
            input.SetInputFocus(true);
            emitter.AutomaticSimulation = false;
            water.AutomaticTick = false;
            capture = new RenderTexture(1280, 720, 24);
            image = new Texture2D(capture.width, capture.height, TextureFormat.RGB24, false);
            paintedMaterials = view.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .Select(renderer => renderer.sharedMaterial)
                .Where(material => material && material.HasProperty(PaintedDetail) && material.GetFloat(PaintedDetail) > .5f)
                .Distinct().ToArray();

            Check(view.orthographic && Mathf.Abs(view.orthographicSize - 6f) < .001f, "Original gameplay camera distance");
            var physical = input.GetComponent<PhysXHoseDriver>();
            Check(physical && physical.IsActive && emitter.Connection.IsConnected &&
                FindAnyObjectByType<PhysicalHoseTube>().IsVisible, "Lab connects and renders the actual PhysX hose");
            ValidateDoor();
            ValidateCombustion();

            field.ResetSimulation();
            airflow.ResetSimulation();
            field.SetSpreadRate(.032f);
            field.Ignite(WaterFireLabSetup.SmallFire, .40f);
            field.Ignite(WaterFireLabSetup.LargeFire, .95f);
            var target = WaterFireLabSetup.LargeFire;
            var toilet = emitter.Connection.Source;
            yield return Record(18, target);
            var controls = FindAnyObjectByType<FirePhysicsReviewControls>();
            Check(toilet && input.NearbySource == toilet && controls.Focus == toilet,
                "The nearby toilet is the selected outline target");
            ValidateOutlineColour();
            if (failed)
            {
                Finish();
                yield break;
            }
            airflow.SetTestWind(Vector3.left);
            yield return Record(45, target);
            var left = airflow.GetFlameFlow(target, 0f);
            airflow.SetTestWind(Vector3.right);
            yield return Record(45, target);
            var right = airflow.GetFlameFlow(target, 0f);
            Check(left.x < -.2f && right.x > .2f, $"Changing the airflow reverses the field sampled by flame geometry ({left.x:F2}, {right.x:F2})");
            Check(airflow.PeakVerticalVelocity > .15f && airflow.SampleHeat(target + .50f * Vector3.up) > .005f &&
                flames.ActiveFlameCount > 0, "Burning fuel drives rising heat and active flame geometry");
            var smoke = FindAnyObjectByType<FireAirflowSmokeView>();
            Check(smoke && smoke.ActiveWispCount > 0 && smoke.ActiveWispCount <= 12,
                "Combustion feeds the bounded airflow smoke view");
            airflow.SetTestWind(Vector3.zero);
            yield return Record(34, target, movement: new Vector2(.45f, -.24f));
            Check(input.NearbySource != toilet && !controls.Focus,
                "Walking out of reach restores the material-coloured outline");
            float beforeWater = IntensityAt(target);
            yield return Record(78, target, spray: true);
            yield return Record(17, new Vector3(.6f, .1f, .12f), spray: true);
            yield return Record(43, target);
            Check(beforeWater > .2f && IntensityAt(target) < beforeWater * .30f && water.Evaporated > .001,
                $"Real spray cools fuel at contact ({beforeWater:F3} to {IntensityAt(target):F3}), vapor {water.Evaporated:F4}");
            Check(water.FloorVolume > .05 && water.Airborne < .001 &&
                Math.Abs(water.BalanceError) < .001, $"Airborne water settles into the floor film, balance {water.BalanceError:F6}");
            var outlines = view.GetComponent<WorldOutlineView>();
            Capture("puddles-with-outline.png");
            outlines.enabled = false;
            Capture("puddles-without-outline.png");
            outlines.enabled = true;
            Finish();
        }

        void ValidateOutlineColour()
        {
            var outlines = view.GetComponent<WorldOutlineView>();
            var selected = outlines.Focus;
            outlines.enabled = false;
            Capture("dry-without-outline.png");
            var baseline = image.GetPixels32();
            outlines.enabled = true;
            outlines.Focus = null;
            Capture("dry-neutral-outline.png");
            var neutral = image.GetPixels32();
            outlines.Focus = selected;
            Capture("dry-green-outline.png");
            var green = image.GetPixels32();
            int changedColour = 0;
            int sameFootprint = 0;
            for (int i = 0; i < green.Length; i++)
            {
                if (ColourDifference(neutral[i], green[i]) < 12) continue;
                changedColour++;
                if (ColourDifference(baseline[i], neutral[i]) >= 6 &&
                    ColourDifference(baseline[i], green[i]) >= 6)
                    sameFootprint++;
            }
            Check(changedColour > 60 && sameFootprint >= changedColour * .98f,
                $"Green selection repaints the same rendered outline pixels ({sameFootprint}/{changedColour})");
        }

        static int ColourDifference(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        void ValidateDoor()
        {
            var door = FindAnyObjectByType<BreakableDoor>();
            var hot = new Vector3(1.80f, field.Origin.y, -.20f);
            var far = new Vector3(2.20f, field.Origin.y, -.20f);
            (float fire, float heat, int solids) Sample()
            {
                field.ResetSimulation();
                airflow.ResetSimulation();
                field.SetSpreadRate(.32f);
                field.Ignite(hot, .95f);
                airflow.SetTestWind(Vector3.right);
                for (int i = 0; i < 18; i++)
                {
                    field.Step(FrameTime);
                    airflow.Step(FrameTime);
                }
                return (IntensityAt(far), airflow.SampleHeat(far + .18f * Vector3.up), airflow.SolidCellCount);
            }

            bool closed = door.BlockingCollider.enabled && !field.CanTransferHeat(hot, far);
            var blocked = Sample();
            door.BurnDown();
            Physics.SyncTransforms();
            bool open = !door.BlockingCollider.enabled && field.CanTransferHeat(hot, far);
            var clear = Sample();
            Check(closed && open && blocked.fire < .01f && clear.fire > .08f,
                $"Actual door collider blocks and releases fire spread ({blocked.fire:F4}, {clear.fire:F4})");
            Check(clear.heat > blocked.heat + .01f,
                $"Hot air crosses the open doorway ({blocked.heat:F4}, {clear.heat:F4}; occupied {blocked.solids}/{clear.solids})");
            door.ResetDamageState();
            door.transform.Find("Visual").gameObject.SetActive(true);
            Physics.SyncTransforms();
            airflow.SetTestWind(Vector3.zero);
        }

        void ValidateCombustion()
        {
            var target = WaterFireLabSetup.SmallFire;
            field.ResetSimulation();
            field.SetSpreadRate(0f);
            field.Ignite(target, .95f);
            for (int i = 0; i < 120; i++) field.Step(.25f);
            field.TryGetCell(target, out var cell);
            var exhausted = field.GetCell(cell.x, cell.y);
            Check(exhausted.Fuel <= .001f && exhausted.Intensity <= .001f, "Finite fuel is consumed and the dry fire goes out");
            airflow.ResetSimulation();
            field.ResetSimulation();
            field.Ignite(target, .95f);
            for (int i = 0; i < 45; i++) airflow.Step(FrameTime);
            float peak = airflow.TotalHeat;
            field.ResetSimulation();
            for (int i = 0; i < 75; i++) airflow.Step(FrameTime);
            Check(peak > .1f && airflow.TotalHeat < peak * .22f,
                $"Residual heat decays without a source ({peak:F4} to {airflow.TotalHeat:F4})");
        }

        IEnumerator Record(int frames, Vector3 target, bool spray = false, Vector2 movement = default)
        {
            for (int i = 0; i < frames; i++)
            {
                const float step = .5f * FrameTime;
                for (int substep = 0; substep < 2; substep++)
                {
                    input.TickPointerCommand(movement, target, false, false, false, false,
                        spray && i == 0 && substep == 0, spray, step, true);
                    emitter.Advance(step);
                    water.Simulate(step);
                    field.Step(step);
                }
                airflow.Step(FrameTime);
                water.RefreshView();
                yield return null;
                if (frame is 4 or 103 or 156 or 226 or 279) CapturePreviousArt();
                Capture();
            }
        }

        void CapturePreviousArt()
        {
            foreach (Material material in paintedMaterials) material.SetFloat(PaintedDetail, 0f);
            try
            {
                Capture($"previous-{frame:D4}.png");
            }
            finally
            {
                foreach (Material material in paintedMaterials) material.SetFloat(PaintedDetail, 1f);
            }
        }

        void Capture(string file = null)
        {
            var previous = RenderTexture.active;
            var previousTarget = view.targetTexture;
            view.targetTexture = capture;
            view.Render();
            RenderTexture.active = capture;
            image.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0);
            File.WriteAllBytes(Path.Combine(Output, file ?? $"review-{frame++:D4}.png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            view.targetTexture = previousTarget;
        }

        float IntensityAt(Vector3 point) => field.TryGetCell(point, out var cell) ? field.GetCell(cell.x, cell.y).Intensity : 0f;

        void Check(bool passed, string message)
        {
            failed |= !passed;
            File.AppendAllText(Results, (passed ? "PASS " : "FAIL ") + message + "\n");
        }

        void OnLog(string line, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                Check(false, line + "\n" + stack);
                Finish();
            }
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= OnLog;
            Time.captureDeltaTime = 0f;
            if (capture) Destroy(capture);
            if (image) Destroy(image);
            File.AppendAllText(Results, failed ? "FIRE_PHYSICS_REVIEW_FAIL\n" : "FIRE_PHYSICS_REVIEW_PASS\n");
            if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
            else EditorApplication.ExitPlaymode();
        }
    }
}
