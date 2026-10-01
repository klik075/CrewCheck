using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 방 목록의 한 줄 (RoomItem 프리팹 루트에 부착).
    /// LobbyUIController가 방 목록을 받을 때마다 프리팹을 생성하고 Bind()로 데이터를 넣는다.
    ///
    /// 표시 규칙
    ///   - 대기 중이고 자리가 있으면: Enter 버튼 활성화
    ///   - 게임 중이면              : 버튼 비활성화, 버튼 글자 "게임 중"
    ///   - 정원이 찼으면            : 버튼 비활성화, 버튼 글자 "FULL"
    ///
    /// 버튼을 눌렀을 때 실제 입장 요청은 하지 않고 LobbyUIController에 알리기만 한다.
    /// (중복 클릭 방지, 상태 메시지, 씬 이동을 한 곳에서 처리하기 위해서)
    /// </summary>
    public class RoomItemView : MonoBehaviour
    {
        [Header("UI (RoomItem 프리팹의 자식)")]
        [SerializeField] private TMP_Text titleText;       // Title_Text (TMP)
        [SerializeField] private TMP_Text memberInfoText;  // MemberInfo_Text (TMP) (1)
        [SerializeField] private Button enterButton;       // Button_Enter
        [SerializeField] private TMP_Text enterButtonText; // Button_Enter/Text (TMP)

        [Header("Button Labels")]
        [SerializeField] private string enterLabel = "Enter";
        [SerializeField] private string inGameLabel = "게임 중";
        [SerializeField] private string fullLabel = "FULL";

        private RoomResponse room;              // 이 줄에 표시 중인 방
        private Action<RoomResponse> onEnter;   // Enter 클릭 시 호출할 콜백 (LobbyUIController.JoinRoom)
        private bool locked;                    // 다른 방에 입장 요청 중이라 잠근 상태

        /// <summary>이 줄에 표시 중인 방 정보</summary>
        public RoomResponse Room => room;

        private void Awake()
        {
            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            if (enterButton != null && enterButton.onClick.GetPersistentEventCount() == 0)
                enterButton.onClick.AddListener(OnEnterClicked);
        }

        private void OnDestroy()
        {
            if (enterButton != null) enterButton.onClick.RemoveListener(OnEnterClicked);
        }

        /// <summary>
        /// 방 데이터를 화면에 표시한다.
        /// </summary>
        /// <param name="data">표시할 방</param>
        /// <param name="onEnterClicked">Enter 버튼을 눌렀을 때 호출할 함수</param>
        public void Bind(RoomResponse data, Action<RoomResponse> onEnterClicked)
        {
            room = data;
            onEnter = onEnterClicked;

            if (titleText != null) titleText.text = data.title;

            // 예) "Member : 3/6"   게임 중이면 "Member : 5/6 (게임 중)"
            if (memberInfoText != null)
            {
                string info = $"Member : {data.currentPlayers}/{data.maxPlayers}";
                if (data.IsInGame) info += $" ({inGameLabel})";
                memberInfoText.text = info;
            }

            RefreshButton();
        }

        /// <summary>
        /// 입장 요청 중에 다른 방 버튼을 누르지 못하도록 잠그거나 푼다.
        /// 잠금을 풀어도 게임 중/정원 초과인 방은 계속 비활성화 상태로 남는다.
        /// </summary>
        public void SetLocked(bool value)
        {
            locked = value;
            RefreshButton();
        }

        // 방 상태와 잠금 여부에 따라 버튼 활성화와 글자를 정한다.
        private void RefreshButton()
        {
            if (room == null) return;

            if (enterButton != null) enterButton.interactable = room.CanJoin && !locked;

            if (enterButtonText != null)
            {
                enterButtonText.text = room.IsInGame ? inGameLabel
                                     : room.IsFull ? fullLabel
                                     : enterLabel;
            }
        }

        // Button_Enter 클릭 (인스펙터 OnClick에 직접 연결해도 된다)
        public void OnEnterClicked()
        {
            if (room == null || locked || !room.CanJoin) return;
            onEnter?.Invoke(room);
        }
    }
}
