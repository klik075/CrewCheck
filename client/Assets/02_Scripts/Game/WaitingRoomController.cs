using System;
using UnityEngine;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameScene의 게임 진입·복귀 담당. 대기실은 Room 씬(RoomUIController)이 맡는다.
    ///
    /// 흐름
    ///   Lobby → Room 씬(대기실, 방장이 게임 시작) → RoomSession.GameId를 채워 GameScene으로 이동
    ///   GameScene: 이 컴포넌트가 RoomSession.GameId로 GameController.BeginGame(gameId)
    ///   결과 화면 "대기실로" / 서버에서 게임이 지워짐(404) → Room 씬으로 돌아간다
    ///
    /// 진입 규칙 (Start)
    ///   1) 방 + gameId가 있으면 실제 서버 게임 (GameController의 가짜 서버 설정보다 우선)
    ///   2) 방 없이 Play했고 GameController.useFakeServer가 켜져 있으면 가짜 서버로 바로 게임 (개발용)
    ///   3) 방은 있는데 gameId가 없으면 방 상세를 한 번 조회해서 게임 중이면 들어가고, 아니면 Room 씬으로
    ///   4) 방도 없으면 로비로
    ///
    /// 예전 GameScene 안 대기실(WaitingRoomView)은 더 쓰지 않는다. IsWaiting이 항상 false라 패널은 켜지지 않는다.
    /// (WaitingRoomView가 참조하므로 StartGame·LeaveRoom과 이벤트는 남겨 둔다)
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class WaitingRoomController : MonoBehaviour
    {
        private GameController game;
        private bool checkingRoom;
        private bool startInFlight;
        private bool leaveInFlight;
        private bool leaving; // 다른 씬으로 이동 시작함

        /// <summary>방 상세가 갱신됨. (예전 대기실 UI 호환용)</summary>
        public event Action<RoomDetailResponse> RoomUpdated;

        /// <summary>사용자에게 보여줄 안내. (예전 대기실 UI 호환용)</summary>
        public event Action<string> MessageRaised;

        /// <summary>마지막으로 받은 방 상세. 아직 없으면 null.</summary>
        public RoomDetailResponse Room { get; private set; }

        /// <summary>대기실 화면인지. 대기실은 Room 씬이 맡으므로 항상 false (GameScene 안 대기실 패널을 켜지 않는다).</summary>
        public bool IsWaiting
        {
            get { return false; }
        }

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.GameClosed += OnGameClosed;
        }

        private void Start()
        {
            if (RoomSession.HasRoom && RoomSession.HasGame && RoomSession.GameId != RoomSession.FinishedGameId)
            {
                EnterGame(RoomSession.GameId);
                return;
            }
            if (!RoomSession.HasRoom)
            {
                if (game.UsesFakeServer)
                {
                    game.BeginFakeGame(); // 개발용: 로그인·방 없이 바로 게임
                    return;
                }
                Debug.LogWarning("[Game] 들어가 있는 방이 없어서 로비로 돌아갑니다.");
                GoTo(SceneType.Lobby);
                return;
            }

            // 방은 있는데 gameId가 없음 → 방 상태를 한 번 확인
            checkingRoom = true;
            RoomApi.GetRoom(RoomSession.RoomId, OnRoom);
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.GameClosed -= OnGameClosed;
            }
        }

        private void OnRoom(ApiResult<RoomDetailResponse> result)
        {
            if (this == null || !checkingRoom)
            {
                return;
            }
            checkingRoom = false;
            if (!result.success)
            {
                Raise(result.message);
                Debug.LogWarning("[Game] 방 상태를 확인하지 못해 대기실(Room)로 돌아갑니다: " + result.message);
                GoTo(SceneType.Room);
                return;
            }
            Room = result.data;
            RoomSession.Set(result.data);
            if (RoomUpdated != null)
            {
                RoomUpdated(result.data);
            }
            if (GameEntry.ShouldEnter(result.data.status, result.data.gameId, RoomSession.FinishedGameId))
            {
                EnterGame(result.data.gameId);
                return;
            }
            GoTo(SceneType.Room); // 아직 게임 전 → 대기실로
        }

        // ================================================================ 버튼에서 부르는 입력

        /// <summary>
        /// 게임 결과 화면의 "대기실로" (GameScreen). 끝난 게임에 다시 들어가지 않도록 기억해 두고 Room 씬으로 돌아간다.
        /// 가짜 서버로 하던 게임이면 새 판을 시작한다(개발용).
        /// </summary>
        public void ReturnToWaitingRoom()
        {
            bool fake = game.UsesFakeServer; // EndGame 전에 확인 (게임이 닫히면 가짜 여부를 알 수 없다)
            if (game.Session != null)
            {
                RoomSession.MarkGameFinished(game.Session.GameId);
            }
            game.EndGame();
            if (fake && !RoomSession.HasRoom)
            {
                game.BeginFakeGame(); // 개발용: 새 판
                return;
            }
            GoTo(RoomSession.HasRoom ? SceneType.Room : SceneType.Lobby);
        }

        /// <summary>(예전 대기실 UI 호환용) 게임 시작 (방장만). 지금은 Room 씬의 시작 버튼을 쓴다.</summary>
        public void StartGame()
        {
            if (startInFlight || !RoomSession.HasRoom)
            {
                return;
            }
            startInFlight = true;
            RoomApi.StartGame(RoomSession.RoomId, result =>
            {
                if (this == null)
                {
                    return;
                }
                startInFlight = false;
                if (!result.success)
                {
                    Raise(result.message);
                    return;
                }
                RoomSession.SetGameId(result.data.gameId);
                EnterGame(result.data.gameId);
            });
        }

        /// <summary>(예전 대기실 UI 호환용) 방 나가기 → 로비. 게임 중에는 서버가 막는다(409).</summary>
        public void LeaveRoom()
        {
            if (leaveInFlight || !RoomSession.HasRoom)
            {
                return;
            }
            leaveInFlight = true;
            RoomApi.LeaveRoom(RoomSession.RoomId, result =>
            {
                if (this == null)
                {
                    return;
                }
                leaveInFlight = false;
                if (!result.success)
                {
                    Raise(result.message);
                    return;
                }
                RoomSession.Clear();
                GoTo(SceneType.Lobby);
            });
        }

        private void EnterGame(string gameId)
        {
            Debug.Log("[Game] 게임 시작: roomId=" + RoomSession.RoomId + ", gameId=" + gameId);
            game.BeginGame(gameId);
        }

        private void OnGameClosed()
        {
            ReturnToWaitingRoom();
        }

        private void GoTo(SceneType scene)
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            if (!SceneLoader.Load(scene))
            {
                leaving = false;
                Raise(scene + " 씬으로 이동하지 못했습니다. (Build Settings 확인)");
            }
        }

        private void Raise(string message)
        {
            if (MessageRaised != null)
            {
                MessageRaised(message);
            }
        }
    }
}
