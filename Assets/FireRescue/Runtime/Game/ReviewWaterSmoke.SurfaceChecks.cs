using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FireRescue
{
    public sealed partial class ReviewWaterSmoke
    {
        // Isolated edge cases for the physical surface-water reaction. Every object is
        // temporary and far outside the review level so this cannot change its geometry.
        void VerifySurfacePhysicsEdges()
        {
            const int barrierMask = 1 << 8;
            const float reactionStep = .05f;
            Shader shader = ExistingWaterShader();
            Need(shader, "Surface reaction edge checks use the actual water material shader");
            if (!shader) return;

            var root = new GameObject("Temporary surface-water edge checks");
            var surfaces = new List<WaterLabSurface>();
            try
            {
                Vector3 thinOrigin = new(-100f, .1f, 0f);
                var thinField = CreateField(root.transform, thinOrigin, 1, 1, 1f);
                Vector3 wetSide = thinOrigin + new Vector3(.2f,0f,.5f);
                Vector3 fireSide = thinField.CellCenter(0, 0);
                Need(thinField.TryGetCell(wetSide,out var wetCell) && thinField.TryGetCell(fireSide,out var fireCell) && wetCell==fireCell,
                    "Thin-wall probe places wet and burning sides inside the same coarse fire cell");
                thinField.Ignite(fireSide, .8f);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Temporary thin reaction wall";
                wall.layer = 8;
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = new Vector3(-99.65f, .5f, .5f);
                wall.transform.localScale = new Vector3(.018f, 1.2f, 1f);
                var thinSurface = CreateSurface(root.transform, new Bounds(new Vector3(-99.5f, .05f, .5f),
                    new Vector3(1f, .1f, 1f)), shader, surfaces);
                Physics.SyncTransforms();
                bool thinWallBlocks = Physics.Linecast(wetSide + Vector3.up * .1f, fireSide + Vector3.up * .1f,
                    barrierMask, QueryTriggerInteraction.Ignore);
                Need(thinWallBlocks, "The temporary thin wall physically separates the wet and burning centres");
                Need(thinSurface.Deposit(wetSide, .2d) && thinSurface.Volume > .19d,
                    "The thin-wall probe actually stores water on the wet side");
                double thinBefore = thinSurface.Volume;
                float fireBefore = thinField.GetCell(0, 0).Intensity;
                double thinVapor = 0d;
                int thinVisible = 0;
                thinSurface.ReactToFire(thinField, reactionStep, (point, vapor, visible) =>
                {
                    thinVapor += vapor;
                    if (visible) thinVisible++;
                });
                Need(Math.Abs(thinSurface.Volume - thinBefore) < .0000001d && Math.Abs(thinVapor) < .0000001d &&
                    thinVisible == 0 && Mathf.Abs(thinField.GetCell(0, 0).Intensity - fireBefore) < .000001f,
                    "A wall-separated fire cannot consume water or create steam through one coarse fire grid");

                ThresholdResult cold = MeasureThreshold(root.transform, surfaces, shader, -103f, 0f);
                ThresholdResult warm = MeasureThreshold(root.transform, surfaces, shader, -105f, .01f);
                ThresholdResult edge = MeasureThreshold(root.transform, surfaces, shader, -107f, .05f);
                ThresholdResult burning = MeasureThreshold(root.transform, surfaces, shader, -109f, .051f);
                Need(Math.Abs(cold.Spent) < .0000001d && Math.Abs(cold.Evaporated) < .0000001d && cold.VisiblePuffs == 0,
                    "Zero-intensity cold water stays intact and creates no vapor");
                Need(warm.Spent > 0d && warm.Evaporated > 0d && warm.VisiblePuffs == 0 && warm.Remaining < .01f,
                    "Pre-burning intensity .01 exchanges heat without visible steam");
                Need(edge.Spent > 0d && edge.Evaporated > 0d && edge.VisiblePuffs == 0 && edge.Remaining < .05f,
                    "The .05 non-burning boundary exchanges heat without visible steam");
                Need(burning.Spent > 0d && burning.Evaporated > 0d && burning.VisiblePuffs > 0 && burning.Remaining < .051f,
                    "Intensity .051 is burning and creates visible steam from real water contact");

                ScaleResult coarse = MeasureScale(root.transform, surfaces, shader, -112f, 1, .5f);
                ScaleResult fine = MeasureScale(root.transform, surfaces, shader, -114f, 2, .25f);
                Need(Math.Abs(coarse.Evaporated - fine.Evaporated) < .002d &&
                    Math.Abs(coarse.Spent - fine.Spent) < .002d,
                    $"Equal .5m square water has resolution-independent cooling (1 cell {coarse.Evaporated:F5}, 4 cells {fine.Evaporated:F5})");
                File.AppendAllText(Path.Combine(output, "results.txt"),
                    $"SURFACE edge thinWall={thinWallBlocks}; threshold spent .01/.05/.051={warm.Spent:F5}/{edge.Spent:F5}/{burning.Spent:F5}, visible={warm.VisiblePuffs}/{edge.VisiblePuffs}/{burning.VisiblePuffs}; scale={coarse.Evaporated:F5}/{fine.Evaporated:F5}\n");
            }
            finally
            {
                for (int i = 0; i < surfaces.Count; i++) surfaces[i].Dispose();
                Destroy(root);
            }
        }

        Shader ExistingWaterShader()
        {
            foreach (MeshRenderer renderer in water.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial && renderer.sharedMaterial.shader &&
                    renderer.sharedMaterial.shader.name == "FireRescue/WaterLab")
                    return renderer.sharedMaterial.shader;
            return null;
        }

        FireField CreateField(Transform parent, Vector3 origin, int width, int height, float cellSize)
        {
            var fieldObject = new GameObject("Temporary reaction field");
            fieldObject.transform.SetParent(parent, false);
            var field = fieldObject.AddComponent<FireField>();
            field.SetIgnitionSeeds(Array.Empty<Vector2Int>());
            field.Configure(origin, width, height, cellSize);
            field.Simulate = false;
            return field;
        }

        WaterLabSurface CreateSurface(Transform parent, Bounds bounds, Shader shader, List<WaterLabSurface> all)
        {
            var floor = new GameObject("Temporary receiving floor");
            floor.transform.SetParent(parent, false);
            floor.transform.position = bounds.center;
            var collider = floor.AddComponent<BoxCollider>();
            collider.size = bounds.size;
            var surface = new WaterLabSurface(collider, bounds, shader, parent, 1 << 8, false);
            all.Add(surface);
            return surface;
        }

        struct ThresholdResult
        {
            public double Spent, Evaporated;
            public int VisiblePuffs;
            public float Remaining;
        }

        ThresholdResult MeasureThreshold(Transform parent, List<WaterLabSurface> all, Shader shader, float x, float intensity)
        {
            Vector3 origin = new(x, .1f, 0f);
            var field = CreateField(parent, origin, 1, 1, .5f);
            Vector3 point = field.CellCenter(0, 0);
            field.Ignite(point, intensity);
            var surface = CreateSurface(parent, new Bounds(new Vector3(x + .25f, .05f, .25f), new Vector3(.5f, .1f, .5f)), shader, all);
            surface.Deposit(point, .2d);
            double before = surface.Volume, evaporated = 0d;
            int visible = 0;
            for (int i = 0; i < 8; i++)
                surface.ReactToFire(field, .05f, (contact, vapor, isVisible) =>
                {
                    evaporated += vapor;
                    if (isVisible) visible++;
                });
            return new ThresholdResult
            {
                Spent = before - surface.Volume,
                Evaporated = evaporated,
                VisiblePuffs = visible,
                Remaining = field.GetCell(0, 0).Intensity
            };
        }

        struct ScaleResult { public double Spent, Evaporated; }

        ScaleResult MeasureScale(Transform parent, List<WaterLabSurface> all, Shader shader, float x, int cells, float cellSize)
        {
            Vector3 origin = new(x, .1f, 0f);
            var field = CreateField(parent, origin, cells, cells, cellSize);
            var surface = CreateSurface(parent, new Bounds(new Vector3(x + .25f, .05f, .25f), new Vector3(.5f, .1f, .5f)), shader, all);
            for (int z = 0; z < 2; z++)
            for (int column = 0; column < 2; column++)
            {
                Vector3 point = new(x + .125f + column * .25f, .1f, .125f + z * .25f);
                if (cells == 1) field.Ignite(field.CellCenter(0, 0), .8f);
                else field.Ignite(point, .8f);
                surface.Deposit(point, .05d);
            }
            double before = surface.Volume, evaporated = 0d;
            for (int i = 0; i < 160; i++)
                surface.ReactToFire(field, .05f, (contact, vapor, visible) => evaporated += vapor);
            return new ScaleResult { Spent = before - surface.Volume, Evaporated = evaporated };
        }
    }
}
