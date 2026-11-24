using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    /// <summary>
    /// Small fixed-pool smoke wisps for the airflow lab. Each wisp is emitted from a currently
    /// burning FireField cell and advances from FireAirflowSolver velocity samples. It has no
    /// gameplay authority and stays separate from water-contact steam.
    /// </summary>
    [RequireComponent(typeof(FireAirflowSolver))]
    public sealed class FireAirflowSmokeView : MonoBehaviour
    {
        [SerializeField] FireAirflowSolver airflow;
        [SerializeField, Range(1, 24)] int maximumWisps = 12;
        [SerializeField, Min(.1f)] float lifetime = 1.55f;
        [SerializeField] bool automaticTick = true;

        Wisp[] wisps = Array.Empty<Wisp>();
        Material material;
        MaterialPropertyBlock properties;
        Texture2D texture;
        int nextSource;

        public int ActiveWispCount { get; private set; }
        public bool AutomaticTick { get => automaticTick; set => automaticTick = value; }

        public void Initialize(FireAirflowSolver solver)
        {
            airflow = solver;
            EnsurePresentation();
        }

        void Awake()
        {
            if (!airflow) airflow = GetComponent<FireAirflowSolver>();
            EnsurePresentation();
        }

        void LateUpdate()
        {
            if (automaticTick) TickPresentation(Time.deltaTime);
        }

        public void TickPresentation(float deltaTime)
        {
            if (!airflow || !airflow.Field || !float.IsFinite(deltaTime) || deltaTime < 0f) return;
            EnsurePresentation();
            ActiveWispCount = 0;
            for (int i = 0; i < wisps.Length; i++)
            {
                ref Wisp wisp = ref wisps[i];
                if (wisp.Age >= lifetime || airflow.SampleHeat(wisp.Position) < .012f)
                    Respawn(ref wisp, i);
                if (wisp.Age < 0f)
                {
                    if (wisp.Root.activeSelf) wisp.Root.SetActive(false);
                    continue;
                }

                Vector3 flow = airflow.SampleFlow(wisp.Position);
                wisp.Velocity = Vector3.Lerp(wisp.Velocity, flow + Vector3.up * .12f,
                    1f - Mathf.Exp(-5f * deltaTime));
                Vector3 next = wisp.Position + wisp.Velocity * deltaTime;
                if (airflow.IsOpenAirSegment(wisp.Position, next))
                    wisp.Position = next;
                else
                    wisp.Velocity = Vector3.zero;
                wisp.Age += deltaTime;
                float progress = Mathf.Clamp01(wisp.Age / lifetime);
                float alpha = Mathf.Sin(progress * Mathf.PI) * .26f;
                UpdateWisp(wisp, progress, alpha);
                ActiveWispCount++;
            }
        }

        void EnsurePresentation()
        {
            if (wisps.Length > 0) return;
            maximumWisps = Mathf.Clamp(maximumWisps, 1, 24);
            texture = CreateSoftTexture();
            material = new Material(Shader.Find("Sprites/Default")) { name = "Airflow study smoke material", mainTexture = texture };
            properties = new MaterialPropertyBlock();
            wisps = new Wisp[maximumWisps];
            for (int i = 0; i < wisps.Length; i++)
            {
                var root = GameObject.CreatePrimitive(PrimitiveType.Quad);
                root.name = "Airflow smoke " + i;
                root.hideFlags = HideFlags.DontSave;
                root.transform.SetParent(transform, false);
                var collider = root.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                var renderer = root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                root.SetActive(false);
                wisps[i] = new Wisp { Root = root, Renderer = renderer, Age = lifetime };
            }
        }

        void Respawn(ref Wisp wisp, int ordinal)
        {
            FireField field = airflow.Field;
            int capacity = field.Width * field.Height;
            for (int attempt = 0; attempt < capacity; attempt++)
            {
                int index = (nextSource++ + ordinal * 7) % capacity;
                int x = index % field.Width;
                int z = index / field.Width;
                FireCellState state = field.GetCell(x, z);
                if (!state.IsBurning) continue;
                float side = Mathf.Repeat(ordinal * .618034f, 1f) - .5f;
                wisp.Position = field.CellCenter(x, z) + new Vector3(side * .11f, .16f, -side * .08f);
                wisp.Velocity = airflow.GetFlameFlow(wisp.Position, state.Intensity) * .32f;
                wisp.Age = 0f;
                return;
            }
            wisp.Age = -1f;
        }

        void UpdateWisp(Wisp wisp, float progress, float alpha)
        {
            if (!wisp.Root.activeSelf) wisp.Root.SetActive(true);
            Camera view = Camera.main;
            if (view) wisp.Root.transform.rotation = Quaternion.LookRotation(view.transform.forward, view.transform.up);
            wisp.Root.transform.position = wisp.Position;
            float size = Mathf.Lerp(.11f, .34f, progress);
            wisp.Root.transform.localScale = new Vector3(size, size, 1f);
            properties.Clear();
            properties.SetColor("_Color", new Color(.17f, .16f, .15f, alpha));
            wisp.Renderer.SetPropertyBlock(properties);
        }

        static Texture2D CreateSoftTexture()
        {
            const int size = 32;
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Airflow smoke soft disc", hideFlags = HideFlags.DontSave };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f;
                float dy = (y + .5f) / size * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                result.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
            }
            result.Apply(false, true);
            return result;
        }

        void OnDestroy()
        {
            for (int i = 0; i < wisps.Length; i++)
                if (wisps[i].Root) Destroy(wisps[i].Root);
            if (material) Destroy(material);
            if (texture) Destroy(texture);
        }

        struct Wisp
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
        }
    }
}
