using UnityEngine;

namespace FireRescue
{
    /// <summary>Shared fire appearance only. No fire state, damage, collision or spawning authority.</summary>
    public static class FireSurface
    {
        public static readonly int Clock = Shader.PropertyToID("_FireTime");
        public static readonly int Phase = Shader.PropertyToID("_Phase");
        public static readonly int Edges = Shader.PropertyToID("_EdgeMask");
        public static readonly int CoreBoost = Shader.PropertyToID("_CoreBoost");

        internal static int CellHash(int value)
        {
            // Spread flame samples across the field without row or column patterns.
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (int)(x & int.MaxValue);
            }
        }

        public static Material CreateMaterial()
        {
            var shader = Resources.Load<Shader>("FlowingFire");
            if (shader && shader.isSupported)
                return new Material(shader) { name = "Flowing cartoon fire" };
            // Preserve a visible warning surface even on an unsupported graphics device.
            return new Material(Shader.Find("Sprites/Default")) { color = new Color(1, .3f, .02f, .75f) };
        }

        public static void SetPhase(Renderer renderer, MaterialPropertyBlock properties, float phase, float coreBoost = 0f)
        {
            properties.Clear();
            properties.SetFloat(Phase, phase);
            // Outer and inner tongues share a material/pool but retain distinct visual roles.
            // This is presentation data only; no gameplay system reads it.
            properties.SetFloat(CoreBoost, Mathf.Clamp01(coreBoost));
            renderer.SetPropertyBlock(properties);
        }

        public static Mesh CreateTongueMesh(bool volumetric = false)
        {
            if (volumetric) return CreateVolumeCandidate();
            const int rows = 7;
            var vertices = new Vector3[rows * 2];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector4[vertices.Length];
            var triangles = new int[(rows - 1) * 6];
            int write = 0;
            // The shader faces this strip around world Y only, keeping its foot fixed.
            for (int row = 0; row < rows; row++)
                for (int side = 0; side < 2; side++)
                {
                    int i = row * 2 + side;
                    float h = row / (float)(rows - 1), x = side * 2 - 1;
                    vertices[i] = new Vector3(x, h, 0);
                    normals[i] = Vector3.forward;
                    uv[i] = new Vector4(side, h, 0, 0);
                    if (side == 0 && row < rows - 1)
                    {
                        triangles[write++] = i; triangles[write++] = i + 2; triangles[write++] = i + 1;
                        triangles[write++] = i + 1; triangles[write++] = i + 2; triangles[write++] = i + 3;
                    }
                }
            var mesh = new Mesh { name = "Upright flowing fire tongues" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.SetUVs(0, uv); mesh.triangles = triangles;
            // The shader bends only upper vertices, by at most .13 local units.
            mesh.bounds = new Bounds(new Vector3(0, .5f, 0), new Vector3(2.3f, 1.05f, 2.3f));
            return mesh;
        }

        public static Mesh CreateFootprintMesh()
        {
            var mesh = new Mesh { name = "Exact burning cell footprint" };
            mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.SetUVs(0, new[] { new Vector4(0, 0, -1, 0), new Vector4(0, 1, -1, 0), new Vector4(1, 1, -1, 0), new Vector4(1, 0, -1, 0) });
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Kept as a small same-shader A/B candidate, not a second fire simulation.
        static Mesh CreateVolumeCandidate()
        {
            const int sides = 10, rows = 7;
            var vertices = new Vector3[(sides + 1) * rows];
            var uv = new Vector4[vertices.Length];
            var triangles = new int[sides * (rows - 1) * 6];
            int write = 0;
            for (int row = 0; row < rows; row++)
                for (int side = 0; side <= sides; side++)
                {
                    int i = row * (sides + 1) + side;
                    float h = row / (float)(rows - 1), a = side * Mathf.PI * 2 / sides;
                    float radius = Mathf.Pow(1 - h, .7f) * (.8f + Mathf.Sin(h * Mathf.PI) * .2f);
                    vertices[i] = new Vector3(Mathf.Cos(a) * radius, h, Mathf.Sin(a) * radius);
                    uv[i] = new Vector4(side / (float)sides, h, 1, 0);
                    if (row == rows - 1 || side == sides) continue;
                    int next = i + sides + 1;
                    triangles[write++] = i; triangles[write++] = next; triangles[write++] = i + 1;
                    triangles[write++] = i + 1; triangles[write++] = next; triangles[write++] = next + 1;
                }
            var mesh = new Mesh { name = "Volume fire comparison candidate" };
            mesh.vertices = vertices; mesh.SetUVs(0, uv); mesh.triangles = triangles; mesh.RecalculateNormals();
            mesh.bounds = new Bounds(new Vector3(0, .5f, 0), new Vector3(2.3f, 1.05f, 2.3f));
            return mesh;
        }
    }
}
