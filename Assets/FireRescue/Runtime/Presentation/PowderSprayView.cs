using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // Renders the actual flying powder and a short puff at its measured impact.
    // Material amount and all reactions belong to SprayEmitter.
    public sealed class PowderSprayView : MonoBehaviour
    {
        const int ContactCapacity = 18;
        const int LobesPerContact = 3;
        const int QuadCapacity = SprayEmitter.ParticleCapacity + ContactCapacity * LobesPerContact;
        const float ContactLifetime = .37f;

        [SerializeField] SprayEmitter emitter;
        [SerializeField] Camera view;
        readonly Contact[] contacts = new Contact[ContactCapacity];
        readonly List<Vector3> vertices = new(QuadCapacity * 4);
        readonly List<Vector4> uvs = new(QuadCapacity * 4);
        readonly List<int> triangles = new(QuadCapacity * 6);
        Mesh mesh;
        Material material;
        MeshRenderer meshView;
        int nextContact;
        uint contactSeed;

        public bool IsDrawing => isActiveAndEnabled && material;

        struct Contact
        {
            public Vector3 Position;
            public Vector3 Normal;
            public float Remaining;
            public uint Seed;
        }

        public void Initialize(SprayEmitter source, Camera camera)
        {
            emitter = source;
            view = camera;
        }

        void Awake()
        {
            var shader = Resources.Load<Shader>("ColorBlockPowder");
            if (!shader || !shader.isSupported) return;
            material = new Material(shader) { name = "Painted baking soda" };
            mesh = new Mesh { name = "Batched baking soda shapes" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshView = gameObject.AddComponent<MeshRenderer>();
            meshView.sharedMaterial = material;
            meshView.shadowCastingMode = ShadowCastingMode.Off;
            meshView.receiveShadows = false;
            meshView.enabled = false;
        }

        void OnEnable()
        {
            if (material && emitter) emitter.ParticleImpacted += OnContact;
        }

        void OnDisable()
        {
            if (emitter) emitter.ParticleImpacted -= OnContact;
            if (meshView) meshView.enabled = false;
            System.Array.Clear(contacts, 0, contacts.Length);
        }

        void OnContact(SprayEmitter.ParticleImpact impact)
        {
            if (impact.Element != ElementKind.DryPowder) return;
            Vector3 normal = impact.Normal.sqrMagnitude > .1f ? impact.Normal.normalized : Vector3.up;
            for (int i = 0; i < contacts.Length; i++)
            {
                ref var seen = ref contacts[i];
                if (seen.Remaining > ContactLifetime - .12f && Vector3.Dot(seen.Normal, normal) > .8f &&
                    (seen.Position - impact.Position).sqrMagnitude < .038f) return;
            }
            contacts[nextContact] = new Contact
                { Position = impact.Position, Normal = normal, Remaining = ContactLifetime, Seed = ++contactSeed };
            nextContact = (nextContact + 1) % contacts.Length;
        }

        void LateUpdate()
        {
            if (!material || !emitter || !view) return;
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
            var local = transform.worldToLocalMatrix;
            Vector3 cameraRight = view.transform.right;
            Vector3 cameraUp = view.transform.up;
            Vector3 cameraForward = view.transform.forward;
            for (int i = 0; i < emitter.ActiveParticleCount; i++)
            {
                var grain = emitter.GetParticle(i);
                if (grain.Element != ElementKind.DryPowder) continue;
                float variation = SprayEmitter.SampleVariation(grain.Seed, 139);
                Vector3 flight = Vector3.ProjectOnPlane(grain.Velocity, cameraForward);
                Vector3 longAxis = flight.sqrMagnitude > .001f ? flight.normalized : cameraRight;
                Vector3 side = Vector3.Cross(cameraForward, longAxis);
                float size = Mathf.Clamp(grain.Radius * (.64f + variation * .54f + grain.NormalizedAge * .2f), .037f, .15f);
                AddQuad(local, grain.Position, longAxis * size * 1.37f, side * size, variation);
            }
            for (int slot = 0; slot < contacts.Length; slot++)
            {
                ref var contact = ref contacts[slot];
                if (contact.Remaining <= 0f) continue;
                contact.Remaining = Mathf.Max(0f, contact.Remaining - Time.deltaTime);
                float age = 1f - contact.Remaining / ContactLifetime;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1f, age));
                float size = Mathf.SmoothStep(.049f, .10f, age) * fade;
                Vector3 tangent = Vector3.Cross(contact.Normal,
                    Mathf.Abs(contact.Normal.y) > .75f ? Vector3.forward : Vector3.up).normalized;
                Vector3 rise = contact.Normal * (.035f + age * .12f) + Vector3.up * (.025f + age * .09f);
                for (int i = 0; i < LobesPerContact; i++)
                {
                    float seed = SprayEmitter.SampleVariation(contact.Seed, (uint)(61 + i));
                    Vector3 puff = contact.Position + rise + tangent * ((i - 1) * .13f * age);
                    float radius = size * Mathf.Lerp(.76f, 1.18f, seed);
                    AddQuad(local, puff, cameraRight * radius, cameraUp * radius * .83f, seed);
                }
            }
            meshView.enabled = vertices.Count != 0;
            if (!meshView.enabled) return;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
        }

        void AddQuad(Matrix4x4 local, Vector3 center, Vector3 x, Vector3 y, float variation)
        {
            int start = vertices.Count;
            vertices.Add(local.MultiplyPoint3x4(center - x - y));
            vertices.Add(local.MultiplyPoint3x4(center + x - y));
            vertices.Add(local.MultiplyPoint3x4(center - x + y));
            vertices.Add(local.MultiplyPoint3x4(center + x + y));
            uvs.Add(new Vector4(0, 0, variation, 0));
            uvs.Add(new Vector4(1, 0, variation, 0));
            uvs.Add(new Vector4(0, 1, variation, 0));
            uvs.Add(new Vector4(1, 1, variation, 0));
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
