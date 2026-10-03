using UnityEngine;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameScene에 들어오고 나가는 흐름. 대기실 화면은 Room 씬(RoomUIController)이 맡는다.
    /// - Room 씬에서 넘어오면(RoomSession.GameId가 있으면) 항상 실제 서버 게임을 시작한다. useFakeServer가 켜져 있어도 같다.
    /// - 방 없이 GameScene을 바로 Play하면 useFakeServer일 때 가짜 서버로 한 판을 시작하고, 아니면 Room(또는 로비)으로 보낸다.
    /// - 결과 화면의 "대기실로"(GameScreen)나 서버에서 게임이 사라졌을 때(404, GameClosed) ReturnToWaitingRoom()으로 Room 씬에 돌아간다.
    ///   게임이 끝나면 서버가 방을 WAITING으로 돌려 두므로 Room 씬이 같은 게임으로 다시 보내지 않는다.
    ///   취소된 게임은 서버가 결과 조회 시간 뒤 방을 지우므로 Room이 아니라 로비로 가고, 로비에서 이유를 알린다.
    ///   가짜 서버 게임이면 씬을 옮기지 않고 새 판을 시작한다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class WaitingRoomController : MonoBehaviour
    {
        private GameController game;
        private bool leaving;

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.GameClosed += ReturnToWaitingRoom;
        }

        private void Start()
        {
            if (RoomSession.HasGame)
            {
                game.BeginGame(RoomSession.GameId);
                return;
            }
            if (game.UsesFakeServer)
            {
                game.BeginFakeGame(); // 개발용: 로그인·방 없이 바로 게임
                return;
            }
            Debug.LogWarning("[GameScene] 진행 중인 게임이 없어서 대기실로 돌아갑니다.");
            Leave();
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.GameClosed -= ReturnToWaitingRoom;
            }
        }

        /// <summary>결과 화면의 "대기실로". 실제 서버 게임이면 Room 씬으로, 가짜 서버 게임이면 새 판.</summary>
        public void ReturnToWaitingRoom()
        {
            bool fake = game.IsFakeGame;
            string finishedGameId = game.Session != null ? game.Session.GameId : null;
            bool cancelled = game.Session != null && game.Session.IsCancelled;
            game.EndGame();
            if (fake)
            {
                game.BeginFakeGame(); // 개발용: 새 판
                return;
            }
            if (cancelled)
            {
                // 방은 서버가 곧 지운다. 대기실에 들렀다가 튕기지 않도록 바로 로비로 간다.
                RoomSession.Clear();
                RoomSession.SetLobbyNotice(GameScreenText.CancelledLobbyNotice);
            }
            Leave(finishedGameId);
        }

        private void Leave(string finishedGameId = null)
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            // 끝난 게임. Room 씬이 방을 다시 조회해 채운다.
            // 끝낸 게임 id를 기억해 두어, 서버가 방을 WAITING으로 돌리기 전에 Room 씬이 조회해도 같은 게임으로 다시 보내지 않는다.
            RoomSession.MarkGameFinished(finishedGameId);
            if (!SceneLoader.Load(RoomSession.HasRoom ? SceneType.Room : SceneType.Lobby))
            {
                leaving = false; // 다른 씬을 로딩 중이거나 Build Settings 누락: 다시 누를 수 있게 둔다
            }
        }
    }
}