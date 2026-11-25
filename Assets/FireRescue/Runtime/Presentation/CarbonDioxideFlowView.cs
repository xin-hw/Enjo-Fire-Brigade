using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // Tapered streamlines follow already simulated packets, including after release.
    public sealed class CarbonDioxideFlowView : MonoBehaviour
    {
        const int Rows = 9;
        const int VerticesPerLine = Rows * 2;
        [SerializeField] SprayEmitter emitter;
        [SerializeField] Camera view;
        readonly List<Vector3> vertices = new(SprayEmitter.ParticleCapacity * VerticesPerLine);
        readonly List<Color32> colours = new(SprayEmitter.ParticleCapacity * VerticesPerLine);
        readonly List<int> indices = new(SprayEmitter.ParticleCapacity * (Rows - 1) * 6);
        Mesh mesh;
        Material material;
        MeshRenderer meshView;

        public int VisibleLines { get; private set; }
        public void Initialize(SprayEmitter spray, Camera camera) { emitter = spray; view = camera; }

        void Awake()
        {
            mesh = new Mesh { name = "Carbon dioxide streamlines" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshView = gameObject.AddComponent<MeshRenderer>();
            material = new Material(Shader.Find("Sprites/Default")) { name = "Pressurized gas line", renderQueue = 3024 };
            meshView.sharedMaterial = material;
            meshView.shadowCastingMode = ShadowCastingMode.Off;
            meshView.receiveShadows = false;
            meshView.enabled = false;
        }

        void LateUpdate()
        {
            vertices.Clear(); colours.Clear(); indices.Clear();
            Vector3 forward = view.transform.forward;
            Matrix4x4 local = transform.worldToLocalMatrix;
            for (int i = 0; i < emitter.ActiveParticleCount; i++)
            {
                var flight = emitter.GetParticle(i);
                if (flight.Element != ElementKind.CarbonDioxide) continue;
                Vector3 axis = flight.Position - flight.TrailStart;
                if (axis.sqrMagnitude < .0025f) continue;
                Vector3 side = Vector3.Cross(axis, forward).normalized;
                float style = SprayEmitter.SampleVariation(flight.Seed, 71);
                float width = Mathf.Lerp(.017f, .034f, style);
                float bend = Mathf.Min(axis.magnitude * .20f, .20f) * (style < .45f ? -1f : 1f);
                Vector3 trackCurve = flight.TrailMiddle - (flight.TrailStart + flight.Position) * .5f;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.62f, 1f, flight.NormalizedAge));
                int start = vertices.Count;
                for (int row = 0; row < Rows; row++)
                {
                    float t = row / (float)(Rows - 1);
                    Vector3 center = Curve(t);
                    Vector3 tangent = Curve(Mathf.Min(1f, t + .03f)) - Curve(Mathf.Max(0f, t - .03f));
                    float belly = Mathf.Sin(Mathf.PI * t);
                    Vector3 offset = Vector3.Cross(tangent, forward).normalized * (width * Mathf.Lerp(.12f, 1f, belly));
                    var tint = Color32.Lerp(new Color32(88,148,168,224), new Color32(179,217,223,194), t);
                    tint.a = (byte)(tint.a * fade * belly);
                    vertices.Add(local.MultiplyPoint3x4(center - offset));
                    vertices.Add(local.MultiplyPoint3x4(center + offset));
                    colours.Add(tint); colours.Add(tint);
                    if (row == Rows - 1) continue;
                    int edge = start + row * 2;
                    indices.Add(edge); indices.Add(edge + 1); indices.Add(edge + 2);
                    indices.Add(edge + 1); indices.Add(edge + 3); indices.Add(edge + 2);
                }

                Vector3 Curve(float t) => flight.TrailStart + axis * t + trackCurve * (4f * t * (1f - t)) +
                    side * (bend * (Mathf.Sin(t * Mathf.PI) + .42f * Mathf.Sin(t * Mathf.PI * 2f)));
            }
            VisibleLines = vertices.Count / VerticesPerLine;
            meshView.enabled = VisibleLines != 0;
            if (!meshView.enabled) return;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(indices, 0);
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
