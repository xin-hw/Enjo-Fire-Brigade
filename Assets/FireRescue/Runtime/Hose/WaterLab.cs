using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    // Owns conserved surface water and the transparent droplet presentation.
    [DefaultExecutionOrder(100)]
    public sealed class WaterLab : MonoBehaviour
    {
        public enum SprayWidth { Concentrated, Wide }

        const float Step = 1f / 60f;
        const float SpreadInterval = .05f;
        const float SplashRadius = .014f;
        const int SplashCapacity = 128;
        const int VertexCapacity = (SprayEmitter.ParticleCapacity + SplashCapacity) * 6;
        const double StartingWater = 120d;

        [SerializeField] SprayEmitter emitter;
        [SerializeField] FluidSource source;
        [SerializeField] Camera view;
        [SerializeField] Collider floor, counter;
        [SerializeField] Shader shader;
        [SerializeField] LayerMask barriers = 1 << 8;
        [SerializeField, Tooltip("Choose the test spray width before entering Play Mode.")]
        SprayWidth sprayWidth;
        [SerializeField] WaterFireLab fireTrial;
        [SerializeField] bool useLevelSurfaces;
        [SerializeField] FireField levelFire;
        [SerializeField] ElementReaction levelReaction;
        [SerializeField] ObjectFire oilPan;
        WaterSteamView steam;
        [SerializeField] BreakableDoor[] doors;

        struct Splash
        {
            public Vector3 Position, Velocity;
            public double Volume;
            public float Age;
            public uint Seed;
        }

        readonly Splash[] splashes = new Splash[SplashCapacity];
        readonly List<Vector3> vertices = new(VertexCapacity);
        readonly List<Vector2> uvs = new(VertexCapacity);
        readonly List<Color32> colors = new(VertexCapacity);
        readonly List<int> triangles = new(VertexCapacity * 2);
        WaterLabSurface floorWater, counterWater;
        Material dropMaterial;
        Mesh dropMesh;
        SprayProfile runtimeProfile;
        Bounds[] doorFootprints;
        int splashCount;
        uint seed;
        float simClock, spreadClock, waterTime;
        double drained;
        Action<Vector3, double, bool> vaporized;

        public bool AutomaticTick { get; set; } = true;
        internal Transform PresentationRoot { get; private set; }
        public double SourceRemaining => source.RemainingWater;
        public double FloorVolume => floorWater.Volume;
        public double CounterVolume => counterWater.Volume;
        public double Discharged => drained + emitter.WaterDiscarded;
        public double Evaporated { get; private set; }
        public double Airborne
        {
            get
            {
                double water = emitter.AirborneWaterAmount;
                for (int i = 0; i < splashCount; i++) water += splashes[i].Volume;
                return water;
            }
        }
        public double BalanceError => source.WaterDrawn - (Airborne + FloorVolume + CounterVolume + Discharged + Evaporated);
        public int FloorWetCells => floorWater.WetCells;
        public int FloorWetRegions => floorWater.WetRegions();
        public int ActiveSplashCount => splashCount;
        public double FloorVolumeIn(Bounds region) => floorWater.VolumeIn(region);
        internal double FloorVolumeUnderCounter => floorWater.VolumeUnder(counter);
        internal int OpenFloorCellsIn(Bounds region) => floorWater.OpenCellsIn(region);
        float SpreadDegrees => sprayWidth == SprayWidth.Wide ? 34f : 18f;

        public void SetSprayWidth(SprayWidth width)
        {
            sprayWidth = width;
            if (!runtimeProfile) return;
            var settings = runtimeProfile.Settings;
            settings.SpreadAngle = SpreadDegrees;
            runtimeProfile.Configure(settings);
        }
        public void SetFireTrial(WaterFireLab trial) => fireTrial = trial;

        public void Initialize(SprayEmitter spray, FluidSource water, Camera cameraView, BoxCollider ground,
            BoxCollider tabletop, Shader waterShader)
        {
            emitter = spray;
            source = water;
            view = cameraView;
            floor = ground;
            counter = tabletop;
            shader = waterShader;
        }

        public void InitializeLevel(SprayEmitter spray, FluidSource water, Camera cameraView, Collider ground,
            Collider tabletop, Shader waterShader, FireField field, ElementReaction reaction, ObjectFire pan,
            BreakableDoor[] breakableDoors)
        {
            emitter = spray;
            source = water;
            view = cameraView;
            floor = ground;
            counter = tabletop;
            shader = waterShader;
            levelFire = field;
            levelReaction = reaction;
            oilPan = pan;
            doors = breakableDoors;
            useLevelSurfaces = true;
        }

        void Awake()
        {
            PresentationRoot = new GameObject("Water presentation").transform;
            PresentationRoot.SetParent(transform, false);
            Physics.SyncTransforms();
            if (useLevelSurfaces)
            {
                levelReaction.UsePhysicalWaterDeposits();
                var steamObject = new GameObject("Water contact steam");
                steamObject.transform.SetParent(PresentationRoot, false);
                steam = steamObject.AddComponent<WaterSteamView>();
            }
            else source.SetWaterBudget(StartingWater);
            if (doors != null)
            {
                doorFootprints = new Bounds[doors.Length];
                for (int i = 0; i < doors.Length; i++) doorFootprints[i] = doors[i].BlockingCollider.bounds;
            }
            vaporized = Vaporized;
            emitter.UseDropletBundle = true;
            var settings = source.SpraySettings;
            settings.SpreadAngle = SpreadDegrees;
            settings.CollisionRadius = .035f;
            runtimeProfile = ScriptableObject.CreateInstance<SprayProfile>();
            runtimeProfile.Configure(settings);
            runtimeProfile.ConfigureEffects(source.SprayEffects);
            source.SetSprayProfile(runtimeProfile);
            Bounds floorArea = floor.bounds;
            if (useLevelSurfaces)
            {
                Vector3 size = new(levelFire.Width * levelFire.CellSize, 0f, levelFire.Height * levelFire.CellSize);
                Vector3 center = levelFire.Origin + size * .5f;
                center.y = floor.bounds.max.y;
                floorArea = new Bounds(center, size);
            }
            floorWater = new WaterLabSurface(floor, floorArea, shader, PresentationRoot, barriers, useLevelSurfaces);
            counterWater = new WaterLabSurface(counter, counter.bounds, shader, PresentationRoot, barriers, useLevelSurfaces);

            var drops = new GameObject("Water droplets and splash");
            drops.transform.SetParent(PresentationRoot, false);
            dropMesh = new Mesh { name = "Batched water fragments" };
            dropMesh.MarkDynamic();
            drops.AddComponent<MeshFilter>().sharedMesh = dropMesh;
            var renderer = drops.AddComponent<MeshRenderer>();
            dropMaterial = new Material(shader) { name = "Water fragment sheen", renderQueue = 3010 };
            renderer.sharedMaterial = dropMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void OnEnable()
        {
            emitter.ParticleImpacted += DepositImpact;
            if (doors != null) foreach (var door in doors) door.StateChanged += RefreshFloorObstacles;
        }

        void OnDisable()
        {
            emitter.ParticleImpacted -= DepositImpact;
            if (doors != null) foreach (var door in doors) door.StateChanged -= RefreshFloorObstacles;
        }

        void RefreshFloorObstacles(BreakableDoor door)
        {
            int index = Array.IndexOf(doors, door);
            if (index >= 0) floorWater.RefreshObstacles(doorFootprints[index]);
        }

        void Start()
        {
            if (useLevelSurfaces)
            {
                // The concave cabinet has a solid footprint even where a low overlap
                // touches none of its mesh triangles. Keep those cells out of floor fire.
                levelFire.SetSolidFloor(UnderCounter);
            }
            else if (emitter.Connection.TryConnect(source))
                emitter.GetComponent<FirefighterTools>().EquipHose();
        }

        bool UnderCounter(Vector3 point)
        {
            var ray = new Ray(new Vector3(point.x, counter.bounds.max.y + .10f, point.z), Vector3.down);
            return counter.Raycast(ray, out _, counter.bounds.size.y + .11f);
        }

        void LateUpdate()
        {
            if (!AutomaticTick) return;
            Simulate(Time.deltaTime);
            RefreshView();
        }

        void DepositImpact(SprayEmitter.ParticleImpact impact)
        {
            if (impact.Element != ElementKind.Water) return;
            if ((fireTrial && fireTrial.IsOilPan(impact.Collider)) ||
                (oilPan && impact.Collider.GetComponentInParent<ObjectFire>() == oilPan))
            {
                // The existing element receiver already handled the oil incident.
                drained += impact.Amount;
                return;
            }
            WaterLabSurface surface = ReceivingSurface(impact.Collider, impact.Normal);
            float normalSpeed = Mathf.Max(0f, -Vector3.Dot(impact.Velocity, impact.Normal));
            if (splashCount == SplashCapacity)
            {
                if (Deposit(surface, impact.Position, impact.Amount))
                    surface.AddRipple(impact.Position, normalSpeed, waterTime);
                return;
            }

            double splashVolume = surface == null ? impact.Amount : impact.Amount * .16;
            if (surface != null && Deposit(surface, impact.Position, impact.Amount - splashVolume))
                surface.AddRipple(impact.Position, normalSpeed, waterTime);
            Vector3 tangent = Vector3.ProjectOnPlane(impact.Velocity, impact.Normal);
            float kick = Mathf.Clamp(normalSpeed * .24f, .48f, 1.75f);
            uint id = seed++;
            Vector3 along = tangent.sqrMagnitude > .01f ? tangent.normalized : Vector3.Cross(impact.Normal, Vector3.forward).normalized;
            if (along == Vector3.zero) along = Vector3.right;
            Vector3 side = Vector3.Cross(impact.Normal, along);
            float bearing = SprayEmitter.SampleVariation(id, 13) * Mathf.PI * 2f;
            Vector3 radial = along * Mathf.Cos(bearing) + side * Mathf.Sin(bearing);
            Vector3 rebound = impact.Normal * kick * Mathf.Lerp(.53f, 1.28f, SprayEmitter.SampleVariation(id, 101));
            Vector3 point = impact.Position + impact.Normal * (SplashRadius + .01f);
            bool split = normalSpeed > 2.2f && (id & 3u) == 0 && splashCount + 1 < SplashCapacity;
            double primary = split ? splashVolume * .68d : splashVolume;
            AddSplash(point, rebound + tangent * .19f + radial * (.34f + kick * .70f), primary, id);
            if (split)
                AddSplash(point, rebound * 1.11f + tangent * .12f - radial * (.42f + kick * .67f),
                    splashVolume - primary, seed++);
        }

        void AddSplash(Vector3 point, Vector3 velocity, double volume, uint id)
        {
            splashes[splashCount++] = new Splash
            {
                Position = point,
                Velocity = velocity,
                Volume = volume,
                Seed = id
            };
        }

        WaterLabSurface ReceivingSurface(Collider collider, Vector3 normal)
        {
            if (normal.y < .7f) return null;
            if (collider == floorWater.Collider ||
                (useLevelSurfaces && collider is BoxCollider && collider.gameObject.layer == floor.gameObject.layer &&
                    Mathf.Abs(collider.bounds.max.y - floor.bounds.max.y) < .03f))
                return floorWater;
            return collider == counterWater.Collider ? counterWater : null;
        }

        bool Deposit(WaterLabSurface surface, Vector3 position, double amount)
        {
            if (surface != null && surface.Deposit(position, amount)) return true;
            drained += amount;
            return false;
        }

        void Vaporized(Vector3 point, double volume, bool boiling)
        {
            Evaporated += volume;
            // Neighbouring warmth may quietly evaporate a film. Only actual
            // burning contact pays into the visible white-steam particle budget.
            if (!boiling) return;
            var contactSteam = steam ? steam : fireTrial ? fireTrial.Steam : null;
            if (contactSteam && contactSteam.isActiveAndEnabled) contactSteam.AddVapor(point, volume);
        }

        public void Simulate(float dt)
        {
            if (!float.IsFinite(dt) || dt <= 0f) return;
            simClock += Mathf.Min(dt, .2f);
            waterTime += Mathf.Min(dt, .2f);
            while (simClock >= Step - .000001f)
            {
                simClock -= Step;
                AdvanceSplashes();
                spreadClock += Step;
                if (spreadClock < SpreadInterval - .000001f) continue;
                spreadClock -= SpreadInterval;
                floorWater.Spread();
                counterWater.Spread();
                floorWater.ReactToFire(levelFire ? levelFire : fireTrial ? fireTrial.Field : null,
                    SpreadInterval, vaporized);
            }
        }

        void AdvanceSplashes()
        {
            for (int i = splashCount - 1; i >= 0; i--)
            {
                Splash splash = splashes[i];
                Vector3 next = splash.Position + splash.Velocity * Step + Vector3.down * (8f * Step * Step);
                if (emitter.FindCollision(splash.Position, next, SplashRadius, out var hit, out var point, out var normal))
                {
                    Deposit(ReceivingSurface(hit, normal), point, splash.Volume);
                    splashes[i] = splashes[--splashCount];
                }
                else if (splash.Age >= .85f)
                {
                    drained += splash.Volume;
                    splashes[i] = splashes[--splashCount];
                }
                else
                {
                    splash.Age += Step;
                    splash.Velocity += Vector3.down * (16f * Step);
                    splash.Position = next;
                    splashes[i] = splash;
                }
            }
        }

        public void RefreshView()
        {
            floorWater.Refresh(waterTime);
            counterWater.Refresh(waterTime);
            if (emitter.ActiveParticleCount == 0 && splashCount == 0)
            {
                if (dropMesh.vertexCount != 0) dropMesh.Clear();
                return;
            }
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();
            for (int i = 0; i < emitter.ActiveParticleCount; i++)
            {
                var drop = emitter.GetParticle(i);
                if (drop.Element != ElementKind.Water) continue;
                Vector3 path = drop.Position - drop.TrailStart;
                if (path.sqrMagnitude < .000025f) continue;
                float width = Mathf.Lerp(.037f, .086f, SprayEmitter.SampleVariation(drop.Seed, 53));
                width *= Mathf.Lerp(.72f, 1f, Mathf.InverseLerp(.006f, .070f, drop.AgeSeconds));
                AppendSheet(drop.TrailStart, drop.TrailMiddle, drop.Position, width, drop.Seed,
                    (byte)Mathf.Lerp(137f, 220f, SprayEmitter.SampleVariation(drop.Seed, 103)), false);
            }
            for (int i = 0; i < splashCount; i++)
            {
                Splash splash = splashes[i];
                Vector3 movement = Vector3.ClampMagnitude(splash.Velocity * Mathf.Min(splash.Age, .031f), .091f);
                if (movement.sqrMagnitude < .000009f) continue;
                float age = Mathf.Clamp01(splash.Age * 2.3f);
                float width = Mathf.Lerp(.017f, .029f, SprayEmitter.SampleVariation(splash.Seed, 43)) * Mathf.Lerp(1f, .64f, age);
                AppendSheet(splash.Position - movement, splash.Position - movement * .36f, splash.Position,
                    width, splash.Seed, (byte)Mathf.Lerp(210f, 120f, age), true);
            }
            dropMesh.Clear();
            if (vertices.Count == 0) return;
            dropMesh.SetVertices(vertices);
            dropMesh.SetUVs(0, uvs);
            dropMesh.SetColors(colors);
            dropMesh.SetTriangles(triangles, 0);
        }

        void AppendSheet(Vector3 tail, Vector3 middle, Vector3 tip, float width, uint id, byte alpha, bool splash)
        {
            Vector3 side = Vector3.Cross(view.transform.forward, tip - tail).normalized;
            if (side.sqrMagnitude < .000001f) side = view.transform.right;
            byte shape = (byte)(255f * SprayEmitter.SampleVariation(id, 79));
            var tint = new Color32(shape, splash ? (byte)255 : (byte)0,
                (byte)(255f * SprayEmitter.SampleVariation(id, 87)), alpha);
            int offset = vertices.Count;
            AddRow(tail, side, width, 0f, tint);
            AddRow(middle, side, width, .50f, tint);
            AddRow(tip, side, width, 1f, tint);
            for (int row = 0; row < 2; row++)
            {
                int start = offset + row * 2;
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 3);
            }
        }

        void AddRow(Vector3 center, Vector3 side, float width, float along, Color32 tint)
        {
            Vector3 spread = side * width;
            vertices.Add(transform.InverseTransformPoint(center - spread));
            vertices.Add(transform.InverseTransformPoint(center + spread));
            uvs.Add(new Vector2(0f, along));
            uvs.Add(new Vector2(1f, along));
            colors.Add(tint);
            colors.Add(tint);
        }

        void OnDestroy()
        {
            floorWater?.Dispose();
            counterWater?.Dispose();
            if (dropMesh) Destroy(dropMesh);
            if (dropMaterial) Destroy(dropMaterial);
            if (runtimeProfile) Destroy(runtimeProfile);
            if (PresentationRoot) Destroy(PresentationRoot.gameObject);
        }
    }
}
