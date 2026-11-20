using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// Counts continuous contact between a door's physical footprint and real floor-fire cells.
    /// FireField remains authoritative for the fire state; this component owns only door exposure.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class DoorFireExposure : MonoBehaviour
    {
        [SerializeField] private FireField field;
        [SerializeField] private BreakableDoor door;
        [SerializeField, Min(.01f)] private float burnoutSeconds = 5f;
        [SerializeField, Min(0f)] private float surfaceReach = .08f;

        private Collider contactCollider;
        private bool wasBroken;

        public bool AutomaticTick { get; set; } = true;
        public float ExposureSeconds { get; private set; }
        public float BurnoutSeconds => burnoutSeconds;
        public bool IsExposed { get; private set; }

        /// <summary>Explicit scene/builder wiring; clears any old runtime exposure.</summary>
        public void Initialize(FireField fireField, BreakableDoor breakableDoor)
        {
            field = fireField;
            door = breakableDoor;
            contactCollider = door ? door.BlockingCollider : null;
            ExposureSeconds = 0f;
            IsExposed = false;
            wasBroken = door && door.IsBroken;
        }

        private void Awake()
        {
            if (!door) door = GetComponent<BreakableDoor>();
            if (!contactCollider && door) contactCollider = door.BlockingCollider;
        }

        private void OnDisable()
        {
            ExposureSeconds = 0f;
            IsExposed = false;
        }

        private void LateUpdate()
        {
            if (AutomaticTick) Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances environmental exposure. Any gap in actual floor-fire contact clears the
        /// timer, so five seconds means continuous burning rather than accumulated proximity.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            if (!isActiveAndEnabled)
            {
                ClearExposure();
                return;
            }
            if (!door || door.IsBroken)
            {
                ClearExposure();
                wasBroken |= door && door.IsBroken;
                return;
            }

            if (wasBroken)
            {
                // A runtime reset/reload can restore a door without recreating this component.
                wasBroken = false;
                ClearExposure();
            }

            if (!field || !field.isActiveAndEnabled || !contactCollider || !contactCollider.enabled || !HasBurningCellTouchingDoor())
            {
                ClearExposure();
                return;
            }

            IsExposed = true;
            ExposureSeconds = Mathf.Min(PositiveFiniteOrDefault(burnoutSeconds, 5f), ExposureSeconds + deltaTime);
            if (ExposureSeconds + .0001f < PositiveFiniteOrDefault(burnoutSeconds, 5f)) return;

            door.BurnDown();
            wasBroken = door.IsBroken;
            ClearExposure();
        }

        private bool HasBurningCellTouchingDoor()
        {
            Bounds bounds = contactCollider.bounds;
            float verticalGap = field.Origin.y < bounds.min.y
                ? bounds.min.y - field.Origin.y
                : field.Origin.y > bounds.max.y ? field.Origin.y - bounds.max.y : 0f;
            if (verticalGap > Mathf.Max(.12f, surfaceReach)) return false;

            // Test a cell's finite floor square against the collider footprint. This avoids a
            // broad radial query that could burn a door from a fire several cells behind a wall.
            float halfCell = field.CellSize * .5f;
            float minXWorld = bounds.min.x - surfaceReach - halfCell;
            float maxXWorld = bounds.max.x + surfaceReach + halfCell;
            float minZWorld = bounds.min.z - surfaceReach - halfCell;
            float maxZWorld = bounds.max.z + surfaceReach + halfCell;
            int minX = Mathf.Max(0, Mathf.FloorToInt((minXWorld - field.Origin.x) / field.CellSize));
            int maxX = Mathf.Min(field.Width - 1, Mathf.FloorToInt((maxXWorld - field.Origin.x) / field.CellSize));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((minZWorld - field.Origin.z) / field.CellSize));
            int maxZ = Mathf.Min(field.Height - 1, Mathf.FloorToInt((maxZWorld - field.Origin.z) / field.CellSize));
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!field.GetCell(x, z).IsBurning) continue;
                Vector3 center = field.CellCenter(x, z);
                float dx = Mathf.Max(0f, Mathf.Abs(center.x - bounds.center.x) - halfCell - bounds.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(center.z - bounds.center.z) - halfCell - bounds.extents.z);
                if (dx * dx + dz * dz <= surfaceReach * surfaceReach) return true;
            }

            return false;
        }

        private void ClearExposure()
        {
            ExposureSeconds = 0f;
            IsExposed = false;
        }

        private static float PositiveFiniteOrDefault(float value, float fallback) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
    }
}
