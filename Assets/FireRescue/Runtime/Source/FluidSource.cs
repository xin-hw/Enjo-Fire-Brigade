using UnityEngine;

namespace FireRescue
{
    public sealed class FluidSource : MonoBehaviour
    {
        const float RefillDelay = .45f;

        [SerializeField] ElementKind element;
        [SerializeField, Min(.1f)] float flowRate = 2;
        [SerializeField] Transform outlet;
        [SerializeField] Transform interactionPoint;
        [SerializeField] SprayProfile sprayProfile;
        [SerializeField, Min(0f), Tooltip("Zero leaves this source unlimited.")] float capacity;
        [SerializeField, Min(0f)] float refillPerSecond;
        float refillWait;

        public ElementKind Element => element;
        public float FlowRate => flowRate;
        public SprayProfile Profile => sprayProfile;
        public SpraySettings SpraySettings => sprayProfile ? sprayProfile.Settings : FireRescue.SpraySettings.DefaultFor(element);
        public SprayEffectSettings SprayEffects => sprayProfile ? sprayProfile.Effects : SprayEffectSettings.Neutral;
        public void SetSprayProfile(SprayProfile profile) => sprayProfile = profile;
        public Vector3 Port => outlet ? outlet.position : transform.position + Vector3.up * .6f;
        public Vector3 InteractionPosition => interactionPoint ? interactionPoint.position : transform.position + Vector3.up * .35f;
        public void Configure(ElementKind kind, float rate = 2) { element = kind; flowRate = rate; }
        public void SetOutlet(Transform port) => outlet = port;
        public void SetInteractionPoint(Transform point) => interactionPoint = point;
        public void ConfigureCapacity(float amount, float refill = 0f)
        {
            capacity = amount;
            refillPerSecond = refill;
            SetBudget(amount > 0f && float.IsFinite(amount) ? amount : double.PositiveInfinity);
        }

        public double Capacity { get; private set; } = double.PositiveInfinity;
        public double RemainingAmount { get; private set; } = double.PositiveInfinity;
        public bool HasCapacity => double.IsFinite(Capacity);
        public float RemainingFraction => HasCapacity ? (float)(RemainingAmount / Capacity) : 1f;
        public bool CanRefill => HasCapacity && float.IsFinite(refillPerSecond) && refillPerSecond > 0f;
        public bool IsRefilling { get; private set; }
        internal bool AutomaticRefill { get; set; } = true;
        internal double DrawnAmount { get; private set; }
        internal double RefilledAmount { get; private set; }
        internal double WaterDrawn => element == ElementKind.Water ? DrawnAmount : 0d;

        void Awake()
        {
            if (float.IsFinite(capacity) && capacity > 0f) SetBudget(capacity);
        }

        void Update()
        {
            if (AutomaticRefill) TickRefill(Time.deltaTime);
        }

        internal void SetBudget(double amount)
        {
            Capacity = RemainingAmount = amount;
            DrawnAmount = RefilledAmount = 0d;
            refillWait = 0f;
            IsRefilling = false;
        }

        internal void TickRefill(float deltaTime)
        {
            IsRefilling = false;
            if (!isActiveAndEnabled || !CanRefill || RemainingAmount >= Capacity ||
                !float.IsFinite(deltaTime) || deltaTime <= 0f) return;

            float activeTime = Mathf.Max(0f, deltaTime - refillWait);
            refillWait = Mathf.Max(0f, refillWait - deltaTime);
            double previous = RemainingAmount;
            RemainingAmount = System.Math.Min(Capacity, previous + (double)refillPerSecond * activeTime);
            RefilledAmount += RemainingAmount - previous;
            IsRefilling = RemainingAmount > previous && RemainingAmount < Capacity;
        }

        internal float Draw(float requested)
        {
            if (!isActiveAndEnabled || !float.IsFinite(requested) || requested <= 0f) return 0f;
            // Held input keeps the valve open even when the tank is empty. Refilling starts on release.
            if (CanRefill)
            {
                refillWait = RefillDelay;
                IsRefilling = false;
            }
            float drawn = (float)System.Math.Min(requested, RemainingAmount);
            RemainingAmount = System.Math.Max(0d, RemainingAmount - drawn);
            DrawnAmount += drawn;
            return drawn;
        }
    }
}
