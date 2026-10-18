using UnityEngine;
using UnityEngine.EventSystems;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 화면이 아무리 가로로 넓어져도 이 RectTransform의 폭을 세로 비율(기본 9:16)까지만 허용한다.
    /// 넓은 화면(웹 전체화면 등)에서는 가운데 9:16 영역만 쓰고, 양옆은 프레임 밖의 배경이 보인다.
    ///
    /// 사용법
    ///   Canvas 바로 아래에 이 컴포넌트를 붙인 빈 오브젝트를 두고,
    ///   늘어나면 안 되는 UI(SafeArea, HUD, 패널 등)를 그 안에 넣는다.
    ///   배경 이미지, 화면 전체를 덮는 팝업(어두운 막 + 가운데 창)은 프레임 밖에 둔다.
    ///   메뉴 Tools/UI/선택한 UI를 세로 프레임으로 감싸기 로 한 번에 만들 수 있다.
    ///
    /// - Canvas Scaler를 Scale With Screen Size 1080x1920, Screen Match Mode = Expand로 두면
    ///   넓은 화면에서 프레임은 정확히 1080x1920이 되고, 세로로 긴 폰에서는 화면 전체를 그대로 쓴다.
    /// - 위치/크기는 이 컴포넌트가 계속 덮어쓴다(인스펙터에서 회색으로 잠김).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class PortraitFrame : UIBehaviour
    {
        [Tooltip("허용할 최대 가로/세로 비율. 9:16 = 0.5625")]
        [SerializeField] private float maxAspect = 9f / 16f;

        private RectTransform rectTransform;
        private DrivenRectTransformTracker tracker;
        private Vector2 lastParentSize = new Vector2(-1f, -1f);

        protected override void OnEnable()
        {
            base.OnEnable();
            rectTransform = (RectTransform)transform;
            Apply(force: true);
        }

        protected override void OnDisable()
        {
            tracker.Clear();
            base.OnDisable();
        }

        private void LateUpdate()
        {
            Apply(force: false);
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            Apply(force: true);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            maxAspect = Mathf.Max(0.1f, maxAspect);
            if (isActiveAndEnabled) Apply(force: true);
        }
#endif

        /// <summary>부모 크기에 맞춰 프레임 폭을 다시 계산한다. 부모 크기가 그대로면 아무것도 하지 않는다.</summary>
        public void Apply(bool force = false)
        {
            if (rectTransform == null) rectTransform = (RectTransform)transform;
            var parent = rectTransform.parent as RectTransform;
            if (parent == null) return;

            Vector2 parentSize = parent.rect.size;
            if (!force && parentSize == lastParentSize) return;
            lastParentSize = parentSize;

            tracker.Clear();
            tracker.Add(this, rectTransform,
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.Pivot |
                DrivenTransformProperties.AnchoredPosition |
                DrivenTransformProperties.SizeDelta);

            // 세로는 부모 전체, 가로는 '부모 폭'과 '높이 x 9/16' 중 작은 쪽만큼 가운데에.
            float width = Mathf.Min(parentSize.x, parentSize.y * maxAspect);
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width), 0f);
        }
    }
}
