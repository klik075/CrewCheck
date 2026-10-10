using System;
using System.Collections.Generic;
using UnityEngine;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.TestScenes.LobbyTest
{
    // ======================================================================
    // [테스트 전용 - Assets/99_Test/LobbyTest 폴더째 삭제 예정]
    // LobbyTest 씬에서 서버 대신 쓰는 가짜 데이터. LobbyTestBootstrap의 Inspector에서 고친다.
    // 플레이 중에 고친 값은 [새로고침]이나 검색을 누르면 화면에 반영된다.
    // ======================================================================

    /// <summary>방 상태 (서버 RoomStatus와 같은 이름)</summary>
    public enum FakeRoomStatus
    {
        WAITING,
        IN_GAME,
    }

    /// <summary>방 참가자 한 명</summary>
    [Serializable]
    public class FakeLobbyPlayer
    {
        public long userId;
        public string nickname;
        public bool ready;

        public FakeLobbyPlayer() { }

        public FakeLobbyPlayer(long userId, string nickname, bool ready)
        {
            this.userId = userId;
            this.nickname = nickname;
            this.ready = ready;
        }
    }

    /// <summary>방 하나. 인원(currentPlayers)은 players 수로 계산한다.</summary>
    [Serializable]
    public class FakeLobbyRoom
    {
        public long id;
        public string title;
        [Range(4, 12)] public int maxPlayers = 8;
        public FakeRoomStatus status = FakeRoomStatus.WAITING;
        public bool privateRoom;
        [Tooltip("비밀방 비밀번호 (숫자 4자리 이상)")]
        public string password;
        [Tooltip("방장 userId. 비워 두면(0) 첫 번째 참가자")]
        public long hostUserId;
        [Tooltip("내가 이 방에서 추방된 상태인지 (입장 시 403 KICKED_FROM_ROOM)")]
        public bool iAmKicked;
        public List<FakeLobbyPlayer> players = new List<FakeLobbyPlayer>();

        public long HostId => hostUserId != 0 ? hostUserId : (players.Count > 0 ? players[0].userId : 0);

        public FakeLobbyPlayer Find(long userId)
        {
            foreach (FakeLobbyPlayer p in players)
                if (p != null && p.userId == userId) return p;
            return null;
        }

        /// <summary>서버 RoomResponseDto와 같은 모양 (메인 DTO 사용)</summary>
        public RoomResponse ToResponse()
        {
            return Fill(new RoomResponse());
        }

        /// <summary>서버 RoomDetailResponseDto와 같은 모양 (메인 DTO 사용)</summary>
        public RoomDetailResponse ToDetail(long myUserId)
        {
            var d = Fill(new RoomDetailResponse());
            d.players = new List<RoomPlayerResponse>();
            foreach (FakeLobbyPlayer p in players)
                if (p != null) d.players.Add(new RoomPlayerResponse { userId = p.userId, nickname = p.nickname, ready = p.ready });
            d.kickedUserIds = new List<long>();
            if (iAmKicked) d.kickedUserIds.Add(myUserId);
            return d;
        }

        private T Fill<T>(T r) where T : RoomResponse
        {
            r.id = id;
            r.title = title;
            r.hostUserId = HostId;
            r.maxPlayers = maxPlayers;
            r.currentPlayers = players.Count;
            r.status = status.ToString();
            r.gameId = status == FakeRoomStatus.IN_GAME ? "fake-game-" + id : "";
            r.privateRoom = privateRoom;
            return r;
        }
    }

    /// <summary>로비 전체 가짜 데이터</summary>
    [Serializable]
    public class FakeLobbyData
    {
        [Header("나 (가짜 로그인)")]
        public long myUserId = 1;
        [Tooltip("로비 상단 사용자 이름 칸에 표시되는 값 (AuthSession.Username)")]
        public string myUsername = "lobbytester";
        [Tooltip("방에 들어갔을 때 표시되는 닉네임")]
        public string myNickname = "유진";

        [Header("방 목록")]
        public List<FakeLobbyRoom> rooms = FakeLobbyDefaults.CreateRooms();

        public FakeLobbyRoom Find(long roomId)
        {
            foreach (FakeLobbyRoom r in rooms)
                if (r != null && r.id == roomId) return r;
            return null;
        }

        public long NextRoomId()
        {
            long id = 0;
            foreach (FakeLobbyRoom r in rooms) if (r != null) id = Math.Max(id, r.id);
            return id + 1;
        }
    }

    public static class FakeLobbyDefaults
    {
        private static readonly string[] Names =
            { "철수", "영희", "민수", "지훈", "수진", "하늘", "도윤", "서연", "예준", "하린", "시우", "지아" };

        public static List<FakeLobbyRoom> CreateRooms()
        {
            return new List<FakeLobbyRoom>
            {
                Room(1, "초보만 오세요", 8, 3),
                Room(2, "친구끼리 (비번 1234)", 6, 4, privateRoom: true, password: "1234"),
                Room(3, "꽉 찬 방", 4, 4),
                Room(4, "이미 게임 중인 방", 8, 6, status: FakeRoomStatus.IN_GAME),
                Room(5, "추방당한 방 (입장 불가)", 8, 3, kicked: true),
                Room(6, "해적선 탈출 대작전 - 제목이 아주아주 길면 어떻게 보이는지 확인하는 방", 12, 2),
                Room(7, "Pirate Party", 10, 5),
                Room(8, "빈자리 하나 남은 비밀방", 6, 5, privateRoom: true, password: "0427"),
            };
        }

        /// <summary>[가짜 방 추가] 메뉴에서 쓰는 방</summary>
        public static FakeLobbyRoom RandomRoom(long id)
        {
            int max = UnityEngine.Random.Range(4, 13);
            return Room(id, "새로 생긴 방 #" + id, max, UnityEngine.Random.Range(1, max + 1));
        }

        private static FakeLobbyRoom Room(long id, string title, int max, int count,
            FakeRoomStatus status = FakeRoomStatus.WAITING, bool privateRoom = false, string password = null, bool kicked = false)
        {
            var room = new FakeLobbyRoom
            {
                id = id,
                title = title,
                maxPlayers = max,
                status = status,
                privateRoom = privateRoom,
                password = password,
                iAmKicked = kicked,
            };
            for (int i = 0; i < count; i++)
            {
                long userId = id * 100 + i + 1; // 방마다 겹치지 않는 가짜 userId (내 userId 1과도 겹치지 않음)
                room.players.Add(new FakeLobbyPlayer(userId, Names[(int)((id + i) % Names.Length)], i % 2 == 1));
            }
            return room;
        }
    }

    // ======================================================================
    // 테스트 씬끼리 방 정보를 주고받는 형식 (JSON 문자열로만 주고받는다)
    // RoomTest 폴더에도 같은 모양의 클래스가 따로 있다. 서로의 타입을 참조하지 않으므로
    // 둘 중 한 폴더만 지워도 남은 쪽은 문제없이 컴파일·동작한다.
    // ======================================================================
    [Serializable]
    public class TestRoomHandoff
    {
        public const string EnterRoomKey = "WhoisntCitizen.TestHandoff.EnterRoom"; // LobbyTest → RoomTest
        public const string LeaveRoomKey = "WhoisntCitizen.TestHandoff.LeaveRoom"; // RoomTest → LobbyTest

        public long myUserId;
        public string myNickname;
        public RoomDetailResponse room;

        /// <summary>플레이 시작마다 비운다. (도메인 리로드를 끈 설정에서도 이전 플레이 값이 남지 않게)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AppDomain.CurrentDomain.SetData(EnterRoomKey, null);
            AppDomain.CurrentDomain.SetData(LeaveRoomKey, null);
        }

        public static void Put(string key, TestRoomHandoff value)
        {
            AppDomain.CurrentDomain.SetData(key, value == null ? null : JsonUtility.ToJson(value));
        }

        /// <summary>꺼내면서 지운다. 없으면 null</summary>
        public static TestRoomHandoff Take(string key)
        {
            string json = AppDomain.CurrentDomain.GetData(key) as string;
            AppDomain.CurrentDomain.SetData(key, null);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<TestRoomHandoff>(json); }
            catch (Exception) { return null; }
        }
    }
}
