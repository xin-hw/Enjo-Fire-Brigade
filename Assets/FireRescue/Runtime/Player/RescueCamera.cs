using UnityEngine;

namespace FireRescue
{
    [RequireComponent(typeof(Camera))]
    public sealed class RescueCamera : MonoBehaviour
    {
        static readonly int CutawayPlayer = Shader.PropertyToID("_CutawayPlayer");
        static readonly int CutawayView = Shader.PropertyToID("_CutawayView");

        [SerializeField] Transform target;
        // Legacy scenes retain 50/40; individual scenes can align the view without rotating gameplay.
        [SerializeField] Vector2 viewAngles = new Vector2(50, 40);
        [SerializeField] bool fixedView;
        [SerializeField] Renderer cutawayWall;
        Vector3 focus;
        Vector3 velocity;
        Camera view;
        MaterialPropertyBlock wallProperties;
        public void Initialize(Transform player) { target = player; Snap(); }
        public void ConfigureView(float pitch, float yaw)
        { viewAngles = new Vector2(Mathf.Clamp(pitch, 10, 90), yaw); Snap(); }
        void Awake()
        {
            view = GetComponent<Camera>();
            Snap();
        }
        void Snap()
        {
            if (fixedView) return;
            if (target) focus = target.position + Vector3.up * .7f;
            Apply();
        }
        void LateUpdate()
        {
            if (fixedView || !target) return;
            focus = Vector3.SmoothDamp(focus, target.position + Vector3.up * .7f, ref velocity, .18f);
            Apply();
        }

        void OnPreCull()
        {
            if (!cutawayWall) return;
            wallProperties ??= new MaterialPropertyBlock();
            cutawayWall.GetPropertyBlock(wallProperties);
            Vector3 feet = target ? target.position : Vector3.zero;
            wallProperties.SetVector(CutawayPlayer, new Vector4(feet.x, feet.y, feet.z, target ? 1f : 0f));
            wallProperties.SetVector(CutawayView, view.worldToCameraMatrix.GetRow(2));
            cutawayWall.SetPropertyBlock(wallProperties);
        }

        void OnDisable()
        {
            if (!cutawayWall || wallProperties == null) return;
            cutawayWall.GetPropertyBlock(wallProperties);
            wallProperties.SetVector(CutawayPlayer, Vector4.zero);
            cutawayWall.SetPropertyBlock(wallProperties);
        }

        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(viewAngles.x, viewAngles.y, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 22, rotation);
        }
    }
}
