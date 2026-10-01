using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    // 채팅 기록 스크롤 (시스템 메시지 / 플레이어 채팅). 하단 시트 뒤에 깔리고, 시트가 올라오면 아래 끝이 함께 올라간다.
    //
    // Content는 Viewport 하단에 붙어 있다(anchor/pivot y = 0). 그래서
    //  - 시트가 올라와 Viewport가 줄어들어도 최신 메시지(맨 아래)가 계속 보이고
    //  - 메시지가 적을 때는 아래쪽부터 쌓인다.
    // 이미지 메시지(역할 카드 등)는 나중에 메시지 종류별 프리팹을 추가해 AddItem으로 넣으면 된다.
    public class ChatLogView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private TextMeshProUGUI linePrefab;
        [SerializeField] private int maxItems = 200;

        [Header("Colors")]
        [SerializeField] private Color systemColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);

        [Tooltip("기록 영역을 탭(드래그 아님)했을 때. 보통 BottomTabController.Close를 연결해 패널을 내린다.")]
        [SerializeField] private UnityEvent onTapped = new UnityEvent();

        public UnityEvent OnTapped => onTapped;

        // 시스템 메시지: "갑판장이 처형당했습니다", "역할이 배정되었습니다" 등
        public void AddSystemMessage(string text)
        {
            string hex = ColorUtility.ToHtmlStringRGB(systemColor);
            AddLine($"<color=#{hex}>[시스템] {text}</color>");
        }

        // 플레이어 채팅: "원우  1번이 범인인 듯"
        public void AddPlayerMessage(string nickname, string text, bool isMe = false)
        {
            string name = isMe ? $"<color=#6EA8FF><b>{nickname}</b></color>" : $"<b>{nickname}</b>";
            AddLine($"{name}  {Escape(text)}");
        }

        public TextMeshProUGUI AddLine(string richText)
        {
            TextMeshProUGUI line = Instantiate(linePrefab, content);
            line.text = richText;
            AddItem(line.transform);
            return line;
        }

        // 메시지 종류별 프리팹(이미지 등)을 직접 만들어 넣을 때 사용
        public void AddItem(Transform item)
        {
            if (item.parent != content) item.SetParent(content, false);
            while (content.childCount > maxItems)
                DestroyImmediate(content.GetChild(0).gameObject);
        }

        public void ScrollToLatest()
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        public void Clear()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!eventData.dragging) onTapped?.Invoke();
        }

        // 플레이어가 입력한 문자열의 리치 텍스트 태그를 무력화한다.
        private static string Escape(string text) => text.Replace("<", "<​");
    }
}
