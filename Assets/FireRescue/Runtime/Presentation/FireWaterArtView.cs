using UnityEngine;

namespace FireRescue
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(WorldOutlineView))]
    public sealed class FireWaterArtView : MonoBehaviour
    {
        static readonly int ColorBlocks = Shader.PropertyToID("_ColorBlocks");
        static readonly int PaintedDetail = Shader.PropertyToID("_PaintedDetail");

        [SerializeField] LocalPlayerInput input;
        [SerializeField] FluidSource[] sources;
        [SerializeField] WaterLab water;
        [SerializeField] Transform[] fires;
        [SerializeField] Transform resident;
        [SerializeField] bool enableResourceFocus;
        WorldOutlineView outlines;
        HoseConnection hose;
        float resourceFocusProgress;

        public FluidSource Focus => outlines ? outlines.Focus : null;

        public void Initialize(LocalPlayerInput player, FluidSource[] fluidSources, WaterLab waterLab, Transform[] fireRoots,
            Transform residentModel = null)
        {
            input = player;
            sources = fluidSources;
            water = waterLab;
            fires = fireRoots;
            resident = residentModel;
        }

        void Start() => Apply();

        internal void Apply()
        {
            if (outlines || !input) return;
            hose = input.GetComponent<HoseConnection>();
            outlines = GetComponent<WorldOutlineView>();
            outlines.AddOpaque(input.transform);
            if (resident) outlines.AddOpaque(resident);
            foreach (var source in sources)
                if (source) outlines.AddOpaque(source.transform, source);
            Paint(water ? water.PresentationRoot : null);
            foreach (var root in fires) Paint(root);
        }

        void Paint(Transform root)
        {
            if (!root) return;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (!material || !material.HasProperty(ColorBlocks)) continue;
                material.SetFloat(ColorBlocks, 1f);
                if (material.HasProperty(PaintedDetail)) material.SetFloat(PaintedDetail, 1f);
            }
            outlines.AddEffect(root);
        }

        void LateUpdate()
        {
            if (!outlines) return;
            FluidSource source = input.NearbySource;
            outlines.Focus = hose && hose.IsConnected && source != hose.Source ? null : source;
            if (enableResourceFocus && input.ReadDevices)
                AdvanceResourceFocus(input.ResourceFocusHeld, Time.unscaledDeltaTime);
        }

        internal void AdvanceResourceFocus(bool held, float deltaTime)
        {
            if (!enableResourceFocus || !outlines) return;
            float duration = held ? .40f : .48f;
            resourceFocusProgress = Mathf.MoveTowards(resourceFocusProgress, held ? 1f : 0f, deltaTime / duration);
            outlines.ResourceFocusAmount = Mathf.SmoothStep(0f, 1f, resourceFocusProgress);
        }

        void OnDisable()
        {
            resourceFocusProgress = 0f;
            if (outlines) outlines.ResourceFocusAmount = 0f;
        }
    }
}
