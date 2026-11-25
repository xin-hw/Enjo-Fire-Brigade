using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // One palette and a small set of tongues per object. Collision and intensity live on ObjectFire.
    public sealed class ClassifiedFireView : MonoBehaviour
    {
        static readonly int Palette = Shader.PropertyToID("_FirePalette");
        static readonly int Bend = Shader.PropertyToID("_FireBend");
        [SerializeField] ObjectFire fire;
        [SerializeField] Vector3[] feet;
        [SerializeField] float flameHeight = .68f;
        Mesh mesh;
        Material flameMaterial, sparkMaterial;
        MeshRenderer[] tongues;
        WaterSteamView steam;
        ParticleSystem sparks;
        MaterialPropertyBlock properties;
        float sparkCredit;
        uint sparkId;

        public int SmokePuffs => steam ? steam.EmittedCount : 0;
        public void Initialize(ObjectFire target, Vector3[] bases, float height)
        {
            fire = target;
            feet = bases;
            flameHeight = height;
        }

        void Awake()
        {
            flameMaterial = FireSurface.CreateMaterial();
            flameMaterial.SetFloat(Palette, (int)fire.Kind + 1f);
            flameMaterial.SetFloat("_ColorBlocks", 1f);
            flameMaterial.SetFloat("_PaintedDetail", 1f);
            mesh = FireSurface.CreateTongueMesh();
            properties = new MaterialPropertyBlock();
            tongues = new MeshRenderer[feet.Length];
            for (int i = 0; i < feet.Length; i++)
            {
                var tongue = new GameObject("Object fire tongue");
                tongue.transform.SetParent(transform, false);
                tongue.transform.localPosition = feet[i];
                tongue.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = tongue.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = flameMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                tongues[i] = renderer;
            }

            var particles = new GameObject("Fire reaction particles");
            particles.transform.SetParent(transform, false);
            if (fire.Kind == FireKind.Electrical) ConfigureSparks(particles);
            else steam = particles.AddComponent<WaterSteamView>();
        }

        void OnEnable()
        {
            fire.OilContactedByWater += Smoke;
            fire.CooledByWater += Steam;
        }

        void OnDisable()
        {
            fire.OilContactedByWater -= Smoke;
            fire.CooledByWater -= Steam;
        }

        void Smoke(float amount) => steam.AddVapor(fire.LastImpactPoint, amount, true);
        void Steam(float amount) => steam.AddVapor(fire.LastImpactPoint, amount);

        void LateUpdate()
        {
            float scale = Mathf.Sqrt(Mathf.Clamp(fire.Intensity, 0f, 4.2f) / 2.4f);
            bool lit = fire.IsBurning;
            for (int i = 0; i < tongues.Length; i++)
            {
                var tongue = tongues[i];
                tongue.enabled = lit;
                if (!lit) continue;
                float height = flameHeight * scale * Mathf.Lerp(.68f, 1.12f, (i * .618f) % 1f);
                tongue.transform.localScale = new Vector3(height * .38f, height, height);
                properties.SetFloat(FireSurface.Clock, Time.time);
                properties.SetFloat(FireSurface.Phase, 3f + i * 2.44f);
                properties.SetFloat(FireSurface.CoreBoost, Mathf.Clamp01(scale));
                properties.SetVector(Bend, fire.Airflow * (height * .76f));
                tongue.SetPropertyBlock(properties);
            }
            if (lit && sparks)
            {
                sparkCredit += Time.deltaTime * Mathf.Lerp(4f, 15f, Mathf.Clamp01(scale));
                int count = Mathf.Min(3, Mathf.FloorToInt(sparkCredit));
                sparkCredit -= count;
                for (int i = 0; i < count; i++) EmitSpark();
            }
        }

        void ConfigureSparks(GameObject root)
        {
            sparks = root.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 28;
            main.gravityModifier = .15f;
            var emission = sparks.emission;
            emission.enabled = false;
            var shape = sparks.shape;
            shape.enabled = false;
            sparkMaterial = new Material(Shader.Find("Sprites/Default")) { name = "White electrical sparks", renderQueue = 3023 };
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            sparks.Play();
        }

        void EmitSpark()
        {
            uint id = sparkId++;
            float x = SprayEmitter.SampleVariation(id, 133) - .5f;
            float z = SprayEmitter.SampleVariation(id, 65) - .5f;
            sparks.Emit(new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(feet[id % (uint)feet.Length]) + Vector3.up * .18f,
                velocity = new Vector3(x * 1.25f, 1.5f + z * .8f, z) + fire.Airflow * 1.45f,
                startSize = Mathf.Lerp(.034f,.064f,x + .5f), startColor = Color.white,
                startLifetime = Mathf.Lerp(.16f,.41f,z + .5f), randomSeed = id + 1
            }, 1);
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (flameMaterial) Destroy(flameMaterial);
            if (sparkMaterial) Destroy(sparkMaterial);
        }
    }
}
