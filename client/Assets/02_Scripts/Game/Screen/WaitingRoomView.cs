using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 대기실 화면. WaitingRoomController의 RoomUpdated·MessageRaised를 받아 그리고, 버튼은 StartGame·LeaveRoom을 부른다.
    /// 대기실일 때만 root를 켠다(게임 중에는 GameScreen이 보인다).
    /// 시작 버튼은 방장에게만 켠다. 인원(4명 이상) 같은 규칙은 서버가 막고 그 메시지를 보여 준다.
    /// </summary>
    public sealed class WaitingRoomView : MonoBehaviour
    {
        [SerializeField] private WaitingRoomController waitingRoom;
        [SerializeField] private GameObject root;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private TextMeshProUGUI playersText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button leaveButton;

        private void Awake()
        {
            if (waitingRoom == null)
            {
                waitingRoom = FindFirstObjectByType<WaitingRoomController>();
            }
            if (root == null)
            {
                root = gameObject;
            }
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }
            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(OnLeaveClicked);
            }
        }

        private void OnEnable()
        {
            if (waitingRoom != null)
            {
                waitingRoom.RoomUpdated += OnRoomUpdated;
                waitingRoom.MessageRaised += OnMessage;
            }
        }

        private void OnDisable()
        {
            if (waitingRoom != null)
            {
                waitingRoom.RoomUpdated -= OnRoomUpdated;
                waitingRoom.MessageRaised -= OnMessage;
            }
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(OnStartClicked);
            }
            if (leaveButton != null)
            {
                leaveButton.onClick.RemoveListener(OnLeaveClicked);
            }
        }

        private void Update()
        {
            bool waiting = waitingRoom != null && waitingRoom.IsWaiting;
            if (root != gameObject && root.activeSelf != waiting)
            {
                root.SetActive(waiting); // root가 자기 자신이면 끄지 않는다(이벤트를 계속 받아야 한다)
            }
            if (startButton != null)
            {
                startButton.interactable = waiting && RoomSession.IsHost;
            }
        }

        private void OnRoomUpdated(RoomDetailResponse room)
        {
            if (titleText != null)
            {
                titleText.text = GameScreenText.NoRichText(room.title);
            }
            if (countText != null)
            {
                countText.text = room.currentPlayers + " / " + room.maxPlayers + "명";
            }
            if (playersText != null)
            {
                var lines = new List<string>();
                foreach (RoomPlayerResponse p in room.players)
                {
                    lines.Add(GameScreenText.NoRichText(p.nickname) + (p.userId == room.hostUserId ? " (방장)" : string.Empty));
                }
                playersText.text = string.Join("\n", lines.ToArray());
            }
        }

        private void OnMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = GameScreenText.NoRichText(message);
            }
        }

        private void OnStartClicked()
        {
            if (messageText != null)
            {
                messageText.text = string.Empty;
            }
            waitingRoom.StartGame();
        }

        private void OnLeaveClicked()
        {
            waitingRoom.LeaveRoom();
        }
    }
}
