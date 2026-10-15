using UnityEngine;

/// <summary>
/// 알림 점(빨간 점). 버튼·아이콘 오른쪽 위에 붙여 "확인하지 않은 것이 있다"를 표시한다. 개수는 세지 않고 켜짐/꺼짐만 있다.
/// 프리팹: Assets/Resources/Common/NotificationDot.prefab
///
/// ── 에디터에서 배치하기 (팀원용) ──────────────────────────────────────────
///  1) Hierarchy에서 점을 붙일 UI 오브젝트(버튼 등)를 오른쪽 클릭 → UI → Notification Dot (알림 점)
///     (또는 Project 창의 프리팹을 그 오브젝트 아래로 끌어다 놓기)
///  2) 크기·위치는 NotificationDot의 RectTransform에서, 색·이미지는 Visual 아래 Outline·Dot의 Image에서 조정한다.
///     프리팹 자체를 고치면 모든 곳의 점이 함께 바뀐다.
///  3) 점을 켜고 끌 스크립트에 필드를 만들고 Inspector에서 연결한다.
///        [SerializeField] private NotificationDot friendDot;
///        friendDot.Show();   // 알림이 생겼을 때
///        friendDot.Hide();   // 확인했을 때
///
///  - 에디터에서는 보이는 채로 배치하고, 플레이하면 처음에 숨겨진다. (Hide On Start)
///  - 컴포넌트 오른쪽 위 ⋮ 메뉴의 "미리보기: 보이기/숨기기"로 에디터에서 켜고 끌 수 있다.
///
/// ── 코드로 붙이기 (UI를 코드로 만드는 화면용) ────────────────────────────
///        var dot = NotificationDot.AttachTo((RectTransform)button.transform);
///        dot.Show(hasUnread);
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/Notification Dot (알림 점)")]
public sealed class NotificationDot : MonoBehaviour
{
    /// <summary>Resources 기준 프리팹 경로</summary>
    public const string ResourcePath = "Common/NotificationDot";

    [Tooltip("켜고 끌 그림. 비워 두면 첫 번째 자식(Visual)을 쓴다.")]
    [SerializeField] private GameObject visual;

    [Tooltip("플레이를 시작하면 숨긴다. 에디터에서는 보이는 채로 배치할 수 있다.")]
    [SerializeField] private bool hideOnStart = true;

    private static GameObject prefab;
    private bool initialized;

    /// <summary>지금 점이 보이는가</summary>
    public bool IsVisible => Visual != null && Visual.activeSelf;

    private GameObject Visual
    {
        get
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0).gameObject;
            return visual;
        }
    }

    private void Awake()
    {
        Initialize();
    }

    /// <summary>처음 한 번 hideOnStart를 적용한다. Awake 전에 Show가 불려도 그 값이 덮이지 않게 Show에서도 부른다.</summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (hideOnStart && Visual != null) Visual.SetActive(false);
    }

    /// <summary>점을 켜거나 끈다.</summary>
    public void Show(bool visible)
    {
        Initialize();
        if (Visual != null && Visual.activeSelf != visible) Visual.SetActive(visible);
    }

    public void Show() => Show(true);

    public void Hide() => Show(false);

    /// <summary>
    /// 코드로 만든 UI에 점을 붙인다. target의 오른쪽 위에 프리팹을 하나 만들고 돌려준다. (처음에는 숨김)
    /// 이미 붙어 있으면 그것을 돌려준다.
    /// </summary>
    public static NotificationDot AttachTo(RectTransform target)
    {
        if (target == null) return null;

        for (int i = 0; i < target.childCount; i++)
        {
            var existing = target.GetChild(i).GetComponent<NotificationDot>();
            if (existing != null) return existing;
        }

        if (prefab == null) prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[NotificationDot] 프리팹을 찾을 수 없습니다: Resources/" + ResourcePath + ".prefab");
            return null;
        }

        // 프리팹의 앵커·위치·크기를 그대로 쓴다 (worldPositionStays = false)
        var go = Instantiate(prefab, target, false);
        go.name = prefab.name;
        go.transform.SetAsLastSibling(); // 글자·아이콘 위에 그린다
        return go.GetComponent<NotificationDot>();
    }

    [ContextMenu("미리보기: 보이기")]
    private void PreviewShow()
    {
        if (Visual != null) Visual.SetActive(true);
    }

    [ContextMenu("미리보기: 숨기기")]
    private void PreviewHide()
    {
        if (Visual != null) Visual.SetActive(false);
    }
}
