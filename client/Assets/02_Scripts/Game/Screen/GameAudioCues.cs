using UnityEngine;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 진행에 맞춰 BGM·효과음을 튼다. GameScreen(IGameView)이 각 시점에 부른다.
    /// 이름은 06_Data/Audio/_Container 의 AudioClipContainer 에셋 이름 (AudioSystemManager 의 목록에 들어 있어야 한다).
    /// </summary>
    public static class GameAudioCues
    {
        public const string GameStart = "GameStart";       // SFX: 게임 시작
        public const string Night = "Night";               // BGM: 밤 (밤 결과까지)
        public const string DeadFound = "DeadFound";       // SFX: 밤에 누군가 죽음
        public const string Discussion = "Discussion";     // BGM: 낮 토론
        public const string Vote = "Vote";                 // BGM: 투표 (처형 결과가 나올 때까지)
        public const string WalkThePlank = "WalkThePlank"; // BGM: 투표로 처형됨
        public const string Victory = "Victory";           // BGM: 승리 (반복 X)
        public const string Defeat = "Defeat";             // BGM: 패배 (반복 X)

        // 직업 대사: 06_Data/Audio/_Container/UniqueDialogue 의 컨테이너. 이름 = 직업 코드 (예: CREW_DOCTOR)
        private const float VoiceCutSeconds = 0.15f; // 새 대사가 나오면 이전 대사를 짧게 줄이고 끊는다
        private static AudioPlayer voice;            // 지금 나오는 직업 대사 (풀링 플레이어라 끝나면 다른 소리에 재사용될 수 있다)
        private static UnityEngine.Audio.AudioResource voiceClip;

        /// <summary>페이즈가 바뀜. PreviousPhase == null 이면 처음 받은 상태(새 게임 또는 재접속).</summary>
        public static void OnPhase(GameEvent phaseChanged)
        {
            if (phaseChanged == null) return;

            // 새 게임의 첫 밤에만 시작 효과음 (재접속으로 처음 받은 상태는 제외)
            if (phaseChanged.PreviousPhase == null && phaseChanged.Phase == GamePhases.Night && phaseChanged.Day <= 1)
            {
                AudioSystemManager.Play(GameStart);
            }

            switch (phaseChanged.Phase)
            {
                case GamePhases.Night:
                case GamePhases.NightResult:
                    AudioSystemManager.PlayBGM(Night);
                    break;
                case GamePhases.Day:
                    AudioSystemManager.PlayBGM(Discussion);
                    break;
                case GamePhases.Vote:
                    AudioSystemManager.PlayBGM(Vote);
                    break;
                // EXECUTION: 처형 결과가 오면 바꾼다 (OnExecutionResult). ENDED: 결과 화면이 뜨면 바꾼다 (OnGameResult)
            }
        }

        /// <summary>밤 결과 (live = 지금 막 나온 결과. 재접속으로 지난 결과를 받은 경우는 false).</summary>
        public static void OnNightResult(NightResultDto result, bool live)
        {
            if (live && result != null && result.HasKill)
            {
                AudioSystemManager.Play(DeadFound);
            }
        }

        /// <summary>처형 결과. 누가 처형됐으면 WalkThePlank.</summary>
        public static void OnExecutionResult(ExecutionResultDto result, bool live)
        {
            if (live && result != null && result.HasExecution)
            {
                AudioSystemManager.PlayBGM(WalkThePlank);
            }
        }

        /// <summary>결과 화면이 뜰 때. 내가 이겼으면 Victory, 졌으면 Defeat (한 번만). 취소된 게임이면 BGM만 끈다.</summary>
        public static void OnGameResult(GameResultDto result, MyRoleDto me)
        {
            string background = GameResultPanel.BackgroundName(result, me); // null = 취소·알 수 없음
            if (background == null)
            {
                AudioSystemManager.StopBGM(0.5f);
                return;
            }
            AudioSystemManager.PlayBGM(background == "Defeat" ? Defeat : Victory, loop: false);

            // 원숭이는 게임 중에 위장 직업만 받으므로, 결과에서 실제 직업이 원숭이로 밝혀질 때 대사를 튼다
            if (me != null && result != null && result.players != null)
            {
                foreach (PlayerResultDto p in result.players)
                {
                    if (p.playerId == me.playerId && p.role == RoleCodes.CrewMonkey)
                    {
                        PlayRoleVoice(RoleCodes.CrewMonkey);
                        break;
                    }
                }
            }
        }

        /// <summary>새 게임에서 처음 직업을 받았을 때 (재접속 제외). 원숭이는 위장 직업의 대사가 나온다.</summary>
        public static void OnRoleAssigned(MyRoleDto me)
        {
            if (me != null) PlayRoleVoice(me.role);
        }

        /// <summary>내 밤 능력이 접수됨 (그 밤 처음 고를 때만). 원숭이는 위장 직업의 대사가 나온다.</summary>
        public static void OnAbilityUsed(MyRoleDto me)
        {
            if (me != null) PlayRoleVoice(me.role);
        }

        /// <summary>직업 대사를 튼다. 이전 대사가 아직 나오고 있으면 끊는다.</summary>
        private static void PlayRoleVoice(string roleCode)
        {
            if (string.IsNullOrEmpty(roleCode) || !AudioSystemManager.HasInstance) return;
            StopVoice();
            voice = AudioSystemManager.Play(roleCode);
            voiceClip = voice != null ? voice.Source.resource : null;
        }

        /// <summary>직업 대사를 끊는다. 플레이어가 이미 풀로 돌아가 다른 소리를 내고 있으면 건드리지 않는다.</summary>
        public static void StopVoice()
        {
            AudioPlayer player = voice;
            UnityEngine.Audio.AudioResource clip = voiceClip;
            voice = null;
            voiceClip = null;
            if (player == null || clip == null || !player.isActiveAndEnabled) return;
            if (player.Source.resource == clip && player.Source.isPlaying)
            {
                player.FadeOut(VoiceCutSeconds); // FadeOut 은 끝나면 풀에 반환한다 (Stop 은 반환하지 않는다)
            }
        }

        /// <summary>
        /// 게임 화면을 떠날 때 (씬 전환). 매니저는 DontDestroy 라서 직접 꺼야 한다.
        /// 다음 씬의 SceneBGM 이 먼저 다른 곡을 틀었을 수 있으므로, 지금 흐르는 곡이 게임 BGM 일 때만 끈다.
        /// </summary>
        public static void Stop()
        {
            if (!AudioSystemManager.HasInstance) return;
            StopVoice();
            var current = AudioSystemManager.CurrentBGM;
            if (current != null && IsGameBgm(current.name)) AudioSystemManager.StopBGM();
        }

        private static bool IsGameBgm(string name)
            => name == Night || name == Discussion || name == Vote
            || name == WalkThePlank || name == Victory || name == Defeat;
    }
}
