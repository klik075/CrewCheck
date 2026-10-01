using System;
using UnityEngine;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// Game 씬의 대기실. 방 상세(GET /api/v1/rooms/{roomId})를 1초마다 조회하다가 IN_GAME + gameId가 되면 GameController로 넘긴다.
    /// 방장이 시작하면 응답의 gameId로 바로 들어가고, 다른 참가자는 polling으로 들어간다.
    /// 게임이 끝나면 서버가 방을 WAITING으로 돌린다. 결과 화면에서 ReturnToWaitingRoom()을 부르면 다시 대기실이 된다.
    /// 대기실 UI(G 단계)는 RoomUpdated·MessageRaised를 구독하고, 버튼은 StartGame·LeaveRoom·ReturnToWaitingRoom을 부른다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class WaitingRoomController : MonoBehaviour
    {
        [SerializeField] private float pollIntervalSeconds = 1f;

        private GameController game;
        private bool polling;
        private bool roomInFlight;
        private bool startInFlight;
        private bool leaveInFlight;
        private float nextPollAt;
        private string finishedGameId;

        /// <summary>방 상세가 갱신됨 (참가자 목록, 방장, 상태). 대기실 UI가 다시 그린다.</summary>
        public event Action<RoomDetailResponse> RoomUpdated;

        /// <summary>사용자에게 보여줄 안내 (게임 시작·나가기 실패, 방 조회 실패 등).</summary>
        public event Action<string> MessageRaised;

        /// <summary>마지막으로 받은 방 상세. 아직 없으면 null.</summary>
        public RoomDetailResponse Room { get; private set; }

        /// <summary>대기실 화면인지 (false면 게임 중).</summary>
        public bool IsWaiting
        {
            get { return polling; }
        }

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.GameClosed += OnGameClosed;
        }

        private void Start()
        {
            if (game.UsesFakeServer)
            {
                game.BeginFakeGame(); // 개발용: 로그인·방 없이 바로 게임
                return;
            }
            if (!RoomSession.HasRoom)
            {
                Debug.LogWarning("[WaitingRoom] 들어가 있는 방이 없어서 로비로 돌아갑니다.");
                SceneLoader.Load(SceneType.Lobby);
                return;
            }
            polling = true;
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.GameClosed -= OnGameClosed;
            }
        }

        private void Update()
        {
            if (!polling || roomInFlight || Time.unscaledTime < nextPollAt)
            {
                return;
            }
            nextPollAt = Time.unscaledTime + pollIntervalSeconds;
            roomInFlight = true;
            RoomApi.GetRoom(RoomSession.RoomId, OnRoom);
        }

        private void OnRoom(ApiResult<RoomDetailResponse> result)
        {
            if (this == null)
            {
                return;
            }
            roomInFlight = false;
            if (!polling)
            {
                return;
            }
            if (!result.success)
            {
                Raise(result.message);
                return;
            }
            Room = result.data;
            RoomSession.Set(result.data); // 방장이 바뀌었을 수 있다
            if (RoomUpdated != null)
            {
                RoomUpdated(result.data);
            }
            if (GameEntry.ShouldEnter(result.data.status, result.data.gameId, finishedGameId))
            {
                EnterGame(result.data.gameId);
            }
        }

        // ================================================================ 버튼에서 부르는 입력

        /// <summary>게임 시작 (방장만, 4명 이상). 실패하면 서버 메시지(409)를 알린다.</summary>
        public void StartGame()
        {
            if (!polling || startInFlight)
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
                if (polling)
                {
                    EnterGame(result.data.gameId);
                }
            });
        }

        /// <summary>방 나가기 → 로비. 게임 중에는 서버가 막는다(409).</summary>
        public void LeaveRoom()
        {
            if (leaveInFlight)
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
                SceneLoader.Load(SceneType.Lobby);
            });
        }

        /// <summary>게임 결과 화면의 "대기실로". 끝난 게임에 다시 들어가지 않도록 기억해 두고 대기실 polling을 다시 시작한다.</summary>
        public void ReturnToWaitingRoom()
        {
            if (game.Session != null)
            {
                finishedGameId = game.Session.GameId;
            }
            game.EndGame();
            if (game.UsesFakeServer)
            {
                game.BeginFakeGame(); // 개발용: 새 판
                return;
            }
            polling = true;
            nextPollAt = 0;
        }

        private void EnterGame(string gameId)
        {
            polling = false;
            game.BeginGame(gameId);
        }

        private void OnGameClosed()
        {
            ReturnToWaitingRoom();
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
