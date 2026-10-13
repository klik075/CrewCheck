using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    public enum ChibiPose
    {
        Standing,
        SeatedFront,
        SeatedSide,
        SeatedBack,
        Seated1OClock,
        Seated5OClock,
        Seated7OClock,
        Seated11OClock
    }

    /// <summary>
    /// One reusable avatar view. Swap the skin to change the design while keeping
    /// the same seat, pose and speaking controls.
    /// Every skin is drawn at the same height with its feet on this object's position (sprites are fitted by
    /// Tools/ChibiSkinFit/FitSkins.ps1: trimmed, pivot where the character sits), so a new skin never moves or resizes.
    /// </summary>
    public sealed class ChibiCharacterView : MonoBehaviour
    {
        [SerializeField] private ChibiCharacterSkin skin;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private DialoguePortraitMotion motion;
        [SerializeField] private ChibiPose pose;
        [SerializeField] private bool faceLeft;
        [Tooltip("Height of a seated character in world units, the same for every skin.")]
        [SerializeField] private float seatedHeight = 3.6f;
        [Tooltip("Height of the standing pose in world units.")]
        [SerializeField] private float standingHeight = 3.9f;

        private bool speaking;
        private float nextBlinkAt;
        private float blinkEndsAt;

        public ChibiCharacterSkin Skin => skin;
        public ChibiPose Pose => pose;

        public void SetSkin(ChibiCharacterSkin value)
        {
            skin = value;
            RefreshSprite();
        }

        public void SetPose(ChibiPose value, bool lookLeft = false)
        {
            pose = value;
            faceLeft = lookLeft;
            RefreshSprite();
        }

        public void SetSpeaking(bool value)
        {
            speaking = value;
            if (motion != null) motion.SetSpeaking(value);
            RefreshSprite();
        }

        private void Awake()
        {
            FindComponents();
            ScheduleBlink();
            RefreshSprite();
        }

        private void OnEnable()
        {
            ScheduleBlink();
            RefreshSprite();
        }

        private void Update()
        {
            if (skin == null || pose != ChibiPose.SeatedFront) return;

            float now = Time.unscaledTime;
            if (now >= nextBlinkAt)
            {
                blinkEndsAt = now + 0.12f;
                ScheduleBlink();
            }
            RefreshSprite();
        }

        private void OnValidate()
        {
            FindComponents();
            RefreshSprite();
        }

        private void FindComponents()
        {
            if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);
            if (motion == null) motion = GetComponentInChildren<DialoguePortraitMotion>(true);
        }

        private void ScheduleBlink()
        {
            nextBlinkAt = Time.unscaledTime + Random.Range(2.7f, 5.2f);
        }

        private void RefreshSprite()
        {
            if (visual == null) return;
            if (skin == null)
            {
                visual.sprite = null;
                return;
            }

            Sprite sprite;
            switch (pose)
            {
                case ChibiPose.SeatedFront:
                    bool blinking = Application.isPlaying && Time.unscaledTime < blinkEndsAt;
                    bool mouthOpen = speaking && Mathf.FloorToInt(Time.unscaledTime * 5f) % 2 == 0;
                    sprite = blinking && skin.SeatedFrontBlink != null ? skin.SeatedFrontBlink :
                        mouthOpen && skin.SeatedFrontTalk != null ? skin.SeatedFrontTalk : skin.SeatedFront;
                    break;
                case ChibiPose.SeatedSide:
                    sprite = skin.SeatedSide;
                    break;
                case ChibiPose.SeatedBack:
                    sprite = skin.SeatedBack;
                    break;
                case ChibiPose.Seated1OClock:
                    sprite = skin.Seated1OClock;
                    break;
                case ChibiPose.Seated5OClock:
                    sprite = skin.Seated5OClock;
                    break;
                case ChibiPose.Seated7OClock:
                    sprite = skin.Seated7OClock;
                    break;
                case ChibiPose.Seated11OClock:
                    sprite = skin.Seated11OClock;
                    break;
                default:
                    sprite = skin.Standing;
                    break;
            }

            visual.sprite = sprite != null ? sprite : skin.Standing;
            visual.flipX = pose == ChibiPose.SeatedSide && faceLeft;
            FitHeight(visual.sprite);
        }

        // Sprites come from sheets with different cell sizes and pixels-per-unit; scale each to one height.
        // Play mode only: in edit mode this would write the scale into the prefab or scene.
        private void FitHeight(Sprite sprite)
        {
            if (!Application.isPlaying || sprite == null || sprite.rect.height <= 0f) return;
            float target = pose == ChibiPose.Standing ? standingHeight : seatedHeight;
            float scale = target / (sprite.rect.height / sprite.pixelsPerUnit);
            if (motion != null && motion.transform == visual.transform) motion.SetScaleMultiplier(scale);
            else visual.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
