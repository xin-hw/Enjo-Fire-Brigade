using UnityEngine;

namespace FireRescue
{
    public sealed class FluidSource : MonoBehaviour
    {
        [SerializeField] ElementKind element;
        [SerializeField, Min(.1f)] float flowRate = 2;
        [SerializeField] Transform outlet;
        [SerializeField] Transform interactionPoint;
        [SerializeField] SprayProfile sprayProfile;
        double remainingWater = double.PositiveInfinity;
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

        internal double RemainingWater => remainingWater;
        internal double WaterDrawn { get; private set; }
        internal void SetWaterBudget(double amount) => remainingWater = amount;

        internal float DrawWater(float requested)
        {
            float drawn = (float)System.Math.Min(requested, remainingWater);
            remainingWater = System.Math.Max(0d, remainingWater - drawn);
            WaterDrawn += drawn;
            return drawn;
        }
    }
}
