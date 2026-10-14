using System;
using System.Collections.Generic;
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
    // 줄은 ChatEntry로 받는다. RowFactory(게임 화면의 해적 HUD가 넣는다)가 있으면 그 줄 모양(아바타·이름패·시간)으로,
    // 없으면(대기실 등) 줄 프리팹에 리치 텍스트 한 줄로 그린다.
    // 탭(전체 / 시스템 / 일반 / 귓속말)과 보낸 사람 필터는 SetFilter로 바꾼다. 걸러진 줄은 꺼 둔다.
    // 보낸 사람 필터는 전체·일반 탭에서만 쓴다. 전체 탭은 그 사람의 채팅 + 시스템·안내 줄(흐름을 보려고),
    // 일반 탭은 그 사람의 채팅만 보인다.
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

        // 줄 모양. 비우면 리치 텍스트 한 줄. 이미 그린 줄은 바뀌지 않는다
        public Func<ChatEntry, RectTransform> RowFactory { get; set; }

        // 줄이 늘거나 줄거나 필터가 바뀌었을 때 (빈 목록 안내 등)
        public event Action RowsChanged;

        public ChatTab Tab { get; private set; } = ChatTab.All;
        public string SpeakerFilter { get; private set; } // 이 닉네임의 플레이어 채팅만 (비우면 모두)

        private sealed class Row
        {
            public ChatEntry entry;
            public GameObject go;
        }

        private readonly List<Row> rows = new List<Row>();

        // 코드로 만든 채팅창(로비 채팅 등)에서 인스펙터 대신 연결한다. 줄을 넣기 전에 부른다
        public void Configure(ScrollRect scroll, RectTransform contentRoot, TextMeshProUGUI line)
        {
            scrollRect = scroll;
            content = contentRoot;
            linePrefab = line;
        }

        public int VisibleCount
        {
            get
            {
                int count = 0;
                foreach (Row row in rows) if (row.go != null && row.go.activeSelf) count++;
                return count;
            }
        }

        // 지금 기록에 있는 플레이어 닉네임 (처음 말한 순서)
        public List<string> Speakers()
        {
            var names = new List<string>();
            foreach (Row row in rows)
            {
                ChatEntry e = row.entry;
                if (e.Kind == ChatEntryKind.Player && !string.IsNullOrEmpty(e.Nickname) && !names.Contains(e.Nickname)) names.Add(e.Nickname);
            }
            return names;
        }

        // 시스템 메시지: "갑판장이 처형당했습니다", "역할이 배정되었습니다" 등
        public void AddSystemMessage(string text)
        {
            string hex = ColorUtility.ToHtmlStringRGB(systemColor);
            AddEntry(new ChatEntry
            {
                Kind = ChatEntryKind.System, Nickname = "시스템", Text = text, Time = DateTime.Now,
                Rich = $"<color=#{hex}>[시스템] {text}</color>"
            });
        }

        // 내 화면에만 보이는 안내·오류: "[알림] 투표할 수 없습니다" 등
        public void AddNotice(string label, string text, Color color)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            AddEntry(new ChatEntry
            {
                Kind = ChatEntryKind.Notice, Nickname = label, Text = text, TextColor = color, Time = DateTime.Now,
                Rich = $"<color=#{hex}>[{label}] {Escape(text)}</color>"
            });
        }

        // 플레이어 채팅: "원우  1번이 범인인 듯"
        public void AddPlayerMessage(string nickname, string text, bool isMe = false)
        {
            string name = isMe ? $"<color=#6EA8FF><b>{nickname}</b></color>" : $"<b>{nickname}</b>";
            AddEntry(new ChatEntry
            {
                Kind = ChatEntryKind.Player, Nickname = nickname, Text = text, Mine = isMe, Time = DateTime.Now,
                Rich = $"{name}  {Escape(text)}"
            });
        }

        // 리치 텍스트 한 줄 (안내 줄로 취급한다). 줄 모양이 있으면 그 모양의 TMP가 아니므로 null을 돌려준다
        public TextMeshProUGUI AddLine(string richText)
        {
            return AddEntry(new ChatEntry { Kind = ChatEntryKind.Notice, Time = DateTime.Now, Rich = richText });
        }

        public TextMeshProUGUI AddEntry(ChatEntry entry)
        {
            GameObject go;
            TextMeshProUGUI line = null;
            if (RowFactory != null)
            {
                RectTransform row = RowFactory(entry);
                row.SetParent(content, false);
                go = row.gameObject;
            }
            else
            {
                line = Instantiate(linePrefab, content);
                line.text = entry.Rich ?? Escape(entry.Text ?? string.Empty);
                go = line.gameObject;
            }
            rows.Add(new Row { entry = entry, go = go });
            go.SetActive(Visible(entry));
            Trim();
            RowsChanged?.Invoke();
            return line;
        }

        // 메시지 종류별 프리팹(이미지 등)을 직접 만들어 넣을 때 사용 (필터와 상관없이 늘 보인다)
        public void AddItem(Transform item)
        {
            if (item.parent != content) item.SetParent(content, false);
            Trim();
        }

        public void SetFilter(ChatTab tab, string speaker)
        {
            Tab = tab;
            SpeakerFilter = UsesSpeakerFilter(tab) && !string.IsNullOrEmpty(speaker) ? speaker : null;
            foreach (Row row in rows) if (row.go != null) row.go.SetActive(Visible(row.entry));
            ScrollToLatest();
            RowsChanged?.Invoke();
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
            rows.Clear();
            RowsChanged?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!eventData.dragging) onTapped?.Invoke();
        }

        // 보낸 사람 필터를 쓰는 탭 (전체·일반)
        public static bool UsesSpeakerFilter(ChatTab tab) => tab == ChatTab.All || tab == ChatTab.General;

        private bool Visible(ChatEntry e)
        {
            switch (Tab)
            {
                case ChatTab.System: return e.Kind == ChatEntryKind.System || e.Kind == ChatEntryKind.Notice;
                case ChatTab.General: return e.Kind == ChatEntryKind.Player && (SpeakerFilter == null || e.Nickname == SpeakerFilter);
                case ChatTab.Whisper: return e.Kind == ChatEntryKind.Whisper;
                default:
                    // 필터를 켜도 시스템·안내 줄은 남긴다: 그 사람이 어떤 상황에서 말했는지 보이게
                    return SpeakerFilter == null || e.Kind == ChatEntryKind.System || e.Kind == ChatEntryKind.Notice
                           || e.Nickname == SpeakerFilter;
            }
        }

        private void Trim()
        {
            while (content.childCount > maxItems)
            {
                GameObject oldest = content.GetChild(0).gameObject;
                rows.RemoveAll(r => r.go == oldest);
                DestroyImmediate(oldest);
            }
        }

        // 플레이어가 입력한 문자열의 리치 텍스트 태그를 무력화한다.
        private static string Escape(string text) => text.Replace("<", "<​");
    }
}
