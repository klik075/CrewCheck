using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 친구 삭제 등에 쓰는 예/아니오 확인 팝업. 씬에 오브젝트를 만들 필요 없이 코드로 UI를 만든다.
    ///
    ///   popup = FriendConfirmPopup.Create(parentCanvasTransform, fontSource, buttonSource);
    ///   popup.Open("철수님을 친구에서 삭제할까요?", () => { ...실제 요청... });
    ///
    /// 확인 → 닫고 onConfirm 호출. 취소 / 바깥 영역 / ESC → 닫기만 한다.
    /// fontSource(한글 폰트)와 buttonSource(버튼 스프라이트)는 기존 UI에서 빌려 쓴다.
    /// </summary>
    public class FriendConfirmPopup : MonoBehaviour
    {
        private TMP_Text messageText;
        private Action onConfirm;

        public bool IsOpen => gameObject.activeSelf;

        public static FriendConfirmPopup Create(Transform parent, TMP_Text fontSource, Image buttonSource)
        {
            var root = new GameObject("Popup_FriendConfirm", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.layer = 5;
            root.transform.SetParent(parent, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // 뒤쪽 클릭을 막는 어두운 배경

            var popup = root.AddComponent<FriendConfirmPopup>();

            var panel = NewImage("Background", rootRt, new Color(0.10f, 0.12f, 0.16f, 0.98f), new Vector2(560f, 280f));
            popup.messageText = NewText("Message", panel, fontSource, 32f, new Vector2(500f, 130f), new Vector2(0f, 45f));

            Button confirm = NewButton("Button_Confirm", panel, "삭제", fontSource, buttonSource, new Vector2(-120f, -85f));
            Button cancel = NewButton("Button_Cancel", panel, "취소", fontSource, buttonSource, new Vector2(120f, -85f));
            confirm.onClick.AddListener(popup.OnConfirmClicked);
            cancel.onClick.AddListener(popup.Close);

            root.SetActive(false);
            return popup;
        }

        private void Update()
        {
            if (EscapePressed()) Close();
        }

        public void Open(string message, Action confirm)
        {
            onConfirm = confirm;
            if (messageText != null) messageText.text = message;
            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // 다른 팝업 위에 보이게
        }

        public void Close()
        {
            onConfirm = null;
            gameObject.SetActive(false);
        }

        private void OnConfirmClicked()
        {
            Action confirm = onConfirm; // Close()가 콜백을 비우므로 먼저 꺼내 둔다
            Close();
            confirm?.Invoke();
        }

        // ------------------------------------------------------------------
        // UI 조립 도우미
        // ------------------------------------------------------------------

        internal static RectTransform NewImage(string name, Transform parent, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        internal static TMP_Text NewText(string name, Transform parent, TMP_Text fontSource, float size, Vector2 sizeDelta, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            var t = go.GetComponent<TMP_Text>();
            if (fontSource != null) t.font = fontSource.font; // 기본 폰트에는 한글이 없다
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        internal static Button NewButton(string name, Transform parent, string label, TMP_Text fontSource, Image buttonSource, Vector2 pos)
        {
            RectTransform rt = NewImage(name, parent, Color.white, new Vector2(200f, 64f));
            rt.anchoredPosition = pos;
            var img = rt.GetComponent<Image>();
            if (buttonSource != null)
            {
                img.sprite = buttonSource.sprite;
                img.type = buttonSource.type;
                img.color = buttonSource.color;
            }
            else
            {
                img.color = new Color(0.3f, 0.3f, 0.35f);
            }

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            NewText("Label", rt, fontSource, 30f, new Vector2(200f, 64f), Vector2.zero).text = label;
            return button;
        }

        internal static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
