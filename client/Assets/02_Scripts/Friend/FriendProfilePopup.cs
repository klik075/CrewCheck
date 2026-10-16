using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Profile;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 친구 프로필 팝업 (내 프로필 창과 같은 정보, 읽기 전용: 아바타·닉네임 변경 없음). 코드로 UI를 만든다.
    ///
    ///   popup = FriendProfilePopup.Create(parentCanvasTransform, fontSource, buttonSource);
    ///   popup.Open(friendResponse);
    ///
    /// 골드는 친구 목록 응답에 없고 남에게 보일 정보도 아니라서 뺐다. 닫기 버튼 / 바깥 영역 / ESC로 닫는다.
    /// </summary>
    public class FriendProfilePopup : MonoBehaviour
    {
        private Image avatar;
        private TMP_Text nickname, status, level, record, winRate;

        public bool IsOpen => gameObject.activeSelf;

        public static FriendProfilePopup Create(Transform parent, TMP_Text fontSource, Image buttonSource)
        {
            var root = new GameObject("Popup_FriendProfile", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.layer = 5;
            root.transform.SetParent(parent, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var popup = root.AddComponent<FriendProfilePopup>();

            RectTransform panel = FriendConfirmPopup.NewImage("Background", rootRt, new Color(0.10f, 0.12f, 0.16f, 0.98f), new Vector2(600f, 620f));

            RectTransform av = FriendConfirmPopup.NewImage("Avatar", panel, Color.white, new Vector2(140f, 140f));
            av.anchoredPosition = new Vector2(0f, 200f);
            popup.avatar = av.GetComponent<Image>();
            popup.avatar.preserveAspect = true;
            popup.avatar.raycastTarget = false;

            popup.nickname = FriendConfirmPopup.NewText("Nickname", panel, fontSource, 40f, new Vector2(520f, 56f), new Vector2(0f, 92f));
            popup.status = FriendConfirmPopup.NewText("Status", panel, fontSource, 28f, new Vector2(520f, 40f), new Vector2(0f, 48f));

            popup.level = Row(panel, fontSource, "레벨", -10f);
            popup.record = Row(panel, fontSource, "전적", -80f);
            popup.winRate = Row(panel, fontSource, "승률", -150f);

            Button close = FriendConfirmPopup.NewButton("Button_Close", panel, "닫기", fontSource, buttonSource, new Vector2(0f, -250f));
            close.onClick.AddListener(popup.Close);

            root.SetActive(false);
            return popup;
        }

        // 왼쪽 제목 + 오른쪽 값 한 줄. 값 텍스트를 돌려준다.
        private static TMP_Text Row(Transform parent, TMP_Text fontSource, string title, float y)
        {
            TMP_Text label = FriendConfirmPopup.NewText("Label_" + title, parent, fontSource, 32f, new Vector2(200f, 50f), new Vector2(-150f, y));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = new Color(1f, 1f, 1f, 0.7f);
            label.text = title;

            TMP_Text value = FriendConfirmPopup.NewText("Value_" + title, parent, fontSource, 34f, new Vector2(300f, 50f), new Vector2(100f, y));
            value.alignment = TextAlignmentOptions.MidlineRight;
            return value;
        }

        private void Update()
        {
            if (FriendConfirmPopup.EscapePressed()) Close();
        }

        public void Open(FriendResponse friend)
        {
            AvatarCatalog.Apply(avatar, friend.profileImageId);
            nickname.text = friend.nickname;
            status.text = $"<color=#{ColorUtility.ToHtmlStringRGB(FriendItemView.StatusColor(friend))}>● {friend.StatusLabel}</color>";
            level.text = $"Lv.{friend.level}";
            record.text = $"{friend.playCount}전 {friend.winCount}승 {Mathf.Max(0, friend.playCount - friend.winCount)}패";
            winRate.text = $"{friend.WinRatePercent:0.#}%";

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }
    }
}
