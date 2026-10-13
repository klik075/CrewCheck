using UnityEngine;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// RectTransform을 기기의 Safe Area(노치, 펀치홀, 홈 인디케이터를 피한 영역)에 맞춘다.
    ///
    /// 사용법
    ///   Canvas 바로 아래에 "SafeArea" 빈 오브젝트를 만들고 이 컴포넌트를 붙인다.
    ///   버튼/텍스트/입력창 등 조작 UI는 전부 SafeArea 아래에 둔다.
    ///   배경 이미지는 SafeArea 밖(Canvas 바로 아래)에 둬서 화면 끝까지 채운다.
    ///
    /// - Screen.safeArea를 화면 크기 대비 비율로 바꿔 anchorMin/anchorMax에 넣는다.
    ///   그래서 부모는 화면 전체를 덮는 RectTransform(= Canvas 루트)이어야 한다.
    /// - 화면 회전, 해상도 변경, Device Simulator 기기 변경 시 자동으로 다시 계산한다.
    /// - 방향별 적용 여부를 끌 수 있다. (예: 상단 바는 Top만, 하단 입력창은 Bottom만)
    /// - 플레이 중에만 동작한다. (에디터에서 씬 파일의 앵커 값을 바꾸지 않기 위해)
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Header("적용할 방향")]
        [SerializeField] private bool applyTop = true;
        [SerializeField] private bool applyBottom = true;
        [SerializeField] private bool applyLeft = true;
        [SerializeField] private bool applyRight = true;

        [Header("Debug")]
        [Tooltip("Safe Area가 바뀔 때마다 콘솔에 값을 출력한다.")]
        [SerializeField] private bool logChanges = false;

        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private ScreenOrientation lastOrientation;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            Apply(force: true);
        }

        private void Update()
        {
            // 회전/해상도/시뮬레이터 기기 변경 감지. 값 비교만 하므로 비용은 거의 없다.
            Apply(force: false);
        }

        /// <summary>
        /// Safe Area를 다시 계산해서 적용한다. 값이 그대로면 아무것도 하지 않는다.
        /// </summary>
        public void Apply(bool force = false)
        {
            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
            ScreenOrientation orientation = Screen.orientation;

            if (!force
                && safeArea == lastSafeArea
                && screenSize == lastScreenSize
                && orientation == lastOrientation)
            {
                return;
            }

            if (screenSize.x <= 0 || screenSize.y <= 0)
            {
                return; // 창이 최소화된 경우 등
            }

            lastSafeArea = safeArea;
            lastScreenSize = screenSize;
            lastOrientation = orientation;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= screenSize.x;
            anchorMin.y /= screenSize.y;
            anchorMax.x /= screenSize.x;
            anchorMax.y /= screenSize.y;

            // 적용하지 않는 방향은 화면 끝(0 또는 1)으로 되돌린다.
            if (!applyLeft) anchorMin.x = 0f;
            if (!applyBottom) anchorMin.y = 0f;
            if (!applyRight) anchorMax.x = 1f;
            if (!applyTop) anchorMax.y = 1f;

            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            if (logChanges)
            {
                Debug.Log($"[SafeAreaFitter] {name} screen={screenSize} safeArea={safeArea} " +
                          $"anchorMin={anchorMin} anchorMax={anchorMax}");
            }
        }
    }
}
