namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 현재 들어가 있는 방 정보를 씬 사이에 전달하는 static 클래스.
    ///
    /// 흐름
    ///   Lobby 씬: 방 생성/입장 성공 → RoomSession.Set(room) → Game 씬으로 이동
    ///   Game 씬 : RoomSession.RoomId 로 방 상세 조회(polling), 게임 시작, 나가기 등을 처리
    ///   방을 나가거나 로그아웃하면 RoomSession.Clear()
    ///
    /// 메모리에만 저장하므로 앱을 종료하면 사라진다.
    /// </summary>
    public static class RoomSession
    {
        /// <summary>현재 방 id. 방에 없으면 0.</summary>
        public static long RoomId { get; private set; }

        /// <summary>방 제목 (Game 씬 상단 표시용)</summary>
        public static string RoomTitle { get; private set; }

        /// <summary>방장 userId</summary>
        public static long HostUserId { get; private set; }

        /// <summary>최대 인원</summary>
        public static int MaxPlayers { get; private set; }

        public static bool HasRoom => RoomId > 0;

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
        /// 방장이 나가면 서버가 방장을 바꿀 수 있으므로 Game 씬에서 polling할 때마다 호출해 주면 좋다.
        /// </summary>
        public static void Set(RoomDetailResponse room)
        {
            if (room == null) return;
            RoomId = room.id;
            RoomTitle = room.title;
            HostUserId = room.hostUserId;
            MaxPlayers = room.maxPlayers;
        }

        /// <summary>방을 나갔거나 로그아웃했을 때 호출</summary>
        public static void Clear()
        {
            RoomId = 0;
            RoomTitle = null;
            HostUserId = 0;
            MaxPlayers = 0;
        }
    }
}
