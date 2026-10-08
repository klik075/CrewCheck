using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Vote
{
    /// <summary>
    /// VoteUITest 씬 전용: 서버·로그인 없이 투표 카드패(VoteCardHandController) 연출만 확인한다.
    /// 화면(캔버스, 상단 정보, 조작 버튼, 하단 탭 바 자리)은 실행할 때 코드로 만든다.
    ///
    /// 조작
    ///  - 투표 시작 : 카드패가 아래에서 올라온다 (타이머 시작)
    ///  - 투표 종료 : 카드패가 내려가고 뽑은 카드가 흐려지며 사라진다 (타이머가 0이 되어도 같다)
    ///  - 인원 −/+  : 다음 투표 시작 때 카드 수 (나를 뺀 나머지가 카드가 된다)
    ///  - 한 명 이탈 : 투표 도중 연결이 끊긴 플레이어처럼 카드 한 장을 뺀다 (뽑은 카드가 있으면 그 카드)
    ///  - 자동 종료 : 켜면 voteSeconds가 지나면 저절로 투표 종료
    /// 카드를 뽑아도 서버로 보내지 않는다. 화면 위쪽 기록에만 남긴다.
    /// </summary>
    public sealed class VoteUITestBootstrap : MonoBehaviour
    {
        [Header("모양")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Sprite[] portraits;

        [Header("테스트 설정")]
        [SerializeField, Range(2, 12)] private int playerCount = 8;
        [SerializeField] private float voteSeconds = 20f;
        [SerializeField] private bool autoEnd = true;
        [SerializeField] private bool startOnPlay = true;

        private static readonly string[] MockNames =
            { "나", "철수", "영희", "민수", "지훈", "수진", "현우", "유나", "도윤", "서연", "하준", "지아" };

        private const float TopBarHeight = 120f;
        private const float TabBarHeight = 140f;
        private static readonly Color BackgroundColor = new Color32(0x14, 0x16, 0x1C, 0xFF);
        private static readonly Color BarColor = new Color32(0x23, 0x26, 0x2F, 0xFF);
        private static readonly Color ButtonColor = new Color32(0x34, 0x39, 0x47, 0xFF);
        private static readonly Color AccentColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);

        private readonly List<PlayerView> targets = new List<PlayerView>();
        private readonly List<string> logLines = new List<string>();

        private VoteCardHandController hand;
        private TextMeshProUGUI timerText;
        private TextMeshProUGUI countText;
        private TextMeshProUGUI autoEndText;
        private TextMeshProUGUI logText;
        private float remaining = -1f;

        // ================================================================ Unity

        private void Start()
        {
            EnsureEventSystem();
            Build();
            Log("서버 연동 없음 · 카드를 뽑아도 투표가 전송되지 않습니다.");
            if (startOnPlay)
            {
                StartVote();
            }
        }

        private void Update()
        {
            if (remaining < 0f)
            {
                return;
            }
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                remaining = -1f;
                timerText.text = "투표 종료";
                hand.Hide();
                Log("시간 종료 → 카드패가 내려갑니다.");
                return;
            }
            int s = Mathf.CeilToInt(remaining);
            timerText.text = string.Format("투표 {0:00}:{1:00}", s / 60, s % 60);
        }

        // ================================================================ 조작

        private void StartVote()
        {
            targets.Clear();
            for (int i = 1; i < playerCount; i++) // 0번 "나"는 카드패에 넣지 않는다
            {
                targets.Add(new PlayerView(i + 1, MockNames[i % MockNames.Length], true));
            }
            hand.Show(targets, PortraitOf, 0);
            remaining = autoEnd ? voteSeconds : -1f;
            timerText.text = autoEnd ? string.Empty : "투표 중 (자동 종료 꺼짐)";
            Log("투표 시작: 카드 " + targets.Count + "장");
        }

        private void EndVote()
        {
            if (!hand.IsShown)
            {
                return;
            }
            remaining = -1f;
            timerText.text = "투표 종료";
            hand.Hide();
            Log("투표 종료 → 카드패가 내려갑니다.");
        }

        private void ChangeCount(int delta)
        {
            playerCount = Mathf.Clamp(playerCount + delta, 2, 12);
            countText.text = "인원 " + playerCount;
        }

        private void RemoveOne()
        {
            if (!hand.IsShown || targets.Count == 0)
            {
                return;
            }
            int index = targets.Count - 1;
            long drawnId = hand.DrawnPlayerId;
            if (drawnId != 0)
            {
                index = targets.FindIndex(p => p.playerId == drawnId);
            }
            Log(targets[index].nickname + "님 이탈" + (drawnId != 0 ? " (뽑아 둔 카드가 사라집니다)" : string.Empty));
            targets.RemoveAt(index);
            hand.SyncPlayers(targets);
        }

        private void ToggleAutoEnd()
        {
            autoEnd = !autoEnd;
            autoEndText.text = autoEnd ? "자동 종료: 켬" : "자동 종료: 끔";
            if (!autoEnd && remaining > 0f)
            {
                remaining = -1f;
                timerText.text = "투표 중 (자동 종료 꺼짐)";
            }
        }

        private void OnVoteRequested(long playerId)
        {
            PlayerView p = targets.Find(t => t.playerId == playerId);
            Log((p != null ? p.nickname : "#" + playerId) + " 카드를 뽑음 (전송 안 함)");
        }

        private Sprite PortraitOf(long playerId)
        {
            if (portraits == null || portraits.Length == 0)
            {
                return null;
            }
            return portraits[(int)((playerId - 1) % portraits.Length)];
        }

        private void Log(string line)
        {
            logLines.Add(line);
            if (logLines.Count > 4)
            {
                logLines.RemoveAt(0);
            }
            if (logText != null)
            {
                logText.text = string.Join("\n", logLines.ToArray());
            }
        }

        // ================================================================ 화면 만들기

        private void Build()
        {
            var canvasGo = new GameObject("VoteUITestCanvas", typeof(RectTransform));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            Camera cam = Camera.main;
            if (cam != null)
            {
                // 카메라 기준으로 그린다 (에디터 카메라 캡처에도 UI가 함께 찍힌다)
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 5f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f); // 게임 씬과 같은 세로 모바일 기준
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform root = canvasGo.transform;

            Stretch(NewImage("Background", root, BackgroundColor), 0f, 0f, 0f, 0f);

            // 상단: 제목 + 타이머
            RectTransform top = NewImage("TopBar", root, BarColor).rectTransform;
            AnchorTop(top, TopBarHeight, 0f);
            TextMeshProUGUI title = NewText("Title", top, "투표 UI 연출 테스트", 40f, TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, 40f, 0f, 400f, 0f);
            timerText = NewText("Timer", top, string.Empty, 40f, TextAlignmentOptions.MidlineRight);
            timerText.color = AccentColor;
            Stretch(timerText.rectTransform, 500f, 0f, 40f, 0f);

            // 조작 버튼 두 줄
            RectTransform row1 = NewRow("Controls1", root, TopBarHeight + 20f);
            NewButton("Start", row1, "투표 시작", StartVote, 260f);
            NewButton("End", row1, "투표 종료", EndVote, 260f);
            NewButton("Remove", row1, "한 명 이탈", RemoveOne, 260f);

            RectTransform row2 = NewRow("Controls2", root, TopBarHeight + 140f);
            NewButton("Minus", row2, "인원 −", () => ChangeCount(-1), 170f);
            countText = NewText("Count", row2, "인원 " + playerCount, 34f, TextAlignmentOptions.Center);
            countText.rectTransform.sizeDelta = new Vector2(170f, 90f);
            NewButton("Plus", row2, "인원 +", () => ChangeCount(1), 170f);
            autoEndText = NewButton("AutoEnd", row2, autoEnd ? "자동 종료: 켬" : "자동 종료: 끔", ToggleAutoEnd, 300f);

            // 기록
            logText = NewText("Log", root, string.Empty, 28f, TextAlignmentOptions.TopLeft);
            logText.color = new Color(0.75f, 0.8f, 0.9f, 1f);
            AnchorTop(logText.rectTransform, 200f, TopBarHeight + 260f);
            logText.rectTransform.offsetMin = new Vector2(40f, logText.rectTransform.offsetMin.y);
            logText.rectTransform.offsetMax = new Vector2(-40f, logText.rectTransform.offsetMax.y);

            // 하단 탭 바 자리 (게임 씬처럼 카드패가 이 위에 놓인다)
            RectTransform tabBar = NewImage("TabBar_Placeholder", root, BarColor).rectTransform;
            tabBar.anchorMin = new Vector2(0f, 0f);
            tabBar.anchorMax = new Vector2(1f, 0f);
            tabBar.pivot = new Vector2(0.5f, 0f);
            tabBar.offsetMin = Vector2.zero;
            tabBar.offsetMax = new Vector2(0f, TabBarHeight);
            TextMeshProUGUI tabLabel = NewText("Label", tabBar, "(하단 탭 바 자리)", 30f, TextAlignmentOptions.Center);
            tabLabel.color = new Color(1f, 1f, 1f, 0.35f);
            Stretch(tabLabel.rectTransform, 0f, 0f, 0f, 0f);

            // 카드패
            var handGo = new GameObject("VoteCardHand", typeof(RectTransform));
            handGo.layer = canvasGo.layer;
            handGo.transform.SetParent(root, false);
            hand = handGo.AddComponent<VoteCardHandController>();
            hand.SetFont(font);
            hand.SetBottomAnchor(tabBar);
            hand.VoteRequested += OnVoteRequested;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private RectTransform NewRow(string name, Transform parent, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AnchorTop(rt, 100f, top);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rt;
        }

        private TextMeshProUGUI NewButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width)
        {
            Image img = NewImage(name, parent, ButtonColor);
            img.raycastTarget = true;
            img.rectTransform.sizeDelta = new Vector2(width, 90f);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            TextMeshProUGUI text = NewText("Label", img.rectTransform, label, 32f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            return text;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(Graphic g, float left, float bottom, float right, float top)
        {
            Stretch(g.rectTransform, left, bottom, right, top);
        }

        private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorTop(RectTransform rt, float height, float fromTop)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -fromTop - height);
            rt.offsetMax = new Vector2(0f, -fromTop);
        }
    }
}
