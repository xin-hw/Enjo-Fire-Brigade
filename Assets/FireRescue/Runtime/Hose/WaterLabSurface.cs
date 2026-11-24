using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FireRescue
{
    // One horizontal receiving surface. Edges exist only between adjacent free cells.
    // All transfers are paired and use the same pre-step volume snapshot.
    sealed class WaterLabSurface : IDisposable
    {
        const float CellSize = .12f;
        // The house's floor finish extends 4.5 mm above its shared ground collider.
        const float FilmHeight = .006f;
        const double RetainedFilm = .018;
        const double BoilingRatePerSquareMetre = .85;
        const int MaxRipples = 8;
        const float RippleLifetime = .72f;
        readonly Collider collider;
        readonly Bounds bounds;
        readonly int width, height;
        readonly float stepX, stepZ;
        readonly double[] water, delta;
        readonly bool[] open, east, north;
        readonly Color32[] pixels;
        readonly Texture2D texture;
        readonly GameObject view;
        readonly Material material;
        readonly Vector4[] ripples = new Vector4[MaxRipples];
        static readonly int RipplePositions = Shader.PropertyToID("_Ripples");
        static readonly int WaterClock = Shader.PropertyToID("_WaterTime");
        static readonly int RippleActive = Shader.PropertyToID("_RippleActive");
        readonly LayerMask obstacles;
        readonly bool sampleFootprint;
        bool dirty, spreading;
        float lastRipple = float.NegativeInfinity;
        int rippleIndex;

        public double Volume { get; private set; }
        public int WetCells { get; private set; }
        public Collider Collider => collider;

        public WaterLabSurface(Collider surface, Bounds area, Shader shader, Transform parent, LayerMask obstacles, bool sampleFootprint)
        {
            collider = surface;
            bounds = area;
            this.obstacles = obstacles;
            this.sampleFootprint = sampleFootprint;
            width = Mathf.CeilToInt(bounds.size.x / CellSize);
            height = Mathf.CeilToInt(bounds.size.z / CellSize);
            stepX = bounds.size.x / width;
            stepZ = bounds.size.z / height;
            int count = width * height;
            water = new double[count];
            delta = new double[count];
            open = new bool[count];
            east = new bool[count];
            north = new bool[count];
            pixels = new Color32[count];

            RefreshObstacles();

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { name = surface.name + " water depth", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            material = new Material(shader) { name = surface.name + " water film", renderQueue = 2990 };
            material.SetFloat("_Surface", 1f);
            material.SetTexture("_WaterData", texture);
            material.SetVector("_Grid", new Vector4(width, height, 1f / width, 1f / height));
            material.SetVectorArray(RipplePositions, ripples);
            view = GameObject.CreatePrimitive(PrimitiveType.Quad);
            view.name = surface.name + " water film";
            var viewCollider = view.GetComponent<Collider>();
            viewCollider.enabled = false;
            Object.Destroy(viewCollider);
            view.transform.SetParent(parent);
            view.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + FilmHeight, bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));
            view.transform.localScale = new Vector3(bounds.size.x, bounds.size.z, 1f);
            var renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            view.SetActive(false);
        }

        Vector3 Center(int x, int z) => new Vector3(bounds.min.x + (x + .5f) * stepX,
            bounds.max.y + .027f, bounds.min.z + (z + .5f) * stepZ);

        void RefreshObstacles() => RefreshObstacles(0, 0, width - 1, height - 1);

        public void RefreshObstacles(Bounds changedBounds)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt((changedBounds.min.x - bounds.min.x) / stepX) - 1, 0, width - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((changedBounds.min.z - bounds.min.z) / stepZ) - 1, 0, height - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((changedBounds.max.x - bounds.min.x) / stepX) + 1, 0, width - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt((changedBounds.max.z - bounds.min.z) / stepZ) + 1, 0, height - 1);
            RefreshObstacles(minX, minZ, maxX, maxZ);
        }

        void RefreshObstacles(int minX, int minZ, int maxX, int maxZ)
        {
            var half = new Vector3(stepX * .49f, .012f, stepZ * .49f);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector3 center = Center(x, z);
                bool clear = !Physics.CheckBox(center, half, Quaternion.identity, obstacles, QueryTriggerInteraction.Ignore);
                if (clear && sampleFootprint)
                {
                    var down = new Ray(center + Vector3.up * 2.6f, Vector3.down);
                    clear = collider is MeshCollider
                        ? collider.Raycast(down, out var top, 2.65f) && Mathf.Abs(top.point.y - bounds.max.y) < .045f
                        : !Physics.Raycast(down, 2.59f, obstacles, QueryTriggerInteraction.Ignore);
                }
                open[z * width + x] = clear;
            }
            // Rebuild the edges entering this range from the west and south as well.
            for (int z = Mathf.Max(0, minZ - 1); z <= maxZ; z++)
            for (int x = Mathf.Max(0, minX - 1); x <= maxX; x++)
            {
                int i = z * width + x;
                Vector3 center = Center(x, z);
                east[i] = open[i] && x + 1 < width && open[i + 1] &&
                    !Physics.Linecast(center, Center(x + 1, z), obstacles, QueryTriggerInteraction.Ignore);
                north[i] = open[i] && z + 1 < height && open[i + width] &&
                    !Physics.Linecast(center, Center(x, z + 1), obstacles, QueryTriggerInteraction.Ignore);
            }
            if (Volume > 0d) dirty = spreading = true;
        }

        public bool Deposit(Vector3 point, double amount)
        {
            if (!TryCell(point, out int index)) return false;
            if (!open[index]) return false;
            water[index] += amount;
            Volume += amount;
            dirty = spreading = true;
            return true;
        }

        public int OpenCellsIn(Bounds region)
        {
            int count = 0;
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                Vector3 center = Center(x, z);
                if (open[z * width + x] && center.x >= region.min.x && center.x <= region.max.x &&
                    center.z >= region.min.z && center.z <= region.max.z) count++;
            }
            return count;
        }

        public void AddRipple(Vector3 point, float normalSpeed, float time)
        {
            if (normalSpeed < .8f || time - lastRipple < .072f) return;
            lastRipple = time;
            ripples[rippleIndex] = new Vector4(point.x, point.z, time, Mathf.InverseLerp(.8f, 6f, normalSpeed));
            rippleIndex = (rippleIndex + 1) % ripples.Length;
            material.SetVectorArray(RipplePositions, ripples);
        }

        bool TryCell(Vector3 point, out int index)
        {
            int x = Mathf.FloorToInt((point.x - bounds.min.x) / stepX);
            int z = Mathf.FloorToInt((point.z - bounds.min.z) / stepZ);
            index = z * width + x;
            return x >= 0 && x < width && z >= 0 && z < height;
        }

        public void Spread()
        {
            if (!spreading) return;
            spreading = false;
            Array.Clear(delta, 0, delta.Length);
            for (int i = 0; i < water.Length; i++)
            {
                if (east[i]) Transfer(i, i + 1);
                if (north[i]) Transfer(i, i + width);
            }
            for (int i = 0; i < water.Length; i++) water[i] += delta[i];
        }

        // Standing and flowing water share one account. Contact removes only
        // the volume that supplied cooling; the remaining film has not reacted.
        public void ReactToFire(FireField field, float dt, Action<Vector3, double, bool> vaporized)
        {
            if (!field || Volume <= 0d) return;
            double limit = BoilingRatePerSquareMetre * stepX * stepZ * dt;
            for (int i = 0; i < water.Length; i++)
            {
                if (!open[i] || water[i] <= 0d) continue;
                Vector3 point = Center(i % width, i / width);
                point.y = field.Origin.y;
                if (!field.TryGetCell(point, out var fireCell)) continue;
                var heat = field.GetCell(fireCell.x, fireCell.y);
                if (heat.Intensity <= 0f) continue;
                Vector3 fireCenter = field.CellCenter(fireCell.x, fireCell.y);
                point.y = fireCenter.y = bounds.max.y + .10f;
                if (Physics.Linecast(point, fireCenter, obstacles, QueryTriggerInteraction.Ignore)) continue;
                point.y = field.Origin.y;
                double vapor = field.ReactToSurfaceWater(point, Math.Min(water[i], limit));
                if (vapor <= 0d) continue;
                water[i] -= vapor;
                Volume = Math.Max(0d, Volume - vapor);
                dirty = spreading = true;
                vaporized(point, vapor, heat.IsBurning);
            }
        }

        void Transfer(int a, int b)
        {
            double difference = water[a] - water[b];
            double excess = Math.Abs(difference) - RetainedFilm;
            if (excess < .000001d) return;
            double transfer = Math.Sign(difference) * excess * .22;
            delta[a] -= transfer;
            delta[b] += transfer;
            dirty = spreading = true;
        }

        public void Refresh(float time)
        {
            if (dirty)
            {
                dirty = false;
                WetCells = 0;
                for (int i = 0; i < water.Length; i++)
                {
                    if (water[i] >= .004) WetCells++;
                    byte depth = open[i]
                        ? (byte)Mathf.RoundToInt(255f * Mathf.Sqrt(Mathf.Clamp01((float)(water[i] / .14))))
                        : (byte)0;
                    pixels[i] = new Color32(depth, open[i] ? (byte)255 : (byte)0, 0, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                if (!view.activeSelf) view.SetActive(true);
            }
            if (view.activeSelf)
            {
                material.SetFloat(WaterClock, time);
                material.SetFloat(RippleActive, time - lastRipple < RippleLifetime ? 1f : 0f);
            }
        }

        public int WetRegions()
        {
            var seen = new bool[water.Length];
            var queue = new int[water.Length];
            int regions = 0;
            for (int i = 0; i < water.Length; i++)
            {
                if (seen[i] || water[i] < .008) continue;
                regions++;
                int end = 0;
                Visit(i);
                for (int start = 0; start < end; start++)
                {
                    int cell = queue[start];
                    if (east[cell]) Visit(cell + 1);
                    if (cell % width > 0 && east[cell - 1]) Visit(cell - 1);
                    if (north[cell]) Visit(cell + width);
                    if (cell >= width && north[cell - width]) Visit(cell - width);
                }

                void Visit(int cell)
                {
                    if (seen[cell] || water[cell] < .008) return;
                    seen[cell] = true;
                    queue[end++] = cell;
                }
            }
            return regions;
        }

        public double VolumeIn(Bounds region)
        {
            double volume = 0d;
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                Vector3 center = Center(x, z);
                if (center.x >= region.min.x && center.x <= region.max.x &&
                    center.z >= region.min.z && center.z <= region.max.z)
                    volume += water[z * width + x];
            }
            return volume;
        }

        public double VolumeUnder(Collider roof)
        {
            Bounds footprint = roof.bounds;
            double volume = 0d;
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                if (water[index] <= 0d) continue;
                Vector3 center = Center(x, z);
                if (center.x < footprint.min.x || center.x > footprint.max.x ||
                    center.z < footprint.min.z || center.z > footprint.max.z) continue;
                var down = new Ray(new Vector3(center.x, footprint.max.y + .10f, center.z), Vector3.down);
                if (roof.Raycast(down, out _, footprint.size.y + .11f)) volume += water[index];
            }
            return volume;
        }

        public void Dispose()
        {
            Object.Destroy(view);
            Object.Destroy(material);
            Object.Destroy(texture);
        }
    }
}
