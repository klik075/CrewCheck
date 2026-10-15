using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// [채팅] 버튼 오른쪽 위의 미확인 채팅 표시(빨간 점). 로비(LobbyChatPanel)와 룸(RoomLobbyLayout)이 같이 쓴다.
    ///  - 채팅창이 닫혀 있는 동안 새 채팅이 오면 Show(true), 채팅창을 열면 Show(false).
    ///  - 미확인 채팅이 몇 개든 점은 하나만 찍힌다. (개수는 세지 않는다)
    /// </summary>
    public sealed class ChatUnreadDot : MonoBehaviour
    {
        private const float DotSize = 26f;      // 빨간 점 지름
        private const float OutlineSize = 32f;  // 어두운 테두리 지름
        private const float Inset = 4f;         // 버튼 오른쪽 위 모서리에서 안쪽으로 들어온 거리 (점 중심 기준)

        private static readonly Color DotColor = new Color32(214, 38, 32, 255);
        private static readonly Color OutlineColor = new Color32(40, 20, 13, 255);

        private GameObject dot;

        public bool Visible => dot != null && dot.activeSelf;

        /// <summary>버튼에 빨간 점을 붙인다. 처음에는 숨겨져 있다.</summary>
        public static ChatUnreadDot Attach(Button button)
        {
            var host = button.gameObject.GetComponent<ChatUnreadDot>();
            if (host == null) host = button.gameObject.AddComponent<ChatUnreadDot>();
            host.Build();
            return host;
        }

        public void Show(bool visible)
        {
            if (dot != null && dot.activeSelf != visible) dot.SetActive(visible);
        }

        private void Build()
        {
            if (dot != null) return;

            var root = NewRect("UnreadDot", transform);
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f); // 버튼 오른쪽 위
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(-Inset, -Inset);
            root.sizeDelta = new Vector2(OutlineSize, OutlineSize);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            Disc("Outline", root, OutlineSize, OutlineColor);
            Disc("Dot", root, DotSize, DotColor);

            root.SetAsLastSibling(); // 글자·아이콘 위에 그린다
            dot = root.gameObject;
            dot.SetActive(false);
        }

        private static void Disc(string name, Transform parent, float size, Color color)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            // RoomHelmGraphic.Disc는 크기의 0.46배 반지름으로 그리므로 지름이 size가 되도록 키운다
            rt.sizeDelta = Vector2.one * (size / 0.92f);
            var graphic = rt.gameObject.AddComponent<RoomHelmGraphic>();
            graphic.Shape = RoomHelmGraphic.Symbol.Disc;
            graphic.color = color;
            graphic.raycastTarget = false;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            return rt;
        }
    }
}
