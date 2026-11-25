using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // Short, local steam puffs paid for by the volume that actually vaporized.
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class WaterSteamView : MonoBehaviour
    {
        const int Capacity = 56;
        const double VolumePerPuff = .014d;
        ParticleSystem particles;
        Texture2D texture;
        Material material;
        double credit;
        uint sequence;

        public int ActiveCount => particles ? particles.particleCount : 0;
        public int EmittedCount { get; private set; }
        public Vector3 LastOrigin { get; private set; }

        void Awake()
        {
            particles = GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Capacity;
            main.startSpeed = 0f;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.55f, 0f), new GradientAlphaKey(.94f, .12f),
                    new GradientAlphaKey(.55f, .52f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .65f, 1f, 1.65f));

            texture = CreateBillow();
            material = new Material(Shader.Find("Sprites/Default"))
                { name = "Water contact steam", mainTexture = texture, renderQueue = 3020 };
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void OnEnable() => particles.Play();

        public void AddVapor(Vector3 point, double volume, bool darkSmoke = false)
        {
            if (volume <= 0d) return;
            credit += volume;
            if (credit < VolumePerPuff) return;
            int count = (int)(credit / VolumePerPuff);
            credit -= count * VolumePerPuff;
            count = Mathf.Min(count, Capacity - particles.particleCount);
            for (int i = 0; i < count; i++)
            {
                uint id = sequence++;
                float u = SprayEmitter.SampleVariation(id, 81);
                float v = SprayEmitter.SampleVariation(id, 92);
                LastOrigin = point;
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = point + Vector3.up * .065f,
                    velocity = new Vector3((u - .5f) * .38f, Mathf.Lerp(1.13f, 1.52f, v), (v - .5f) * .36f),
                    startSize = Mathf.Lerp(.32f, .49f, u),
                    startLifetime = Mathf.Lerp(.48f, .76f, v),
                    rotation = 360f * u,
                    startColor = darkSmoke ? new Color(.115f,.097f,.10f,.93f) : new Color(.99f, .99f, 1f, .88f),
                    randomSeed = id + 1
                }, 1);
                EmittedCount++;
            }
        }

        void OnDisable()
        {
            credit = 0d;
            if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (texture) Destroy(texture);
            if (material) Destroy(material);
        }

        static Texture2D CreateBillow()
        {
            const int size = 64;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x / (size - 1f) * 2f - 1f, y / (size - 1f) * 2f - 1f);
                float body = 1f - (1f - Lobe(p, -.26f, -.12f, .63f)) *
                    (1f - Lobe(p, .30f, -.06f, .58f)) * (1f - Lobe(p, .02f, .28f, .64f));
                float grain = Mathf.Sin(p.x * 16 + p.y * 10) * Mathf.Sin(p.y * 14 - p.x * 9);
                float gray = Mathf.Lerp(.52f, .96f, body);
                float alpha = body * (.89f + grain * .085f);
                pixels[y * size + x] = new Color(gray, gray * 1.015f, gray * 1.03f, alpha);
            }
            image.SetPixels32(pixels);
            image.Apply(false, true);
            return image;
        }

        static float Lobe(Vector2 p, float x, float y, float radius) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.03f, radius, Vector2.Distance(p, new Vector2(x, y))));
    }
}
