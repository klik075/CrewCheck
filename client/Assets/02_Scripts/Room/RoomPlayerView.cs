using TMPro;
using UnityEngine;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// Room 씬 PlayerList에 표시되는 참가자 한 줄 (03_Prefabs/Room/RoomPlayer 루트에 부착).
    ///
    /// 표시 예
    ///   [방장] 유진 (나)
    ///   철수
    ///
    /// nicknameText를 비워 두면 자식에서 첫 번째 TMP_Text(Title_Text)를 자동으로 찾는다.
    /// 프리팹에 이 컴포넌트를 붙이지 않아도 RoomUIController가 생성할 때 자동으로 붙인다.
    /// </summary>
    public class RoomPlayerView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText; // RoomPlayer/Title_Text (TMP)

        [Header("Labels")]
        [SerializeField] private string hostPrefix = "[방장] ";
        [SerializeField] private string meSuffix = " (나)";

        public long UserId { get; private set; }

        private void Awake()
        {
            EnsureText();
        }

        /// <summary>참가자 정보를 화면에 표시한다.</summary>
        public void Bind(RoomPlayerResponse player, bool isHost, bool isMe)
        {
            if (player == null) return;
            EnsureText();

            UserId = player.userId;
            string nickname = string.IsNullOrEmpty(player.nickname) ? $"플레이어 {player.userId}" : player.nickname;

            if (nicknameText != null)
                nicknameText.text = $"{(isHost ? hostPrefix : "")}{nickname}{(isMe ? meSuffix : "")}";
        }

        private void EnsureText()
        {
            if (nicknameText == null) nicknameText = GetComponentInChildren<TMP_Text>(true);
        }
    }
}
