namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameSession이 화면에 알리는 단일 진입점. G 단계의 UI가 구현한다(없으면 LogGameView가 Console에 남긴다).
    /// 남은 시간, 고를 수 있는 대상, 버튼을 켤지는 GameSession의 프로퍼티(RemainingWholeSeconds, NightAbility,
    /// NightTargets, CanVote, VoteTargets 등)를 매 프레임 읽어서 그린다.
    /// </summary>
    public interface IGameView
    {
        /// <summary>폴링할 때마다 (생존자·페이즈·일차 갱신). 1초마다 불린다.</summary>
        void ShowState(GameStateDto state);

        /// <summary>페이즈가 바뀜 → 패널 전환. 처음 받은 상태도 여기로 온다(PreviousPhase == null).</summary>
        void ShowPhase(GameEvent phaseChanged);

        /// <summary>직전 상태에서 살아 있던 사람이 사망함 (밤 공격 또는 처형).</summary>
        void ShowPlayerDied(GameEvent died);

        /// <summary>내 역할 (/me). 페이즈가 바뀔 때마다 다시 온다(남은 횟수·동료 갱신).</summary>
        void ShowMyRole(MyRoleDto me);

        /// <summary>밤 결과. 그 밤에 한 번 온다. 폴링 사이에 NIGHT_RESULT를 놓쳤어도 온다.</summary>
        void ShowNightResult(NightResultDto result);

        /// <summary>처형 결과. 그날 한 번 온다. EXECUTION을 놓쳤어도 온다.</summary>
        void ShowExecutionResult(ExecutionResultDto result);

        /// <summary>게임 결과 (전원의 실제 직업). 끝나면 한 번 온다.</summary>
        void ShowGameResult(GameResultDto result);

        /// <summary>내 밤 행동·넘기기가 접수됨. 앵무새가 접선했으면 contactedPirateIds가 채워져 있다.</summary>
        void ShowActionAccepted(NightActionResultDto result);

        /// <summary>내 투표가 접수됨.</summary>
        void ShowVoteAccepted(VoteResultDto result);

        /// <summary>사용자에게 보여줄 오류 (서버 409 메시지, 버튼을 누를 수 없는 이유 등).</summary>
        void ShowError(string message);

        /// <summary>서버 연결이 끊기거나(false) 다시 이어짐(true). 바뀔 때만 온다.</summary>
        void ShowConnection(bool connected);

        /// <summary>서버에서 게임이 지워짐(404). 더 이상 요청하지 않는다. 대기실로 돌아간다.</summary>
        void OnGameClosed();
    }
}
