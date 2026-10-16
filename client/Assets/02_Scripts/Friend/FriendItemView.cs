using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Profile;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 내 친구 목록의 한 줄 (FriendItem 프리팹 루트에 부착).
    /// FriendUIController가 목록을 받을 때마다 프리팹을 생성하고 Bind()로 데이터를 넣는다.
    /// 삭제 버튼을 눌러도 실제 요청은 하지 않고 FriendUIController에 알리기만 한다.
    /// </summary>
    public class FriendItemView : MonoBehaviour
    {
        [Header("UI (FriendItem 프리팹의 자식)")]
        [SerializeField] private TMP_Text nicknameText;
        [Tooltip("레벨/전적 표시. 예) Lv.3 · 12전 7승 · 58.3%")]
        [SerializeField] private TMP_Text recordText;
        [Tooltip("(선택) 접속 상태 표시. 예) ● 온라인 / ● 게임 중. 비워 두면 닉네임 옆에 붙여 보여 준다.")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("(선택) 친구 아바타 이미지")]
        [SerializeField] private Image avatarImage;
        [SerializeField] private Button removeButton;

        // 접속 상태 색 (오프라인은 회색, 온라인은 초록, 대기실·게임 중은 주황)
        private static readonly Color OfflineColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color OnlineColor = new Color(0.30f, 0.80f, 0.35f);
        private static readonly Color BusyColor = new Color(0.95f, 0.65f, 0.20f);

        private FriendResponse friend;
        private Action<FriendResponse> onRemove;
        private Action<FriendResponse> onSelect;
        private Button rowButton;

        public FriendResponse Friend => friend;

        private void Awake()
        {
            if (removeButton != null && removeButton.onClick.GetPersistentEventCount() == 0)
                removeButton.onClick.AddListener(OnRemoveClicked);

            // 줄 전체를 눌러 프로필을 연다. (삭제 버튼은 자식이라 자기 클릭을 먼저 받는다)
            Image rowImage = GetComponent<Image>();
            if (rowImage != null)
            {
                rowImage.raycastTarget = true;
                rowButton = GetComponent<Button>();
                if (rowButton == null) rowButton = gameObject.AddComponent<Button>();
                rowButton.targetGraphic = rowImage;
                rowButton.onClick.AddListener(OnRowClicked);
            }
        }

        private void OnDestroy()
        {
            if (removeButton != null) removeButton.onClick.RemoveListener(OnRemoveClicked);
            if (rowButton != null) rowButton.onClick.RemoveListener(OnRowClicked);
        }

        private void OnRowClicked()
        {
            if (friend != null) onSelect?.Invoke(friend);
        }

        public void Bind(FriendResponse data, Action<FriendResponse> onRemoveClicked, Action<FriendResponse> onSelectClicked = null)
        {
            friend = data;
            onRemove = onRemoveClicked;
            onSelect = onSelectClicked;

            string statusRichText = $"<color=#{ColorUtility.ToHtmlStringRGB(StatusColor(data))}>● {data.StatusLabel}</color>";

            if (nicknameText != null)
                nicknameText.text = statusText != null ? data.nickname : $"{data.nickname}  {statusRichText}";
            AvatarCatalog.Apply(avatarImage, data.profileImageId);
            if (statusText != null) statusText.text = statusRichText;
            if (recordText != null)
                recordText.text = $"Lv.{data.level} · {data.playCount}전 {data.winCount}승 · {data.WinRatePercent:0.#}%";
        }

        internal static Color StatusColor(FriendResponse data)
        {
            switch (data.status)
            {
                case "IN_GAME":
                case "IN_ROOM": return BusyColor;
                case "ONLINE": return OnlineColor;
                case "OFFLINE": return OfflineColor;
                default: return data.online ? OnlineColor : OfflineColor;
            }
        }

        /// <summary>요청 중에 다른 항목을 누르지 못하도록 버튼을 잠그거나 푼다.</summary>
        public void SetLocked(bool value)
        {
            if (removeButton != null) removeButton.interactable = !value;
            if (rowButton != null) rowButton.interactable = !value;
        }

        public void OnRemoveClicked()
        {
            if (friend != null) onRemove?.Invoke(friend);
        }
    }
}
