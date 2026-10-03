using System;
using UnityEngine;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// Game 씬의 게임 진행 담당 MonoBehaviour. 흐름은 GameSession(Core)이 하고, 여기서는 Unity 수명주기와 서버 선택만 맡는다.
    /// - 실제 서버: Room 씬(대기실)에서 받은 RoomSession.GameId로 WaitingRoomController가 BeginGame(gameId)를 부른다.
    /// - 가짜 서버(useFakeServer): 로그인·방 없이 BeginFakeGame()으로 바로 시작한다(Play만 누르면 된다).
    /// - UI(G 단계)는 IGameView를 구현해 gameView에 넣고, 버튼은 SubmitNightAction·SkipNightAction·Vote를 부른다.
    ///   gameView가 비어 있으면 같은 오브젝트·자식에서 IGameView를 찾고, 그래도 없으면 Console 로그(LogGameView)로 대신한다.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        [Header("화면 (IGameView를 구현한 컴포넌트)")]
        [SerializeField] private MonoBehaviour gameView;
        [SerializeField] private float pollIntervalSeconds = 1f;

        [Header("개발용 가짜 서버")]
        [SerializeField] private bool useFakeServer;
        [SerializeField] private string fakeMyRole = RoleCodes.CrewCaptain;
        [SerializeField] private bool fakeBotsAct = true;
        [Tooltip("⋮ 메뉴 \"가짜 서버: 연결 끊김\"으로 내보낼 플레이어 (102 해적 ~ 108 선원)")]
        [SerializeField] private long fakeDisconnectPlayerId = 104;

        private IGameView view;
        private FakeGameApi fakeApi;
        private bool closedRaised;

        /// <summary>진행 중인 게임. 대기실에 있으면 null.</summary>
        public GameSession Session { get; private set; }

        /// <summary>
        /// 가짜 서버 게임인지. 게임이 진행 중이면 실제로 시작한 쪽(BeginGame / BeginFakeGame)을 따르고,
        /// 게임 전에는 인스펙터 설정(useFakeServer)을 돌려준다.
        /// (Room 씬에서 들어온 실제 게임이면 useFakeServer가 켜져 있어도 false → GameScreen이 공개 안내를 중복으로 남기지 않음)
        /// </summary>
        public bool UsesFakeServer
        {
            get { return Session != null ? fakeApi != null : useFakeServer; }
        }

        /// <summary>지금 진행 중인 게임이 가짜 서버 게임인지. Room 씬에서 넘어온 실제 게임이면 useFakeServer가 켜져 있어도 false.</summary>
        public bool IsFakeGame
        {
            get { return fakeApi != null; }
        }

        /// <summary>서버에서 게임이 지워져(404) 닫혔다. WaitingRoomController가 받아서 대기실로 돌아간다.</summary>
        public event Action GameClosed;

        private void Awake()
        {
            view = gameView as IGameView;
            if (gameView != null && view == null)
            {
                Debug.LogError("[Game] gameView에 넣은 컴포넌트가 IGameView를 구현하지 않습니다: " + gameView.GetType().Name);
            }
            if (view == null)
            {
                view = GetComponentInChildren<IGameView>(true);
            }
            if (view == null)
            {
                view = new LogGameView();
            }
        }

        private void Update()
        {
            if (Session == null)
            {
                return;
            }
            Session.Tick();
            if (Session.IsClosed && !closedRaised)
            {
                closedRaised = true;
                if (GameClosed != null)
                {
                    GameClosed();
                }
            }
        }

        private void OnDestroy()
        {
            EndGame();
        }

        // ================================================================ 시작 · 종료

        /// <summary>실제 서버의 게임을 시작한다 (대기실에서 방이 IN_GAME이 되면).</summary>
        public void BeginGame(string gameId)
        {
            EndGame();
            Session = new GameSession(new RealGameApi(), gameId, view, Clock, pollIntervalSeconds);
        }

        /// <summary>가짜 서버로 한 판을 시작한다 (개발용).</summary>
        public void BeginFakeGame()
        {
            EndGame();
            fakeApi = new FakeGameApi(new FakeGameOptions { MyRole = fakeMyRole, BotsAct = fakeBotsAct }, Clock);
            Session = new GameSession(fakeApi, FakeGameApi.FakeGameId, view, Clock, pollIntervalSeconds);
        }

        /// <summary>진행 중인 게임을 닫는다. 늦게 온 응답은 무시된다.</summary>
        public void EndGame()
        {
            if (Session != null)
            {
                Session.Close();
            }
            Session = null;
            fakeApi = null;
            closedRaised = false;
        }

        // ================================================================ 버튼에서 부르는 입력

        public bool SubmitNightAction(long targetId)
        {
            return Session != null && Session.SubmitNightAction(targetId);
        }

        public bool SkipNightAction()
        {
            return Session != null && Session.SkipNightAction();
        }

        public bool Vote(long targetId)
        {
            return Session != null && Session.Vote(targetId);
        }

        /// <summary>개발용: 가짜 서버의 남은 시간을 건너뛴다. 인스펙터 ⋮ 메뉴에서도 부를 수 있다.</summary>
        [ContextMenu("가짜 서버: 다음 페이즈로")]
        public void SkipFakePhase()
        {
            if (fakeApi != null)
            {
                fakeApi.SkipToNextPhase();
            }
        }

        /// <summary>개발용: fakeDisconnectPlayerId 플레이어의 연결이 끊긴 것처럼 처리한다 (서버의 60초 미접속 사망 처리).</summary>
        [ContextMenu("가짜 서버: 연결 끊김")]
        public void DisconnectFakePlayer()
        {
            if (fakeApi != null)
            {
                fakeApi.DisconnectPlayer(fakeDisconnectPlayerId);
            }
        }

        /// <summary>개발용: 서버가 게임을 취소한 것처럼 끝낸다 (취소 결과 화면·로비 이동 확인용).</summary>
        [ContextMenu("가짜 서버: 게임 취소")]
        public void CancelFakeGame()
        {
            if (fakeApi != null)
            {
                fakeApi.CancelGame(EndReasons.CancelledNoDeaths);
            }
        }

        private static double Clock()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
    }
}
