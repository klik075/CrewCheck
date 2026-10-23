using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhoisntCitizen.RoomTest
{
    // ======================================================================
    // [테스트 전용 - Assets/99_Test/RoomTest 폴더째 삭제 예정]
    // RoomTest 씬에서 서버 대신 쓰는 가짜 데이터. RoomTestBootstrap의 Inspector에서 고친다.
    // 플레이 중에 고쳐도 다음 polling(1초)부터 화면에 반영된다.
    // ======================================================================

    /// <summary>가짜 참가자 한 명</summary>
    [Serializable]
    public class FakePlayer
    {
        public long userId;
        public string nickname;
        [Tooltip("준비 여부 (방장은 의미 없음)")]
        public bool ready;

        public FakePlayer() { }

        public FakePlayer(long userId, string nickname, bool ready)
        {
            this.userId = userId;
            this.nickname = nickname;
            this.ready = ready;
        }
    }

    /// <summary>채팅 메시지 종류 (서버 MessageType과 같은 이름)</summary>
    public enum FakeChatType
    {
        USER,   // 일반 채팅
        SYSTEM, // 입장·퇴장 알림, 공지
        DEAD,   // 사망자 채팅 (대기실에서는 보통 안 쓰지만 표시 확인용)
    }

    /// <summary>가짜 채팅 한 줄</summary>
    [Serializable]
    public class FakeChatLine
    {
        public FakeChatType type = FakeChatType.USER;
        [Tooltip("보낸 사람 userId (SYSTEM이면 무시되고 0으로 보냄)")]
        public long userId;
        [Tooltip("비워 두면 참가자 목록의 닉네임을 쓴다")]
        public string nickname;
        [TextArea(1, 3)] public string message;

        public FakeChatLine() { }

        public FakeChatLine(FakeChatType type, long userId, string nickname, string message)
        {
            this.type = type;
            this.userId = userId;
            this.nickname = nickname;
            this.message = message;
        }
    }

    /// <summary>가짜 방 하나 (서버 RoomDetailResponseDto에 해당)</summary>
    [Serializable]
    public class FakeRoomData
    {
        [Header("방")]
        public long roomId = 9001;
        public string title = "[테스트] 가짜 대기실";
        [Range(4, 12)] public int maxPlayers = 8;
        public bool privateRoom;

        [Header("나 / 방장")]
        [Tooltip("내 userId. 아래 players에 같은 userId가 있어야 한다 (없으면 방에서 제외된 것으로 보고 로비로 이동)")]
        public long myUserId = 1;
        [Tooltip("방장 userId. myUserId와 같으면 내가 방장 (시작 버튼, 관리 버튼이 보임)")]
        public long hostUserId = 1;

        [Header("참가자 (입장 순서)")]
        public List<FakePlayer> players = new List<FakePlayer>
        {
            new FakePlayer(1, "유진", false),
            new FakePlayer(2, "철수", true),
            new FakePlayer(3, "영희", true),
            new FakePlayer(4, "민수", false),
            new FakePlayer(5, "지훈", true),
        };

        [Tooltip("추방된 userId (서버 응답의 kickedUserIds)")]
        public List<long> kickedUserIds = new List<long>();

        public FakePlayer Find(long userId)
        {
            foreach (FakePlayer p in players)
                if (p != null && p.userId == userId) return p;
            return null;
        }

        public string NicknameOf(long userId)
        {
            FakePlayer p = Find(userId);
            return p != null && !string.IsNullOrEmpty(p.nickname) ? p.nickname : "플레이어 " + userId;
        }
    }

    /// <summary>처음에 채팅창에 깔아 둘 메시지 기본값</summary>
    public static class FakeChatDefaults
    {
        public static List<FakeChatLine> Create()
        {
            return new List<FakeChatLine>
            {
                new FakeChatLine(FakeChatType.SYSTEM, 0, null, "유진님이 입장했습니다."),
                new FakeChatLine(FakeChatType.SYSTEM, 0, null, "철수님이 입장했습니다."),
                new FakeChatLine(FakeChatType.USER, 2, null, "안녕하세요!"),
                new FakeChatLine(FakeChatType.SYSTEM, 0, null, "영희님이 입장했습니다."),
                new FakeChatLine(FakeChatType.USER, 3, null, "다들 준비 눌러 주세요~"),
                new FakeChatLine(FakeChatType.SYSTEM, 0, null, "민수님이 입장했습니다."),
                new FakeChatLine(FakeChatType.SYSTEM, 0, null, "지훈님이 입장했습니다."),
                new FakeChatLine(FakeChatType.USER, 5, null, "이번 판은 해적 안 걸렸으면 ㅋㅋ"),
            };
        }

        /// <summary>[가짜 참가자 채팅] 메뉴에서 무작위로 고르는 문장</summary>
        public static readonly string[] RandomLines =
        {
            "준비 완료!",
            "잠깐만요, 물 좀 떠 올게요",
            "선장 직업 나왔으면 좋겠다",
            "이번엔 크라켄 하고 싶어요",
            "방장님 시작해 주세요~",
            "<b>태그</b> 들어간 채팅도 깨지지 않는지 확인용",
            "아주아주아주 긴 채팅이 줄바꿈되는지 확인하기 위한 문장입니다. 한 줄에 다 들어가지 않으면 어떻게 보이는지 봅니다.",
        };
    }
}
