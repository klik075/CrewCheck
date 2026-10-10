using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 받은 친구 요청 목록의 한 줄 (FriendRequestItem 프리팹 루트에 부착).
    /// 수락/거절 버튼을 눌러도 실제 요청은 하지 않고 FriendUIController에 알리기만 한다.
    /// </summary>
    public class FriendRequestItemView : MonoBehaviour
    {
        [Header("UI (FriendRequestItem 프리팹의 자식)")]
        [SerializeField] private TMP_Text nicknameText;
        [Tooltip("(선택) 요청 시각 표시")]
        [SerializeField] private TMP_Text requestedAtText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button rejectButton;

        private FriendRequestResponse request;
        private Action<FriendRequestResponse> onAccept;
        private Action<FriendRequestResponse> onReject;

        public FriendRequestResponse Request => request;

        private void Awake()
        {
            if (acceptButton != null && acceptButton.onClick.GetPersistentEventCount() == 0)
                acceptButton.onClick.AddListener(OnAcceptClicked);
            if (rejectButton != null && rejectButton.onClick.GetPersistentEventCount() == 0)
                rejectButton.onClick.AddListener(OnRejectClicked);
        }

        private void OnDestroy()
        {
            if (acceptButton != null) acceptButton.onClick.RemoveListener(OnAcceptClicked);
            if (rejectButton != null) rejectButton.onClick.RemoveListener(OnRejectClicked);
        }

        public void Bind(FriendRequestResponse data, Action<FriendRequestResponse> onAcceptClicked,
            Action<FriendRequestResponse> onRejectClicked)
        {
            request = data;
            onAccept = onAcceptClicked;
            onReject = onRejectClicked;

            if (nicknameText != null) nicknameText.text = data.requesterNickname;
            if (requestedAtText != null) requestedAtText.text = FormatTime(data.requestedAt);
        }

        /// <summary>요청 중에 다른 항목을 누르지 못하도록 버튼을 잠그거나 푼다.</summary>
        public void SetLocked(bool value)
        {
            if (acceptButton != null) acceptButton.interactable = !value;
            if (rejectButton != null) rejectButton.interactable = !value;
        }

        public void OnAcceptClicked()
        {
            if (request != null) onAccept?.Invoke(request);
        }

        public void OnRejectClicked()
        {
            if (request != null) onReject?.Invoke(request);
        }

        // ISO 문자열("2026-10-10T12:34:56.789")을 "10-10 12:34"로 줄인다. 해석하지 못하면 원문 그대로.
        private static string FormatTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;
            return DateTime.TryParse(iso, out DateTime time) ? time.ToString("MM-dd HH:mm") : iso;
        }
    }
}
