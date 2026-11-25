using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireRescue
{
    // Paints one screen-pixel contour over the visible silhouette of explicitly added renderers.
    [RequireComponent(typeof(Camera))]
    public sealed class WorldOutlineView : MonoBehaviour
    {
        const string MaskPass = "OUTLINE_MASK";
        const string HiddenFirePass = "WALL_OCCLUDED_FIRE";
        const string FireOccluderPass = "FIRE_OCCLUDER";
        const string HiddenFireEdgePass = "WALL_FIRE_OUTLINE";
        const string ResourceMaskPass = "RESOURCE_FOCUS_MASK";
        const string ResourceFocusPass = "RESOURCE_FOCUS";
        const CameraEvent RenderEvent = CameraEvent.AfterForwardAlpha;
        const CameraEvent OccludedFireEvent = CameraEvent.BeforeForwardAlpha;
        static readonly int Mask = Shader.PropertyToID("_FireRescueOutlineMask");
        static readonly int PixelSize = Shader.PropertyToID("_FireRescueOutlinePixelSize");
        static readonly int MaskSamples = Shader.PropertyToID("_FireRescueOutlineSamples");
        static readonly int WritingMask = Shader.PropertyToID("_FireRescueWritingOutlineMask");
        static readonly int Selected = Shader.PropertyToID("_Selected");
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        static readonly int MainColour = Shader.PropertyToID("_Color");
        static readonly int OutlineAlbedoTint = Shader.PropertyToID("_OutlineAlbedoTint");
        static readonly int WallMask = Shader.PropertyToID("_FireRescueWallMask");
        static readonly int HiddenFireMask = Shader.PropertyToID("_FireRescueHiddenFireMask");
        static readonly int DrawHiddenFire = Shader.PropertyToID("_FireRescueDrawHiddenFire");
        static readonly int HiddenFireColour = Shader.PropertyToID("_FireRescueOccludedFireColor");
        static readonly int ResourceMask = Shader.PropertyToID("_FireRescueResourceMask");
        static readonly int ResourceScene = Shader.PropertyToID("_FireRescueResourceScene");
        static readonly int ResourceFocusStrength = Shader.PropertyToID("_FireRescueResourceFocusStrength");

        [SerializeField] Renderer fireOcclusionWall;
        [SerializeField] Color hiddenFireColour = new(.80f, .29f, .19f);

        readonly List<Target> targets = new();
        readonly List<Material> materials = new();
        readonly Dictionary<Material, Material> opaqueMasks = new();
        Camera view;
        CommandBuffer commands;
        CommandBuffer hiddenFireCommands;
        Material composite;
        Material green;
        int hiddenFireEdgePass, resourceMaskPass, resourceFocusPass;
        bool addedDepth;

        public FluidSource Focus { get; set; }
        internal float ResourceFocusAmount { get; set; }

        public void AddOpaque(Transform root, FluidSource source = null) => AddTargets(root, false, source);
        public void AddEffect(Transform root) => AddTargets(root, true, null);

        void AddTargets(Transform root, bool effect, FluidSource source)
        {
            if (!composite) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
                renderer.GetSharedMaterials(materials);
                for (int i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    if (!material) continue;
                    if (effect)
                    {
                        int pass = material.FindPass(MaskPass);
                        if (pass >= 0)
                            targets.Add(new Target(renderer, i, material, pass, null, material.FindPass(HiddenFirePass)));
                    }
                    else if (material.renderQueue <= (int)RenderQueue.GeometryLast &&
                        material.GetTag("RenderType", true, "Opaque") == "Opaque")
                    {
                        targets.Add(new Target(renderer, i, GetOpaqueMask(material), 0, source));
                    }
                }
            }
            materials.Clear();
        }

        Material GetOpaqueMask(Material source)
        {
            if (opaqueMasks.TryGetValue(source, out var mask)) return mask;

            mask = new Material(composite) { hideFlags = HideFlags.DontSave };
            if (source.HasProperty(MainColour))
                mask.SetColor(OutlineAlbedoTint, source.GetColor(MainColour));
            if (source.HasProperty(MainTexture) && source.GetTexture(MainTexture) is Texture texture)
            {
                mask.SetTexture(MainTexture, texture);
                mask.SetTextureScale(MainTexture, source.GetTextureScale(MainTexture));
                mask.SetTextureOffset(MainTexture, source.GetTextureOffset(MainTexture));
            }
            opaqueMasks.Add(source, mask);
            return mask;
        }

        void Awake()
        {
            view = GetComponent<Camera>();
            var shader = Shader.Find("FireRescue/WorldOutline");
            if (!shader || !shader.isSupported) return;
            composite = new Material(shader) { hideFlags = HideFlags.DontSave };
            green = new Material(shader) { hideFlags = HideFlags.DontSave };
            green.SetFloat(Selected, 1f);
            hiddenFireEdgePass = composite.FindPass(HiddenFireEdgePass);
            resourceMaskPass = composite.FindPass(ResourceMaskPass);
            resourceFocusPass = composite.FindPass(ResourceFocusPass);
        }

        void OnEnable()
        {
            if (!composite) return;
            addedDepth = (view.depthTextureMode & DepthTextureMode.Depth) == 0;
            view.depthTextureMode |= DepthTextureMode.Depth;
            commands = new CommandBuffer { name = "Fire Rescue gameplay outlines" };
            view.AddCommandBuffer(RenderEvent, commands);
            if (fireOcclusionWall && hiddenFireEdgePass >= 0)
            {
                hiddenFireCommands = new CommandBuffer { name = "Fire contours on occluding walls" };
                view.AddCommandBuffer(OccludedFireEvent, hiddenFireCommands);
            }
        }

        void OnPreRender()
        {
            if (commands == null) return;
            commands.Clear();
            Vector4 pixelSize = new(1f / view.scaledPixelWidth, 1f / view.scaledPixelHeight, 0f, 0f);
            if (hiddenFireCommands != null) RecordHiddenFire(pixelSize);
            if (targets.Count == 0) return;

            int samples = GetMaskSampleCount();
            commands.GetTemporaryRT(Mask, -1, -1, 24, FilterMode.Point,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, samples);
            commands.SetRenderTarget(Mask);
            commands.ClearRenderTarget(true, true, Color.clear);
            commands.SetGlobalVector(PixelSize, pixelSize);
            commands.SetGlobalFloat(MaskSamples, samples);
            commands.SetGlobalFloat(WritingMask, 1f);
            foreach (var target in targets)
            {
                var renderer = target.Renderer;
                if (!CanDraw(renderer)) continue;
                bool selected = target.Source && target.Source == Focus;
                commands.DrawRenderer(renderer, selected ? green : target.MaskMaterial, target.SubMesh, target.Pass);
            }
            commands.SetGlobalFloat(WritingMask, 0f);
            commands.Blit(Mask, BuiltinRenderTextureType.CameraTarget, composite, 1);
            commands.ReleaseTemporaryRT(Mask);
            if (ResourceFocusAmount > 0f && resourceMaskPass >= 0 && resourceFocusPass >= 0)
                RecordResourceFocus();
        }

        void RecordResourceFocus()
        {
            commands.GetTemporaryRT(ResourceMask, -1, -1, 0, FilterMode.Bilinear,
                RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            commands.SetRenderTarget(ResourceMask);
            commands.ClearRenderTarget(false, true, Color.clear);
            foreach (var target in targets)
            {
                var source = target.Source;
                if (!source || !source.isActiveAndEnabled || (source.RemainingAmount <= 0d && !source.CanRefill) ||
                    !CanDraw(target.Renderer)) continue;
                commands.DrawRenderer(target.Renderer, composite, target.SubMesh, resourceMaskPass);
            }

            var format = view.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            commands.GetTemporaryRT(ResourceScene, -1, -1, 0, FilterMode.Bilinear, format);
            commands.Blit(BuiltinRenderTextureType.CameraTarget, ResourceScene);
            commands.SetGlobalFloat(ResourceFocusStrength, ResourceFocusAmount);
            commands.Blit(ResourceScene, BuiltinRenderTextureType.CameraTarget, composite, resourceFocusPass);
            commands.ReleaseTemporaryRT(ResourceScene);
            commands.ReleaseTemporaryRT(ResourceMask);
        }

        int GetMaskSampleCount()
        {
            if (!view.allowMSAA || view.actualRenderingPath == RenderingPath.DeferredShading) return 1;
            // The HDR camera can render through a multisampled intermediate before resolving to its target.
            int requested = QualitySettings.antiAliasing;
            if (view.targetTexture) requested = Mathf.Max(requested, view.targetTexture.antiAliasing);
            if (requested < 2) return 1;
            var descriptor = new RenderTextureDescriptor(view.scaledPixelWidth, view.scaledPixelHeight,
                RenderTextureFormat.ARGBHalf, 24) { msaaSamples = requested, sRGB = false };
            return SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
        }

        void RecordHiddenFire(Vector4 pixelSize)
        {
            hiddenFireCommands.Clear();
            if (!CanDraw(fireOcclusionWall) || fireOcclusionWall.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return;
            Material wallMaterial = fireOcclusionWall.sharedMaterial;
            int maskPass = wallMaterial ? wallMaterial.FindPass(FireOccluderPass) : -1;
            if (maskPass < 0) return;

            bool hasFire = false;
            foreach (var target in targets)
            {
                if (target.HiddenFirePass < 0 || !CanDraw(target.Renderer)) continue;
                if (!hasFire)
                {
                    hasFire = true;
                    hiddenFireCommands.GetTemporaryRT(WallMask, -1, -1, 0, FilterMode.Point,
                        RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                    hiddenFireCommands.GetTemporaryRT(HiddenFireMask, -1, -1, 0, FilterMode.Point,
                        RenderTextureFormat.RG16, RenderTextureReadWrite.Linear);
                    hiddenFireCommands.SetRenderTarget(WallMask);
                    hiddenFireCommands.ClearRenderTarget(false, true, Color.clear);
                    hiddenFireCommands.SetGlobalVector(PixelSize, pixelSize);
                    hiddenFireCommands.SetGlobalFloat(DrawHiddenFire, 1f);
                    hiddenFireCommands.DrawRenderer(fireOcclusionWall, wallMaterial, 0, maskPass);
                    hiddenFireCommands.SetRenderTarget(HiddenFireMask);
                    hiddenFireCommands.ClearRenderTarget(false, true, Color.clear);
                }
                hiddenFireCommands.DrawRenderer(target.Renderer, target.MaskMaterial, target.SubMesh, target.HiddenFirePass);
            }
            if (!hasFire) return;
            hiddenFireCommands.SetGlobalFloat(DrawHiddenFire, 0f);
            // Extract the outer edge once, after overlapping flames have joined into one mask.
            hiddenFireCommands.SetGlobalColor(HiddenFireColour, hiddenFireColour);
            hiddenFireCommands.Blit(HiddenFireMask, BuiltinRenderTextureType.CameraTarget, composite, hiddenFireEdgePass);
            hiddenFireCommands.ReleaseTemporaryRT(HiddenFireMask);
            hiddenFireCommands.ReleaseTemporaryRT(WallMask);
        }

        bool CanDraw(Renderer renderer) => renderer && renderer.enabled && !renderer.forceRenderingOff &&
            renderer.gameObject.activeInHierarchy && (view.cullingMask & (1 << renderer.gameObject.layer)) != 0;

        void OnDisable()
        {
            if (commands == null) return;
            view.RemoveCommandBuffer(RenderEvent, commands);
            commands.Release();
            commands = null;
            if (hiddenFireCommands != null)
            {
                view.RemoveCommandBuffer(OccludedFireEvent, hiddenFireCommands);
                hiddenFireCommands.Release();
                hiddenFireCommands = null;
            }
            if (addedDepth) view.depthTextureMode &= ~DepthTextureMode.Depth;
        }

        void OnDestroy()
        {
            foreach (var mask in opaqueMasks.Values) Destroy(mask);
            if (composite) Destroy(composite);
            if (green) Destroy(green);
        }

        readonly struct Target
        {
            public readonly Renderer Renderer;
            public readonly int SubMesh;
            public readonly Material MaskMaterial;
            public readonly int Pass;
            public readonly int HiddenFirePass;
            public readonly FluidSource Source;

            public Target(Renderer renderer, int subMesh, Material maskMaterial, int pass, FluidSource source, int hiddenFirePass = -1)
            {
                Renderer = renderer;
                SubMesh = subMesh;
                MaskMaterial = maskMaterial;
                Pass = pass;
                HiddenFirePass = hiddenFirePass;
                Source = source;
            }
        }
    }
}
