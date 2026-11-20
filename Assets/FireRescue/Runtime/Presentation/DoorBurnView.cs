using System;
using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// Presentation-only char on a burning door. It never changes fire, durability or materials.
    /// BreakableDoorView remains responsible for hiding the door once it has burned through.
    /// </summary>
    public sealed class DoorBurnView : MonoBehaviour
    {
        static readonly int ColorProperty = Shader.PropertyToID("_Color");
        static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        static readonly Color BurntColor = new Color(.09f, .045f, .018f, 1f);

        [SerializeField] private DoorFireExposure exposure;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();

        private int[] colorProperties = Array.Empty<int>();
        private Color[] baseColors = Array.Empty<Color>();
        private MaterialPropertyBlock propertyBlock;
        private bool captured;

        /// <summary>Explicit scene/builder wiring. Renderers are not cloned and shared materials are never edited.</summary>
        public void Initialize(DoorFireExposure fireExposure, Renderer[] doorRenderers)
        {
            ClearPresentation();
            exposure = fireExposure;
            renderers = doorRenderers == null ? Array.Empty<Renderer>() : (Renderer[])doorRenderers.Clone();
            CaptureBaseColors();
            TickPresentation();
        }

        private void LateUpdate() => TickPresentation();
        private void OnDisable() => ClearPresentation();
        private void OnDestroy() => ClearPresentation();

        /// <summary>Applies the current exposure ratio without creating material instances.</summary>
        public void TickPresentation()
        {
            if (!isActiveAndEnabled || !exposure || !exposure.IsExposed || exposure.BurnoutSeconds <= 0f)
            {
                ClearPresentation();
                return;
            }

            CaptureBaseColors();
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(exposure.ExposureSeconds / exposure.BurnoutSeconds));
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer || colorProperties[i] == 0) continue;
                propertyBlock.Clear();
                renderer.GetPropertyBlock(propertyBlock);
                Color target = Color.Lerp(baseColors[i], BurntColor, amount);
                target.a = baseColors[i].a;
                propertyBlock.SetColor(colorProperties[i], target);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void CaptureBaseColors()
        {
            if (captured) return;
            propertyBlock ??= new MaterialPropertyBlock();
            colorProperties = new int[renderers.Length];
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = renderer ? renderer.sharedMaterial : null;
                if (!material) continue;
                int property = material.HasProperty(ColorProperty) ? ColorProperty
                    : material.HasProperty(BaseColorProperty) ? BaseColorProperty : 0;
                if (property == 0) continue;
                colorProperties[i] = property;
                baseColors[i] = material.GetColor(property);
            }
            captured = true;
        }

        private void ClearPresentation()
        {
            if (!captured) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i]) renderers[i].SetPropertyBlock(null);
            captured = false;
            colorProperties = Array.Empty<int>();
            baseColors = Array.Empty<Color>();
        }
    }
}
