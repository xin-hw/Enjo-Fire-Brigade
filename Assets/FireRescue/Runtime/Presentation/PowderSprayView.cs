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
        const float ContactLifetime = .46f;
        static readonly System.Comparison<Puff> BackToFront = (a, b) => b.Depth.CompareTo(a.Depth);

        [SerializeField] SprayEmitter emitter;
        [SerializeField] Camera view;
        readonly Contact[] contacts = new Contact[ContactCapacity];
        readonly List<Puff> puffs = new(QuadCapacity);
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
            public Vector3 Drift;
            public float Remaining;
            public uint Seed;
        }

        struct Puff
        {
            public Vector3 Center, Along, Across;
            public float Depth, Variation, Opacity;
        }

        public void Initialize(SprayEmitter source, Camera camera)
        {
            emitter = source;
            view = camera;
        }

        void Awake()
        {
            var shader = Resources.Load<Shader>("PowderMist");
            if (!shader || !shader.isSupported) return;
            material = new Material(shader) { name = "Baking soda mist" };
            mesh = new Mesh { name = "Batched baking soda puffs" };
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
            {
                Position = impact.Position,
                Normal = normal,
                Drift = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(impact.Velocity, normal) * .05f, .28f),
                Remaining = ContactLifetime,
                Seed = ++contactSeed
            };
            nextContact = (nextContact + 1) % contacts.Length;
        }

        void LateUpdate()
        {
            if (!material || !emitter || !view) return;
            puffs.Clear();
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
                float expansion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.015f, .38f, grain.AgeSeconds));
                float radius = Mathf.Lerp(.081f, .25f, expansion) * Mathf.Lerp(.82f, 1.16f, variation);
                Vector3 trail = grain.Position - grain.TrailStart;
                float length = Mathf.Clamp(radius * .80f + Vector3.ProjectOnPlane(trail, cameraForward).magnitude * .55f, .09f, .39f);
                Vector3 center = grain.Position - trail * .36f;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.64f, 1f, grain.NormalizedAge));
                float opacity = Mathf.Lerp(.68f, .46f, expansion) * fade;
                AddPuff(center, longAxis * length, side * radius, cameraForward, variation, opacity);
            }
            for (int slot = 0; slot < contacts.Length; slot++)
            {
                ref var contact = ref contacts[slot];
                if (contact.Remaining <= 0f) continue;
                contact.Remaining = Mathf.Max(0f, contact.Remaining - Time.deltaTime);
                float age = 1f - contact.Remaining / ContactLifetime;
                float fade = (1f - age) * Mathf.SmoothStep(.4f, 1f, age * 6f);
                float size = Mathf.SmoothStep(.087f, .22f, age);
                Vector3 tangent = Vector3.Cross(contact.Normal,
                    Mathf.Abs(contact.Normal.y) > .75f ? Vector3.forward : Vector3.up).normalized;
                Vector3 rise = contact.Normal * (.045f + age * .13f) + Vector3.up * (.025f + age * .13f) + contact.Drift * age;
                for (int i = 0; i < LobesPerContact; i++)
                {
                    float seed = SprayEmitter.SampleVariation(contact.Seed, (uint)(61 + i));
                    Vector3 puff = contact.Position + rise + tangent * ((i - 1) * (.025f + .14f * age));
                    float radius = size * Mathf.Lerp(.76f, 1.18f, seed);
                    AddPuff(puff, cameraRight * radius, cameraUp * radius * .88f, cameraForward, seed, .48f * fade);
                }
            }
            meshView.enabled = puffs.Count != 0;
            if (!meshView.enabled) return;
            // Alpha-composite the puffs in view order instead of using the emitter's reused slots.
            puffs.Sort(BackToFront);
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
            var local = transform.worldToLocalMatrix;
            foreach (var puff in puffs) AddQuad(local, puff);
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
        }

        void AddPuff(Vector3 center, Vector3 along, Vector3 across, Vector3 cameraForward, float variation, float opacity)
        {
            puffs.Add(new Puff
            {
                Center = center, Along = along, Across = across,
                Depth = Vector3.Dot(center, cameraForward), Variation = variation, Opacity = opacity
            });
        }

        void AddQuad(Matrix4x4 local, Puff puff)
        {
            int start = vertices.Count;
            Vector3 center = puff.Center, x = puff.Along, y = puff.Across;
            vertices.Add(local.MultiplyPoint3x4(center - x - y));
            vertices.Add(local.MultiplyPoint3x4(center + x - y));
            vertices.Add(local.MultiplyPoint3x4(center - x + y));
            vertices.Add(local.MultiplyPoint3x4(center + x + y));
            uvs.Add(new Vector4(0, 0, puff.Variation, puff.Opacity));
            uvs.Add(new Vector4(1, 0, puff.Variation, puff.Opacity));
            uvs.Add(new Vector4(0, 1, puff.Variation, puff.Opacity));
            uvs.Add(new Vector4(1, 1, puff.Variation, puff.Opacity));
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
