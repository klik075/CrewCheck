namespace WhoisntCitizen.Game
{
    /// <summary>대기실에서 게임으로 들어갈지 정한다. 방 DTO는 Lobby(기본 어셈블리)에 있어서 값만 받는다.</summary>
    public static class GameEntry
    {
        /// <summary>방 상태 IN_GAME. Lobby의 RoomStatus.InGame과 같은 값이다.</summary>
        public const string RoomInGame = "IN_GAME";

        /// <summary>
        /// 방이 게임 중이고 gameId가 있으면 들어간다. 방금 끝낸 게임(finishedGameId)에는 다시 들어가지 않는다.
        /// (게임이 끝나면 서버가 방을 WAITING으로 돌리지만, 그 사이에 받은 응답이 아직 IN_GAME일 수 있다)
        /// </summary>
        public static bool ShouldEnter(string roomStatus, string gameId, string finishedGameId)
        {
            return roomStatus == RoomInGame && !string.IsNullOrEmpty(gameId) && gameId != finishedGameId;
        }
    }
}
