using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    /// <summary>
    /// The fire silhouette bends from FireAirflowSolver samples and does not
    /// write to FireField. A fixed view pool avoids particle spawning and keeps the rendered
    /// shape traceable to the simulated heat/flow field.
    /// </summary>
    [RequireComponent(typeof(FireAirflowSolver))]
    public sealed class FireAirflowFlameView : MonoBehaviour
    {
        const int Rows = 7;
        [SerializeField] FireAirflowSolver airflow;
        [SerializeField] FireAirflowSmokeView smoke;
        [SerializeField, Min(1)] int maximumFlames = 32;
        [SerializeField, Min(.01f)] float minimumIntensity = .051f;
        [SerializeField] bool automaticTick = true;

        Flame[] flames = Array.Empty<Flame>();
        int[] cellOrder = Array.Empty<int>();
        Material material;
        MaterialPropertyBlock properties;
        float time;

        public int ActiveFlameCount { get; private set; }
        public bool AutomaticTick { get => automaticTick; set => automaticTick = value; }

        public void Initialize(FireAirflowSolver solver)
        {
            airflow = solver;
            EnsurePresentation();
            smoke.Initialize(airflow);
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
            time += Mathf.Min(deltaTime, .1f);
            material.SetFloat(FireSurface.Clock, time);
            ActiveFlameCount = 0;
            int next = 0;
            FireField field = airflow.Field;
            EnsureCellOrder(field.Width * field.Height);
            foreach (int index in cellOrder)
            {
                if (next == flames.Length) break;
                int x = index % field.Width;
                int z = index / field.Width;
                FireCellState state = field.GetCell(x, z);
                if (state.Intensity <= minimumIntensity) continue;
                UpdateFlame(flames[next], field.CellCenter(x, z), state.Intensity, x, z);
                next++;
            }
            ActiveFlameCount = next;
            for (; next < flames.Length; next++)
                if (flames[next].Root.activeSelf) flames[next].Root.SetActive(false);
        }

        void EnsureCellOrder(int cellCount)
        {
            if (cellOrder.Length == cellCount) return;
            // A fixed ranking spreads the pool around the room. One new burning
            // cell can replace at most one existing flame.
            cellOrder = new int[cellCount];
            for (int i = 0; i < cellOrder.Length; i++) cellOrder[i] = i;
            Array.Sort(cellOrder, static (a, b) =>
            {
                int order = FireSurface.CellHash(a).CompareTo(FireSurface.CellHash(b));
                return order != 0 ? order : a.CompareTo(b);
            });
        }

        void EnsurePresentation()
        {
            if (flames.Length == 0)
            {
                maximumFlames = Mathf.Max(1, maximumFlames);
                material = FireSurface.CreateMaterial();
                material.name = "Airflow study flame material";
                properties = new MaterialPropertyBlock();
                flames = new Flame[maximumFlames];
                for (int i = 0; i < flames.Length; i++) flames[i] = CreateFlame(i);
            }
            if (!smoke) smoke = GetComponent<FireAirflowSmokeView>();
            if (!smoke) smoke = gameObject.AddComponent<FireAirflowSmokeView>();
            if (airflow) smoke.Initialize(airflow);
        }

        Flame CreateFlame(int index)
        {
            var root = new GameObject("Airflow flame " + index) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var mesh = new Mesh { name = "Airflow driven fire tongue", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            root.SetActive(false);
            return new Flame(root, renderer, mesh);
        }

        void UpdateFlame(Flame flame, Vector3 floor, float intensity, int x, int z)
        {
            if (!flame.Root.activeSelf) flame.Root.SetActive(true);
            float scale = Mathf.Sqrt(Mathf.Clamp01(intensity));
            float growth = Mathf.SmoothStep(0f, 1f, (intensity - minimumIntensity) / .18f);
            float height = Mathf.Lerp(.32f, 1.12f, scale) * growth;
            float width = Mathf.Lerp(.11f, .28f, scale) * growth;
            flame.Root.transform.position = floor + Vector3.up * .015f;
            WriteRibbon(flame, airflow, floor, intensity, height, width);
            FireSurface.SetPhase(flame.Renderer, properties, x * 1.618f + z * 2.414f, scale > .72f ? 1f : 0f);
        }

        static void WriteRibbon(Flame flame, FireAirflowSolver solver, Vector3 floor,
            float intensity, float height, float width)
        {
            Vector3[] vertices = flame.Vertices;
            Vector3[] normals = flame.Normals;
            Vector4[] uvs = flame.Uvs;
            int[] triangles = flame.Triangles;
            Camera view = Camera.main;
            Vector3 screenRight = view
                ? Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized
                : Vector3.right;
            float previousHeight = 0f;
            float accumulatedBend = 0f;
            int triangle = 0;
            for (int row = 0; row < Rows; row++)
            {
                float t = row / (float)(Rows - 1);
                float sampleHeight = height * t;
                Vector3 flow = solver.SampleFlow(floor + Vector3.up * sampleHeight);
                if (row == 0) flow += Vector3.up * Mathf.Clamp01(intensity) * .32f;
                float rise = Mathf.Max(.02f, sampleHeight - previousHeight);
                // Each horizontal slice reads the velocity at its actual height. The
                // ribbon bends along an integrated path through the local flow field.
                accumulatedBend += Vector3.Dot(Vector3.ProjectOnPlane(flow, Vector3.up), screenRight) *
                    rise * .48f;
                previousHeight = sampleHeight;
                // The flame mask already tapers toward varying tip heights.
                float halfWidth = width * Mathf.Lerp(.78f, 1.08f, Mathf.Sin(t * Mathf.PI));
                for (int side = 0; side < 2; side++)
                {
                    int index = row * 2 + side;
                    float sign = side == 0 ? -1f : 1f;
                    vertices[index] = new Vector3(accumulatedBend + sign * halfWidth, sampleHeight, 0f);
                    normals[index] = Vector3.forward;
                    uvs[index] = new Vector4(side, t, 0f, 0f);
                }
                if (row == Rows - 1) continue;
                int start = row * 2;
                triangles[triangle++] = start;
                triangles[triangle++] = start + 2;
                triangles[triangle++] = start + 1;
                triangles[triangle++] = start + 1;
                triangles[triangle++] = start + 2;
                triangles[triangle++] = start + 3;
            }
            flame.Mesh.Clear();
            flame.Mesh.vertices = vertices;
            flame.Mesh.normals = normals;
            flame.Mesh.SetUVs(0, uvs);
            flame.Mesh.triangles = triangles;
            flame.Mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            for (int i = 0; i < flames.Length; i++)
                if (flames[i].Mesh) Destroy(flames[i].Mesh);
            if (material) Destroy(material);
        }

        readonly struct Flame
        {
            public readonly GameObject Root;
            public readonly MeshRenderer Renderer;
            public readonly Mesh Mesh;
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly Vector4[] Uvs;
            public readonly int[] Triangles;
            public Flame(GameObject root, MeshRenderer renderer, Mesh mesh)
            {
                Root = root;
                Renderer = renderer;
                Mesh = mesh;
                Vertices = new Vector3[Rows * 2];
                Normals = new Vector3[Rows * 2];
                Uvs = new Vector4[Rows * 2];
                Triangles = new int[(Rows - 1) * 6];
            }
        }
    }
}
