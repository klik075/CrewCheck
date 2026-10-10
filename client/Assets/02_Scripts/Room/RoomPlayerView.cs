using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// Room 씬 PlayerList에 표시되는 참가자 한 줄 (03_Prefabs/Room/RoomPlayer 루트에 부착).
    ///
    /// 표시 예
    ///   [방장] 유진 (나)
    ///   철수 [준비]                  [⋯]   ← 내가 방장일 때 다른 사람 줄에만 관리 버튼이 보인다
    ///   관리 버튼을 누르면 RoomUIController가 플레이어 관리 메뉴(PlayerManageMenu)를 연다.
    ///
    /// nicknameText를 비워 두면 자식에서 첫 번째 TMP_Text(Title_Text)를 자동으로 찾는다.
    /// 프리팹에 이 컴포넌트를 붙이지 않아도 RoomUIController가 생성할 때 자동으로 붙인다.
    /// (자동으로 붙은 경우 버튼이 연결되지 않으므로 플레이어 관리 기능은 꺼진다)
    /// </summary>
    public class RoomPlayerView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText; // RoomPlayer/Title_Text (TMP)
        [Tooltip("준비 표시 오브젝트(선택). 비워 두면 닉네임 뒤에 readySuffix를 붙인다.")]
        [SerializeField] private GameObject readyIndicator;

        [Header("방장 전용 (선택, 비워 두면 기능만 꺼짐)")]
        [Tooltip("플레이어 관리 버튼(예: ⋯). 내가 방장이고 이 줄이 내가 아닐 때만 보인다. 누르면 관리 메뉴가 열린다.")]
        [SerializeField] private Button manageButton;

        [Header("Labels")]
        [SerializeField] private string hostPrefix = "[방장] ";
        [SerializeField] private string meSuffix = " (나)";
        [SerializeField] private string readySuffix = " [준비]";

        public long UserId { get; private set; }
        public string Nickname { get; private set; }

        private Action<RoomPlayerView> onManage;

        private void Awake()
        {
            EnsureText();
            // 줄은 목록이 바뀔 때마다 새로 만들어지므로 리스너는 한 번만 연결하고 콜백만 Bind에서 바꾼다.
            if (manageButton != null) manageButton.onClick.AddListener(() => onManage?.Invoke(this));
        }

        /// <summary>참가자 정보를 화면에 표시한다. (방장 전용 버튼 없음)</summary>
        public void Bind(RoomPlayerResponse player, bool isHost, bool isMe)
        {
            Bind(player, isHost, isMe, false, null);
        }

        /// <summary>참가자 정보를 화면에 표시한다.</summary>
        /// <param name="canManage">내가 방장이면 true. 이 줄이 내가 아닐 때만 관리 버튼을 보여 준다.</param>
        /// <param name="manage">관리 버튼 클릭 시 호출</param>
        public void Bind(RoomPlayerResponse player, bool isHost, bool isMe, bool canManage,
            Action<RoomPlayerView> manage)
        {
            if (player == null) return;
            EnsureText();

            UserId = player.userId;
            Nickname = string.IsNullOrEmpty(player.nickname) ? $"플레이어 {player.userId}" : player.nickname;

            // 방장은 준비하지 않으므로 표시하지 않는다.
            bool showReady = !isHost && player.ready;
            if (readyIndicator != null) readyIndicator.SetActive(showReady);
            string readyText = showReady && readyIndicator == null ? readySuffix : "";

            if (nicknameText != null)
                nicknameText.text = $"{(isHost ? hostPrefix : "")}{Nickname}{(isMe ? meSuffix : "")}{readyText}";

            onManage = manage;
            if (manageButton != null) manageButton.gameObject.SetActive(canManage && !isMe && manage != null);
            SetManageInteractable(true);
        }

        /// <summary>위임/추방 요청 중에는 관리 버튼을 잠근다.</summary>
        public void SetManageInteractable(bool value)
        {
            if (manageButton != null) manageButton.interactable = value;
        }

        private void EnsureText()
        {
            if (nicknameText == null) nicknameText = GetComponentInChildren<TMP_Text>(true);
        }
    }
}
