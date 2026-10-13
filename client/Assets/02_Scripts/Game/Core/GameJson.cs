using System;
using System.Globalization;
using UnityEngine;

namespace WhoisntCitizen.Game
{
    /// <summary>게임 API JSON 도우미. JsonUtility 사용 규칙을 한곳에 모은다.</summary>
    public static class GameJson
    {
        /// <summary>응답 본문 → DTO. 본문이 비어 있으면(401 등) null.</summary>
        public static T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return JsonUtility.FromJson<T>(json);
        }

        /// <summary>
        /// 밤 능력 요청 body. {"targetId":5,"actionCode":""} (actionCode가 비면 서버는 직업의 기본 능력으로 본다)
        /// ApiClient.Post는 body를 object로 받아 JsonUtility.ToJson으로 다시 직렬화하므로, 이 문자열 대신 TargetRequestDto 객체를 넘긴다.
        /// 문자열을 넘기면 {}가 전송된다.
        /// </summary>
        public static string TargetBody(long targetId, string actionCode = null)
        {
            return JsonUtility.ToJson(new TargetRequestDto { targetId = targetId, actionCode = actionCode });
        }

        /// <summary>
        /// 서버 시각(ISO-8601, UTC, 예: 2026-10-01T12:00:30.123Z) → UTC DateTime.
        /// null이나 빈 값(예: ENDED의 phaseEndsAt)이면 null.
        /// </summary>
        public static DateTime? ParseServerTime(string iso)
        {
            if (string.IsNullOrEmpty(iso))
            {
                return null;
            }
            return DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }
    }
}
