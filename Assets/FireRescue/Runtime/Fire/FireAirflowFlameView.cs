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
        const int CurvedColumns = 5;
        [SerializeField] FireAirflowSolver airflow;
        [SerializeField] FireAirflowSmokeView smoke;
        [SerializeField, Min(1)] int maximumFlames = 32;
        [SerializeField, Min(.01f)] float minimumIntensity = .051f;
        [SerializeField] bool automaticTick = true;
        [SerializeField] bool sculptedTongues;
        [SerializeField] bool redGroundFire;

        public void UseRedGroundPalette() => redGroundFire = true;

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
            Camera view = Camera.main;
            Vector3 screenRight = view ? Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized : Vector3.right;
            if (screenRight.sqrMagnitude < .0001f) screenRight = Vector3.right;
            EnsureCellOrder(field.Width * field.Height);
            foreach (int index in cellOrder)
            {
                if (next == flames.Length) break;
                int x = index % field.Width;
                int z = index / field.Width;
                FireCellState state = field.GetCell(x, z);
                if (state.Intensity <= minimumIntensity) continue;
                float phase = x * 1.618f + z * 2.414f;
                UpdateFlame(flames[next], field.CellCenter(x, z), state.Intensity, index, phase, screenRight);
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
                if (redGroundFire) material.SetFloat("_FirePalette", 1f);
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
            return new Flame(root, renderer, mesh, sculptedTongues ? CurvedColumns : 2);
        }

        void UpdateFlame(Flame flame, Vector3 floor, float intensity, int index, float phase, Vector3 screenRight)
        {
            if (!flame.Root.activeSelf) flame.Root.SetActive(true);
            float scale = Mathf.Sqrt(Mathf.Clamp01(intensity));
            float growth = Mathf.SmoothStep(0f, 1f, (intensity - minimumIntensity) / .18f);
            float height = Mathf.Lerp(.32f, 1.12f, scale) * growth;
            float width = Mathf.Lerp(.11f, .28f, scale) * growth;
            Vector3 rootPosition = floor + Vector3.up * .015f;
            Vector3 awayFromCamera = Vector3.Cross(screenRight, Vector3.up);
            if (sculptedTongues)
            {
                int hash = FireSurface.CellHash(index + 2713);
                height *= Mathf.Lerp(.83f, 1.18f, (hash & 255) / 255f);
                width *= Mathf.Lerp(.88f, 1.13f, ((hash >> 8) & 255) / 255f);
                flame.Root.transform.SetPositionAndRotation(rootPosition, Quaternion.LookRotation(awayFromCamera));
            }
            else flame.Root.transform.position = rootPosition;

            WriteFlameSurface(flame, floor, height, width, phase, screenRight, awayFromCamera);
            float boost = sculptedTongues ? Mathf.InverseLerp(.48f, .90f, scale) : (scale > .72f ? 1f : 0f);
            FireSurface.SetPhase(flame.Renderer, properties, phase, boost);
        }

        void WriteFlameSurface(Flame flame, Vector3 floor, float height, float width, float phase,
            Vector3 screenRight, Vector3 awayFromCamera)
        {
            Vector3[] vertices = flame.Vertices;
            Vector3[] normals = flame.Normals;
            int columns = vertices.Length / Rows;
            float previousHeight = 0f;
            Vector3 bend = Vector3.zero;
            for (int row = 0; row < Rows; row++)
            {
                float t = row / (float)(Rows - 1);
                float sampleHeight = height * t;
                Vector3 flow = airflow.SampleFlow(floor + Vector3.up * sampleHeight);
                // Integrate horizontal airflow in both scene axes as the flame rises.
                float rise = sculptedTongues ? sampleHeight - previousHeight : Mathf.Max(.02f, sampleHeight - previousHeight);
                bend += Vector3.ProjectOnPlane(flow, Vector3.up) * (rise * .48f);
                previousHeight = sampleHeight;
                float halfWidth = width * Mathf.Lerp(.78f, 1.08f, Mathf.Sin(t * Mathf.PI));
                float sideBend = Vector3.Dot(bend, screenRight);
                float relief = 0f;
                float depthBend = 0f;
                if (sculptedTongues)
                {
                    relief = .58f * (1f - .60f * t) * halfWidth;
                    sideBend += Mathf.Sin(time * 4.1f + phase + t * 5f) * t * t * height * .085f;
                    depthBend = Vector3.Dot(bend, awayFromCamera);
                }
                for (int column = 0; column < columns; column++)
                {
                    float across = column * (2f / (columns - 1)) - 1f;
                    int index = row * columns + column;
                    vertices[index] = new Vector3(sideBend + across * halfWidth, sampleHeight,
                        depthBend - relief * (1f - across * across));
                    normals[index] = sculptedTongues ? new Vector3(across * (1.16f - .69f * t), .18f * t, -1f).normalized
                        : Vector3.forward;
                }
            }
            flame.Mesh.SetVertices(vertices);
            flame.Mesh.SetNormals(normals);
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
            public Flame(GameObject root, MeshRenderer renderer, Mesh mesh, int columns)
            {
                Root = root;
                Renderer = renderer;
                Mesh = mesh;
                Vertices = new Vector3[Rows * columns];
                Normals = new Vector3[Vertices.Length];
                var uvs = new Vector4[Vertices.Length];
                var triangles = new int[(Rows - 1) * (columns - 1) * 6];
                int triangle = 0;
                for (int row = 0; row < Rows; row++)
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * columns + column;
                        uvs[i] = new Vector4(column / (float)(columns - 1), row / (float)(Rows - 1), columns > 2 ? 2f : 0f, 0f);
                        if (row == Rows - 1 || column == columns - 1) continue;
                        triangles[triangle++] = i;
                        triangles[triangle++] = i + columns;
                        triangles[triangle++] = i + 1;
                        triangles[triangle++] = i + 1;
                        triangles[triangle++] = i + columns;
                        triangles[triangle++] = i + columns + 1;
                    }
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, false);
            }
        }
    }
}
