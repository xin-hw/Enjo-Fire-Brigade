using UnityEngine;

namespace FireRescue
{
    // Only the separate trial scene opts into this deposit-to-fire route.
    [RequireComponent(typeof(ElementReaction))]
    public sealed class WaterFireLab : MonoBehaviour
    {
        [SerializeField] FireField field;
        [SerializeField] Collider oilPan;
        [SerializeField] WaterSteamView steam;
        [SerializeField] Vector3 smallFire, largeFire;

        public void Initialize(FireField floorFire, Collider pan, WaterSteamView view, Vector3 small, Vector3 large)
        {
            field = floorFire;
            oilPan = pan;
            steam = view;
            smallFire = small;
            largeFire = large;
        }

        void Awake()
        {
            var reaction = GetComponent<ElementReaction>();
            reaction.Initialize(field);
            reaction.UsePhysicalWaterDeposits();
        }

        void Start()
        {
            field.Ignite(smallFire, .40f);
            field.Ignite(largeFire, .95f);
        }

        internal bool IsOilPan(Collider hit) => hit == oilPan;
        internal FireField Field => field;
        internal WaterSteamView Steam => steam;

    }
}
