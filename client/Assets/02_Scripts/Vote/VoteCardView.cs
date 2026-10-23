using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhoisntCitizen.Vote
{
    /// <summary>
    /// 투표 카드패의 카드 한 장. 모양은 코드로 만들고(프리팹 불필요), 움직임은 목표 자세(위치·회전·크기)를 향해 부드럽게 따라간다.
    /// - 루트: 부채꼴 자리(목표 자세)를 따라 움직인다. 투명 Image가 클릭·마우스 판정을 받는다.
    /// - Visual: 실제 그림. 마우스를 올리면 루트 안에서 카드 기준 위쪽으로 살짝 올라간다.
    ///   (판정 영역은 루트에 그대로 있으므로, 카드가 올라가도 마우스가 빠졌다 들어왔다 하며 떨리지 않는다)
    /// 카드는 표시와 입력 전달만 한다. 뽑기·되돌리기 결정은 VoteCardHandController가 한다.
    /// </summary>
    public sealed class VoteCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static readonly Color FrameColor = new Color(0.13f, 0.16f, 0.24f, 1f);
        private static readonly Color PortraitBgColor = new Color(0.08f, 0.10f, 0.15f, 1f);
        private static readonly Color PlateColor = new Color(0.06f, 0.07f, 0.11f, 0.92f);
        private static readonly Color OutlineIdle = new Color(0.55f, 0.60f, 0.72f, 0.9f);
        private static readonly Color OutlineHot = new Color(1f, 0.82f, 0.30f, 1f);
        private static readonly Color GlowColor = new Color(1f, 0.80f, 0.25f, 1f);

        public long PlayerId { get; private set; }
        public string Nickname { get; private set; }

        /// <summary>카드패 안에서의 순서(왼쪽부터). 되돌릴 때 원래 자리로 돌아가는 데 쓴다.</summary>
        public int HandIndex { get; set; }

        /// <summary>뽑혀서 카드패 밖에 단독으로 놓여 있는지.</summary>
        public bool IsDrawn { get; private set; }

        public RectTransform Rect { get; private set; }

        /// <summary>마우스를 올렸을 때 카드 위쪽으로 올라가는 거리(px)와 커지는 비율.</summary>
        public float HoverLift { get; set; } = 48f;
        public float HoverScale { get; set; } = 1.06f;

        /// <summary>목표 자세를 따라가는 빠르기. 클수록 빠르다. (약 4/speed 초에 거의 도착)</summary>
        public float FollowSpeed { get; set; } = 12f;

        public event Action<VoteCardView> Clicked;
        public event Action<VoteCardView, bool> HoverChanged;

        private RectTransform visual;
        private CanvasGroup canvasGroup;
        private Image hitArea;
        private Image glow;
        private Outline outline;
        private Image portrait;
        private TextMeshProUGUI nameText;

        private Vector2 targetPos;
        private float targetAngle;
        private float targetScale = 1f;
        private float targetAlpha = 1f;
        private bool hovered;
        private bool interactable = true;
        private bool fadingOut;
        private float glowAlpha;

        // ================================================================ 생성

        public static VoteCardView Create(Transform parent, long playerId, string nickname, Sprite portraitSprite,
                                          Sprite cardSprite, TMP_FontAsset font, Vector2 size)
        {
            var go = new GameObject("VoteCard_" + playerId, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            // 카드패·뽑은 카드 자리 모두 "부모 하단 가운데" 기준으로 둔다. (부모를 바꿔도 위치 계산이 같다)
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;

            var view = go.AddComponent<VoteCardView>();
            view.Build(size, cardSprite, font);
            view.Bind(playerId, nickname, portraitSprite);
            return view;
        }

        private void Build(Vector2 size, Sprite cardSprite, TMP_FontAsset font)
        {
            Rect = (RectTransform)transform;
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // 클릭·마우스 판정 (보이지 않음)
            hitArea = gameObject.AddComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;

            visual = NewRect("Visual", Rect);
            Stretch(visual, Vector2.zero, Vector2.zero);

            glow = NewImage("Glow", visual, GlowColor, cardSprite);
            Stretch(glow.rectTransform, new Vector2(-14f, -14f), new Vector2(14f, 14f));
            SetAlpha(glow, 0f);

            Image shadow = NewImage("Shadow", visual, new Color(0f, 0f, 0f, 0.35f), cardSprite);
            Stretch(shadow.rectTransform, new Vector2(6f, -10f), new Vector2(6f, -10f));

            Image frame = NewImage("Frame", visual, FrameColor, cardSprite);
            Stretch(frame.rectTransform, Vector2.zero, Vector2.zero);
            outline = frame.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(4f, -4f);
            outline.effectColor = OutlineIdle;

            float inset = Mathf.Round(size.x * 0.07f);
            float plateHeight = Mathf.Round(size.y * 0.2f);

            Image portraitBg = NewImage("PortraitBg", visual, PortraitBgColor, null);
            Stretch(portraitBg.rectTransform, new Vector2(inset, inset + plateHeight), new Vector2(-inset, -inset));

            portrait = NewImage("Portrait", portraitBg.rectTransform, Color.white, null);
            Stretch(portrait.rectTransform, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            portrait.preserveAspect = true;

            Image plate = NewImage("NamePlate", visual, PlateColor, null);
            Stretch(plate.rectTransform, new Vector2(inset, inset), Vector2.zero);
            plate.rectTransform.anchorMax = new Vector2(1f, 0f);
            plate.rectTransform.offsetMax = new Vector2(-inset, inset + plateHeight - 6f);

            var textRt = NewRect("Name", plate.rectTransform);
            Stretch(textRt, new Vector2(6f, 0f), new Vector2(-6f, 0f));
            nameText = textRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                nameText.font = font;
            }
            nameText.richText = false; // 닉네임이 태그로 해석되지 않게
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 16f;
            nameText.fontSizeMax = Mathf.Round(size.y * 0.085f);
            nameText.color = Color.white;
            nameText.raycastTarget = false;
        }

        private void Bind(long playerId, string nickname, Sprite portraitSprite)
        {
            PlayerId = playerId;
            Nickname = nickname;
            nameText.text = nickname;
            portrait.sprite = portraitSprite;
            portrait.enabled = portraitSprite != null;
        }

        // ================================================================ 상태

        /// <summary>움직일 목표 자세 (부모 하단 가운데 기준 anchoredPosition, z 회전 각도, 크기).</summary>
        public void SetTarget(Vector2 position, float angle, float scale)
        {
            targetPos = position;
            targetAngle = angle;
            targetScale = scale;
        }

        /// <summary>목표 자세로 바로 옮긴다 (처음 배치할 때).</summary>
        public void SnapToTarget()
        {
            Rect.anchoredPosition = targetPos;
            Rect.localRotation = Quaternion.Euler(0f, 0f, targetAngle);
            Rect.localScale = Vector3.one * targetScale;
        }

        public void SetTargetAlpha(float alpha)
        {
            targetAlpha = alpha;
        }

        public float Alpha
        {
            get { return canvasGroup.alpha; }
            set { canvasGroup.alpha = value; }
        }

        public void SetDrawn(bool drawn)
        {
            IsDrawn = drawn;
            if (drawn)
            {
                SetHovered(false);
            }
        }

        /// <summary>사라지는 중: 빛을 먼저 끈다. (반투명해진 카드 뒤로 금빛이 비쳐 보이지 않게)</summary>
        public void BeginFadeOut()
        {
            fadingOut = true;
            SetInteractable(false);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            canvasGroup.blocksRaycasts = value;
            if (!value)
            {
                SetHovered(false);
            }
        }

        private void SetHovered(bool value)
        {
            if (hovered == value)
            {
                return;
            }
            hovered = value;
            HoverChanged?.Invoke(this, value);
        }

        // ================================================================ 입력

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (interactable && !IsDrawn)
            {
                SetHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (interactable && eventData.button == PointerEventData.InputButton.Left)
            {
                Clicked?.Invoke(this);
            }
        }

        // ================================================================ 움직임

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-FollowSpeed * dt);

            Rect.anchoredPosition = Vector2.Lerp(Rect.anchoredPosition, targetPos, k);
            Rect.localRotation = Quaternion.Slerp(Rect.localRotation, Quaternion.Euler(0f, 0f, targetAngle), k);
            Rect.localScale = Vector3.Lerp(Rect.localScale, Vector3.one * targetScale, k);
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, targetAlpha, k);

            // 마우스 올림: 카드 기준 위쪽으로 살짝
            bool lift = hovered && !IsDrawn;
            Vector2 visualTarget = lift ? new Vector2(0f, HoverLift) : Vector2.zero;
            float visualScale = lift ? HoverScale : 1f;
            float vk = 1f - Mathf.Exp(-FollowSpeed * 1.5f * dt);
            visual.anchoredPosition = Vector2.Lerp(visual.anchoredPosition, visualTarget, vk);
            visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one * visualScale, vk);

            // 뽑힌 카드는 금빛 테두리 + 은은한 빛, 마우스 올린 카드는 테두리만 살짝
            float glowTarget = fadingOut ? 0f : (IsDrawn ? 0.55f : (lift ? 0.25f : 0f));
            glowAlpha = fadingOut ? Mathf.MoveTowards(glowAlpha, 0f, dt * 4f) : Mathf.Lerp(glowAlpha, glowTarget, vk);
            SetAlpha(glow, glowAlpha);
            outline.effectColor = Color.Lerp(OutlineIdle, OutlineHot, Mathf.Clamp01(glowAlpha * 2.5f));
        }

        // ================================================================ 도우미

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            }
            return img;
        }

        private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }
    }
}
