namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 현재 들어가 있는 방 정보를 씬 사이에 전달하는 static 클래스.
    ///
    /// 흐름
    ///   Lobby 씬: 방 생성/입장 성공 → RoomSession.Set(room) → Room 씬으로 이동
    ///   Room 씬 : RoomSession.RoomId 로 방 상세 조회(polling), 게임 시작, 나가기
    ///             게임이 시작되면 GameId를 채우고 GameScene으로 이동
    ///   GameScene: RoomSession.GameId 로 게임 API(/api/v1/games/{gameId}) 호출
    ///   방을 나가거나 로그아웃하면 RoomSession.Clear()
    ///
    /// 메모리에만 저장하므로 앱을 종료하면 사라진다.
    /// </summary>
    public static class RoomSession
    {
        /// <summary>현재 방 id. 방에 없으면 0.</summary>
        public static long RoomId { get; private set; }

        /// <summary>방 제목 (Room 씬 상단 표시용)</summary>
        public static string RoomTitle { get; private set; }

        /// <summary>방장 userId</summary>
        public static long HostUserId { get; private set; }

        /// <summary>최대 인원</summary>
        public static int MaxPlayers { get; private set; }

        /// <summary>진행 중인 게임 id. 대기 중이면 null. (GameScene에서 사용)</summary>
        public static string GameId { get; private set; }

        /// <summary>방금 끝낸 게임 id. 게임이 끝난 직후 방 상태가 아직 IN_GAME으로 보여도 같은 게임에 다시 들어가지 않게 한다.</summary>
        public static string FinishedGameId { get; private set; }

        // 로비로 돌아간 이유 (게임 취소로 방이 사라짐, 방에서 제외됨 등).
        // Clear()로는 지우지 않는다. (로비에 들어오면 방 세션을 먼저 비우기 때문)
        private static string lobbyNotice;

        public static bool HasRoom => RoomId > 0;

        /// <summary>게임이 시작되어 gameId를 받은 상태인지</summary>
        public static bool HasGame => !string.IsNullOrEmpty(GameId);

        /// <summary>내가 방장인지. (방장만 게임 시작 가능)</summary>
        public static bool IsHost => HasRoom && HostUserId == AuthSession.UserId;

        /// <summary>방 생성/입장 응답으로 세션을 채운다.</summary>
        public static void Set(RoomResponse room)
        {
            if (room == null) return;
            RoomId = room.id;
            RoomTitle = room.title;
            HostUserId = room.hostUserId;
            MaxPlayers = room.maxPlayers;
        }

        /// <summary>
        /// 방 상세 조회 결과로 갱신한다.
        /// 방장이 나가면 서버가 방장을 바꿀 수 있으므로 Room 씬에서 polling할 때마다 호출한다.
        /// </summary>
        public static void Set(RoomDetailResponse room)
        {
            if (room == null) return;
            RoomId = room.id;
            RoomTitle = room.title;
            HostUserId = room.hostUserId;
            MaxPlayers = room.maxPlayers;
            GameId = string.IsNullOrEmpty(room.gameId) ? null : room.gameId;
        }

        /// <summary>게임 시작 API(StartGame) 응답으로 받은 gameId를 저장한다. (방장)</summary>
        public static void SetGameId(string gameId)
        {
            GameId = string.IsNullOrEmpty(gameId) ? null : gameId;
        }

        /// <summary>GameScene에서 게임을 마치고 대기실(Room)로 돌아갈 때 호출. 진행 중 게임 id를 비우고 끝낸 게임으로 기억한다.</summary>
        public static void MarkGameFinished(string gameId)
        {
            if (!string.IsNullOrEmpty(gameId)) FinishedGameId = gameId;
            GameId = null;
        }

        /// <summary>방을 나갔거나 로그아웃했을 때 호출</summary>
        public static void Clear()
        {
            RoomId = 0;
            RoomTitle = null;
            HostUserId = 0;
            MaxPlayers = 0;
            GameId = null;
            FinishedGameId = null;
        }

        /// <summary>로비로 돌아간 이유를 남긴다. 로비가 방 목록을 불러온 뒤 한 번 보여 준다.</summary>
        public static void SetLobbyNotice(string message)
        {
            lobbyNotice = message;
        }

        /// <summary>보여 줄 안내를 꺼낸다. 없으면 null. 꺼내면 지워진다.</summary>
        public static string TakeLobbyNotice()
        {
            string message = lobbyNotice;
            lobbyNotice = null;
            return message;
        }
    }
}
