using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// A bounded, fixed-step air-volume study for FireField presentation. FireField remains
    /// the sole owner of fuel, ignition, spread, water reaction, and damage. This reads its
    /// burning cells as heat sources and exposes simulation-derived heat and velocity.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class FireAirflowSolver : MonoBehaviour
    {
        const int DirectionCount = 6;
        static readonly Vector3Int[] NeighbourOffsets =
        {
            new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1)
        };

        [SerializeField] FireField field;
        [SerializeField] Collider[] obstacleColliders = Array.Empty<Collider>();
        [SerializeField] BreakableDoor[] doors = Array.Empty<BreakableDoor>();
        [SerializeField, Min(2)] int verticalLayers = 7;
        [SerializeField, Min(.08f)] float layerHeight = .28f;
        [SerializeField, Min(.005f)] float fixedStep = 1f / 30f;
        [SerializeField, Min(.1f)] float heatLifetime = .78f;
        [SerializeField, Min(0f)] float heatDiffusion = .24f;
        [SerializeField, Min(0f)] float sourceHeatPerSecond = 2.8f;
        [SerializeField, Min(0f)] float buoyancy = 1.15f;
        [SerializeField, Min(0f)] float flowResponse = 7f;
        [SerializeField, Range(.05f, .45f)] float occupancyProbeFraction = .18f;
        [SerializeField] LayerMask obstacleMask = ~0;
        [SerializeField] Vector3 testWind;
        [SerializeField] bool automaticTick = true;

        readonly Collider[] overlapHits = new Collider[32];
        readonly RaycastHit[] rayHits = new RaycastHit[32];
        readonly HashSet<Collider> obstacleSet = new HashSet<Collider>();
        float[] heat = Array.Empty<float>();
        float[] nextHeat = Array.Empty<float>();
        Vector3[] flow = Array.Empty<Vector3>();
        Vector3[] nextFlow = Array.Empty<Vector3>();
        Vector3[] transportedHeat = Array.Empty<Vector3>();
        float[] outgoingHeat = Array.Empty<float>();
        bool[] solid = Array.Empty<bool>();
        byte[] openEdges = Array.Empty<byte>();
        float accumulator;
        int configuredWidth;
        int configuredHeight;
        bool subscribed;

        public FireField Field => field;
        public Vector3 TestWind => testWind;
        public int StepCount { get; private set; }
        public int SolidCellCount { get; private set; }
        public float TotalHeat { get; private set; }
        public float PeakVerticalVelocity { get; private set; }
        public bool AutomaticTick { get => automaticTick; set => automaticTick = value; }

        public void Initialize(FireField source) => Initialize(source, Array.Empty<Collider>(), Array.Empty<BreakableDoor>());

        public void Initialize(FireField source, Collider[] blockingColliders, BreakableDoor[] environmentDoors)
        {
            UnsubscribeDoors();
            field = source;
            obstacleColliders = blockingColliders ?? Array.Empty<Collider>();
            doors = environmentDoors ?? Array.Empty<BreakableDoor>();
            BuildObstacleSet();
            SubscribeDoors();
            ResetSimulation();
        }

        public void SetTestWind(Vector3 wind) => testWind = new Vector3(wind.x, 0f, wind.z);

        public void ResetSimulation()
        {
            accumulator = 0f;
            StepCount = 0;
            TotalHeat = 0f;
            PeakVerticalVelocity = 0f;
            EnsureGrid();
            Array.Clear(heat, 0, heat.Length);
            Array.Clear(nextHeat, 0, nextHeat.Length);
            Array.Clear(flow, 0, flow.Length);
            Array.Clear(nextFlow, 0, nextFlow.Length);
            RebuildSolidMask();
        }

        /// <summary>
        /// Samples supplied real colliders into local occupancy and edge masks. Door state
        /// changes call this automatically. It is intentionally absent from the step loop.
        /// </summary>
        public void RebuildSolidMask()
        {
            EnsureGrid();
            if (!field || solid.Length == 0) return;
            BuildObstacleSet();
            SolidCellCount = 0;
            Array.Clear(openEdges, 0, openEdges.Length);
            for (int y = 0; y < verticalLayers; y++)
            for (int z = 0; z < configuredHeight; z++)
            for (int x = 0; x < configuredWidth; x++)
            {
                int index = Index(x, y, z);
                solid[index] = IsOccupied(CellCenter(x, y, z));
                if (solid[index]) SolidCellCount++;
            }
            for (int y = 0; y < verticalLayers; y++)
            for (int z = 0; z < configuredHeight; z++)
            for (int x = 0; x < configuredWidth; x++)
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                Vector3Int offset = NeighbourOffsets[direction];
                int nx = x + offset.x, ny = y + offset.y, nz = z + offset.z;
                if (!InBounds(nx, ny, nz)) continue;
                int index = Index(x, y, z);
                if (!solid[index] && !solid[Index(nx, ny, nz)] && IsSegmentClear(CellCenter(x, y, z), CellCenter(nx, ny, nz)))
                    openEdges[index * DirectionCount + direction] = 1;
            }
        }

        public void Step(float deltaTime)
        {
            if (!field || !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
            EnsureGrid();
            accumulator = Mathf.Min(accumulator + deltaTime, fixedStep * 4f);
            while (accumulator >= fixedStep)
            {
                SimulateStep(fixedStep);
                accumulator -= fixedStep;
            }
        }

        public float SampleHeat(Vector3 world) => TryGetIndex(world, out int index) ? heat[index] : 0f;
        public Vector3 SampleFlow(Vector3 world) => TryGetIndex(world, out int index) ? flow[index] : Vector3.zero;
        public Vector3 GetFlameFlow(Vector3 world, float intensity)
            => SampleFlow(world + Vector3.up * layerHeight) + Vector3.up * Mathf.Clamp01(intensity) * .32f;

        internal bool IsOpenAirSegment(Vector3 from, Vector3 to) => IsSegmentClear(from, to);

        void Awake()
        {
            if (!field) field = GetComponent<FireField>();
            BuildObstacleSet();
            SubscribeDoors();
            ResetSimulation();
        }

        void OnEnable() => SubscribeDoors();
        void OnDisable() => UnsubscribeDoors();
        void OnDestroy() => UnsubscribeDoors();
        void Update() { if (automaticTick) Step(Time.deltaTime); }

        void OnValidate()
        {
            verticalLayers = Mathf.Max(2, verticalLayers);
            layerHeight = Mathf.Max(.08f, layerHeight);
            fixedStep = Mathf.Max(.005f, fixedStep);
        }

        void EnsureGrid()
        {
            if (!field) return;
            int width = Mathf.Max(1, field.Width);
            int height = Mathf.Max(1, field.Height);
            int length = width * height * verticalLayers;
            if (configuredWidth == width && configuredHeight == height && heat.Length == length) return;
            configuredWidth = width;
            configuredHeight = height;
            heat = new float[length];
            nextHeat = new float[length];
            flow = new Vector3[length];
            nextFlow = new Vector3[length];
            transportedHeat = new Vector3[length];
            outgoingHeat = new float[length];
            solid = new bool[length];
            openEdges = new byte[length * DirectionCount];
        }

        void SimulateStep(float dt)
        {
            TotalHeat = 0f;
            PeakVerticalVelocity = 0f;
            float response = 1f - Mathf.Exp(-flowResponse * dt);
            float cooling = Mathf.Exp(-dt / heatLifetime);
            float transportStep = dt / Mathf.Max(field.CellSize, layerHeight);
            for (int index = 0; index < heat.Length; index++)
            {
                outgoingHeat[index] = 0f;
                transportedHeat[index] = Vector3.zero;
                if (solid[index]) { nextFlow[index] = Vector3.zero; continue; }
                float source = SourceHeat(index);
                Vector3 desired = testWind + Vector3.up * Mathf.Clamp01(heat[index] + source * dt) * buoyancy;
                nextFlow[index] = ConstrainFlow(index, Vector3.Lerp(flow[index], desired, response));

                // Compute each voxel's exported heat once, before any neighbour reads it.
                if (heat[index] <= 0f) continue;
                float speed = flow[index].magnitude;
                if (speed <= .00001f) continue;
                Vector3 direction = flow[index] / speed;
                float openWeight = 0f;
                for (int face = 0; face < DirectionCount; face++)
                    if (IsOpen(index, face)) openWeight += DirectionalWeight(direction, face);
                if (openWeight <= .0001f) continue;
                float transport = heat[index] * Mathf.Clamp01(speed * transportStep) * .42f;
                outgoingHeat[index] = transport * Mathf.Min(1f, openWeight);
                transportedHeat[index] = direction * (outgoingHeat[index] / openWeight);
            }
            for (int y = 0; y < verticalLayers; y++)
            for (int z = 0; z < configuredHeight; z++)
            for (int x = 0; x < configuredWidth; x++)
            {
                int index = Index(x, y, z);
                if (solid[index]) { nextHeat[index] = 0f; continue; }
                float current = heat[index];
                float neighbourDelta = NeighbourDelta(index, current);
                float cooled = current * cooling;
                // Each shared edge contributes the same opposite delta in its neighbour.
                // Divide by the six fixed faces instead of the available-neighbour count
                // so walls do not create or erase heat at an asymmetric boundary.
                float diffused = neighbourDelta * heatDiffusion * dt / DirectionCount;
                float received = IncomingHeat(index);
                nextHeat[index] = Mathf.Max(0f, cooled + diffused + SourceHeat(index) * dt - outgoingHeat[index] + received);
                TotalHeat += nextHeat[index];
                PeakVerticalVelocity = Mathf.Max(PeakVerticalVelocity, nextFlow[index].y);
            }
            (heat, nextHeat) = (nextHeat, heat);
            (flow, nextFlow) = (nextFlow, flow);
            StepCount++;
        }

        float SourceHeat(int index)
        {
            int cellsPerLayer = configuredWidth * configuredHeight;
            if (index >= cellsPerLayer) return 0f;
            int z = index / configuredWidth;
            int x = index - z * configuredWidth;
            return field.GetCell(x, z).Intensity * sourceHeatPerSecond;
        }

        float NeighbourDelta(int index, float current)
        {
            float delta = 0f;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                int neighbour = NeighbourIndex(index, direction);
                if (neighbour < 0 || !IsOpen(index, direction)) continue;
                delta += heat[neighbour] - current;
            }
            return delta;
        }

        float IncomingHeat(int index)
        {
            float received = 0f;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                int neighbour = NeighbourIndex(index, direction);
                if (neighbour < 0 || outgoingHeat[neighbour] <= 0f) continue;
                int fromNeighbour = Opposite(direction);
                if (!IsOpen(neighbour, fromNeighbour)) continue;
                received += DirectionalWeight(transportedHeat[neighbour], fromNeighbour);
            }
            return received;
        }

        static float DirectionalWeight(Vector3 value, int direction)
        {
            Vector3Int offset = NeighbourOffsets[direction];
            return Mathf.Max(0f, value.x * offset.x + value.y * offset.y + value.z * offset.z);
        }

        Vector3 ConstrainFlow(int index, Vector3 velocity)
        {
            if (velocity.x < 0f && !IsOpen(index, 0)) velocity.x = 0f;
            if (velocity.x > 0f && !IsOpen(index, 1)) velocity.x = 0f;
            if (velocity.y < 0f && !IsOpen(index, 2)) velocity.y = 0f;
            if (velocity.y > 0f && !IsOpen(index, 3)) velocity.y = 0f;
            if (velocity.z < 0f && !IsOpen(index, 4)) velocity.z = 0f;
            if (velocity.z > 0f && !IsOpen(index, 5)) velocity.z = 0f;
            return velocity;
        }

        int NeighbourIndex(int index, int direction)
        {
            int plane = configuredWidth * configuredHeight;
            int y = index / plane;
            int withinPlane = index - y * plane;
            int z = withinPlane / configuredWidth;
            int x = withinPlane - z * configuredWidth;
            Vector3Int offset = NeighbourOffsets[direction];
            return InBounds(x + offset.x, y + offset.y, z + offset.z) ? Index(x + offset.x, y + offset.y, z + offset.z) : -1;
        }

        bool IsOpen(int index, int direction) => openEdges[index * DirectionCount + direction] != 0;
        static int Opposite(int direction) => direction ^ 1;

        bool IsOccupied(Vector3 center)
        {
            Vector3 half = new Vector3(field.CellSize * occupancyProbeFraction, layerHeight * occupancyProbeFraction, field.CellSize * occupancyProbeFraction);
            int count = Physics.OverlapBoxNonAlloc(center, half, overlapHits, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!overlapHits[i].isTrigger && IsObstacle(overlapHits[i])) return true;
            return false;
        }

        bool IsSegmentClear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= .0001f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, rayHits, distance, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!rayHits[i].collider.isTrigger && IsObstacle(rayHits[i].collider)) return false;
            return true;
        }

        bool IsObstacle(Collider collider) => obstacleSet.Count == 0 || obstacleSet.Contains(collider);

        void BuildObstacleSet()
        {
            obstacleSet.Clear();
            for (int i = 0; i < obstacleColliders.Length; i++) if (obstacleColliders[i]) obstacleSet.Add(obstacleColliders[i]);
            for (int i = 0; i < doors.Length; i++) if (doors[i] && doors[i].BlockingCollider) obstacleSet.Add(doors[i].BlockingCollider);
        }

        void SubscribeDoors()
        {
            if (subscribed) return;
            for (int i = 0; i < doors.Length; i++) if (doors[i]) doors[i].StateChanged += DoorStateChanged;
            subscribed = true;
        }

        void UnsubscribeDoors()
        {
            if (!subscribed) return;
            for (int i = 0; i < doors.Length; i++) if (doors[i]) doors[i].StateChanged -= DoorStateChanged;
            subscribed = false;
        }

        void DoorStateChanged(BreakableDoor _) => RebuildSolidMask();

        bool TryGetIndex(Vector3 world, out int index)
        {
            index = -1;
            if (!field || configuredWidth == 0 || !field.TryGetCell(world, out Vector2Int cell)) return false;
            int y = Mathf.FloorToInt((world.y - field.Origin.y) / layerHeight);
            if (!InBounds(cell.x, y, cell.y)) return false;
            index = Index(cell.x, y, cell.y);
            return true;
        }

        Vector3 CellCenter(int x, int y, int z)
        {
            Vector3 floor = field.CellCenter(x, z);
            return new Vector3(floor.x, field.Origin.y + (y + .5f) * layerHeight, floor.z);
        }

        bool InBounds(int x, int y, int z)
            => x >= 0 && x < configuredWidth && y >= 0 && y < verticalLayers && z >= 0 && z < configuredHeight;

        int Index(int x, int y, int z) => (y * configuredHeight + z) * configuredWidth + x;
    }
}
