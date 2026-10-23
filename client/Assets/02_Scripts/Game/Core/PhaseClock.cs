using System;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 페이즈 남은 시간. 서버가 준 (phaseEndsAt - serverTime)을 응답을 받은 순간의 로컬 시간에 붙여 마감 시각을 구한다.
    /// 기기 시계(DateTime.Now) 대신 단조 증가 시간(예: Time.realtimeSinceStartupAsDouble)을 받아서,
    /// 기기 시계가 서버와 다르거나 도중에 바뀌어도 남은 시간이 맞는다.
    /// </summary>
    public sealed class PhaseClock
    {
        private long phaseVersion = -1;
        private double deadline;          // 마감 시각 (로컬 단조 시간, 초)
        private DateTime endsAtSeen;      // 지금 마감의 서버 시각 (phaseEndsAt)

        /// <summary>마감이 있는 페이즈인지. ENDED이거나 아직 동기화 전이면 false.</summary>
        public bool HasDeadline { get; private set; }

        /// <summary>마지막으로 반영한 상태의 phaseVersion. 동기화 전이면 -1.</summary>
        public long PhaseVersion
        {
            get { return phaseVersion; }
        }

        /// <summary>
        /// 상태 응답을 받을 때마다 호출한다. localSeconds = 응답을 받은 순간의 단조 시간(초).
        /// 늦게 도착한 옛 페이즈 응답은 무시하고 false를 돌려준다.
        /// 같은 페이즈에서는 마감 추정치 중 가장 이른 값을 쓴다. 응답이 늦게 올수록(지연이 클수록) 마감이 늦게 추정되기 때문이다.
        /// 단, 같은 페이즈에서 서버가 phaseEndsAt을 늦추면(낮 토론 시간 연장) 마감을 새로 잡고,
        /// 그보다 이른 phaseEndsAt이 담긴 응답(연장 전에 보낸 요청의 응답이 늦게 옴)은 마감에 반영하지 않는다.
        /// </summary>
        public bool Sync(GameStateDto state, double localSeconds)
        {
            if (state == null || state.phaseVersion < phaseVersion)
            {
                return false;
            }

            bool newPhase = state.phaseVersion != phaseVersion;
            phaseVersion = state.phaseVersion;

            DateTime? endsAt = GameJson.ParseServerTime(state.phaseEndsAt);
            DateTime? serverNow = GameJson.ParseServerTime(state.serverTime);
            if (!endsAt.HasValue || !serverNow.HasValue)
            {
                HasDeadline = false;
                return true;
            }

            double estimate = localSeconds + Math.Max(0, (endsAt.Value - serverNow.Value).TotalSeconds);
            int moved = newPhase || !HasDeadline ? 1 : endsAt.Value.CompareTo(endsAtSeen); // 양수 = 새 마감, 0 = 같은 마감, 음수 = 옛 응답
            if (moved > 0 || (moved == 0 && estimate < deadline))
            {
                deadline = estimate;
                endsAtSeen = endsAt.Value;
            }
            HasDeadline = true;
            return true;
        }

        /// <summary>남은 초 (0 이상). 마감이 없으면 0.</summary>
        public double RemainingSeconds(double localSeconds)
        {
            return HasDeadline ? Math.Max(0, deadline - localSeconds) : 0;
        }

        /// <summary>화면 표시용 남은 초. 올림해서 0.2초가 남았을 때도 1로 보인다.</summary>
        public int RemainingWholeSeconds(double localSeconds)
        {
            return (int)Math.Ceiling(RemainingSeconds(localSeconds));
        }

        /// <summary>마감이 지났는지. 서버가 판정하고 다음 페이즈로 넘기기 전까지 "판정 중" 같은 표시에 쓴다.</summary>
        public bool IsExpired(double localSeconds)
        {
            return HasDeadline && RemainingSeconds(localSeconds) <= 0;
        }
    }
}
