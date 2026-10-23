using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    // 방의 직업 배정 설정 (서버 RoleSetup, RoleComposition, RoleSetupOptionsResponse).
    // 필드 이름은 서버 JSON 키와 대소문자까지 같아야 JsonUtility가 값을 채운다.
    // JsonUtility가 Map을 읽지 못해서 서버는 인원수별 표를 {playerCount, roles} 객체 배열로 보낸다.

    /// <summary>직업 배정 방식 (서버 RoleSetupMode). roleSetup.mode 값.</summary>
    public static class RoleSetupModes
    {
        public const string Recommended = "RECOMMENDED"; // 인원수별 추천 구성
        public const string Custom = "CUSTOM";           // 방장이 편집한 인원수별 구성 (편집하지 않은 인원수는 추천)
        public const string Random = "RANDOM";           // 진영 수는 추천과 같고, 진영 안의 직업은 후보 중 무작위

        /// <summary>화면 표시 이름. 서버 채팅 안내(RoleSetupMode.label)와 같은 말을 쓴다.</summary>
        public static string Label(string mode)
        {
            switch (mode)
            {
                case Custom: return "커스텀 구성";
                case Random: return "랜덤 구성";
                default: return "추천 구성";
            }
        }

        /// <summary>버튼처럼 좁은 곳에 쓰는 짧은 이름</summary>
        public static string ShortLabel(string mode)
        {
            switch (mode)
            {
                case Custom: return "커스텀";
                case Random: return "랜덤";
                default: return "추천";
            }
        }

        /// <summary>값이 없거나 모르는 값이면 추천으로 본다. (설정 필드가 없던 예전 서버 응답 대비)</summary>
        public static string Normalize(string mode)
        {
            return mode == Custom || mode == Random ? mode : Recommended;
        }
    }

    /// <summary>
    /// 방의 직업 배정 설정. GET /api/v1/rooms/{roomId}의 roleSetup, PUT /api/v1/rooms/{roomId}/role-setup의 body.
    /// 모드를 바꿔도 나머지 두 값은 남아 있어서, PUT할 때는 항상 세 값을 모두 보낸다.
    /// </summary>
    [Serializable]
    public class RoleSetupDto
    {
        public string mode; // RoleSetupModes
        public List<RoleCompositionDto> customCompositions = new List<RoleCompositionDto>(); // 편집한 인원수만 들어 있다
        public List<string> randomCandidates = new List<string>(); // 랜덤 후보 특수 직업. 해적·선원은 항상 후보라 없다

        public RoleSetupDto Clone()
        {
            var copy = new RoleSetupDto { mode = mode };
            if (customCompositions != null)
                foreach (RoleCompositionDto c in customCompositions)
                    if (c != null) copy.customCompositions.Add(c.Clone());
            if (randomCandidates != null) copy.randomCandidates.AddRange(randomCandidates);
            return copy;
        }
    }

    /// <summary>한 인원수의 직업 구성. roles는 직업 코드(RoleCodes) 목록이고 같은 직업이 여러 번 들어갈 수 있다.</summary>
    [Serializable]
    public class RoleCompositionDto
    {
        public int playerCount;
        public List<string> roles = new List<string>();

        public RoleCompositionDto Clone()
        {
            return new RoleCompositionDto { playerCount = playerCount, roles = new List<string>(roles ?? new List<string>()) };
        }
    }

    /// <summary>직업 설정 화면의 선택지. GET /api/v1/role-setup/options. 서버가 켜져 있는 동안 바뀌지 않는다.</summary>
    [Serializable]
    public class RoleSetupOptionsDto
    {
        public int minPlayers;
        public int maxPlayers;
        public List<RoleDto> roles = new List<RoleDto>();                               // 화면 표시 순서 (해적 진영 → 선원 진영, 선원은 맨 뒤)
        public List<string> randomCandidates = new List<string>();                      // 랜덤 후보로 고를 수 있는 특수 직업
        public List<RoleCompositionDto> recommended = new List<RoleCompositionDto>();   // 인원수별 추천 구성
    }
}
