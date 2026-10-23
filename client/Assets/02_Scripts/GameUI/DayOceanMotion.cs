using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>Animates only the painted sea in DayTable/Deck; the ship and HUD stay still.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class DayOceanMotion : MonoBehaviour
    {
        private static readonly int OceanTime = Shader.PropertyToID("_OceanTime");
        private static readonly int SpriteUV = Shader.PropertyToID("_SpriteUV");
        private Image target;
        private Sprite deck;
        private Material motionMaterial, previousMaterial;
        private bool visible;

        public void SetVisible(bool value)
        {
            visible = value;
            if (visible && isActiveAndEnabled) Apply();
            else Restore();
        }

        private void OnEnable()
        {
            if (visible) Apply();
        }

        private void Apply()
        {
            if (target == null) target = GetComponent<Image>();
            if (deck == null) deck = Resources.Load<Sprite>("DayTable/Deck");
            if (target == null || deck == null || target.overrideSprite != deck) return;
            if (motionMaterial == null)
            {
                // Resources keeps the shader in player builds, even though the material is created at runtime.
                Shader shader = Resources.Load<Shader>("DayTable/DayOcean");
                if (shader == null || !shader.isSupported) return;
                motionMaterial = new Material(shader) { name = "Day ocean (runtime)", hideFlags = HideFlags.HideAndDontSave };
                motionMaterial.SetVector(SpriteUV, DataUtility.GetOuterUV(deck));
            }
            if (target.material != motionMaterial)
            {
                previousMaterial = target.material;
                target.material = motionMaterial;
            }
            Tick();
        }

        private void Update()
        {
            if (!visible || target == null || motionMaterial == null) return;
            if (target.overrideSprite != deck) { Restore(); return; }
            Tick();
        }

        private void Tick()
        {
            motionMaterial.SetFloat(OceanTime, Time.unscaledTime);
            // UI masks may use a stencil copy of the assigned material.
            Material rendered = target.materialForRendering;
            if (rendered != motionMaterial && rendered.HasProperty(OceanTime))
                rendered.SetFloat(OceanTime, Time.unscaledTime);
        }

        private void Restore()
        {
            if (target != null && motionMaterial != null && target.material == motionMaterial)
                target.material = previousMaterial;
        }

        private void OnDisable() => Restore();

        private void OnDestroy()
        {
            Restore();
            if (motionMaterial != null) Destroy(motionMaterial);
        }
    }
}
