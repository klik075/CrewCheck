using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameTest 씬 전용: 서버 없이 [게임 시작] 버튼으로 가짜 서버(FakeGameApi) 게임을 시작하고,
    /// 밤 → 밤 결과 → 낮 → 투표 → 처형 → 다음 밤까지 한 바퀴를 자동으로 진행한다.
    /// - 데이터는 모두 FakeGameApi가 만든다(나 + 봇 7명, 직업은 GameController의 fakeMyRole).
    /// - 각 페이즈에서 "나"의 입력을 대신 한다: 밤 능력(무작위 대상) → 낮 토론 넘기기 → 무작위 투표.
    ///   입력은 화면 버튼과 같은 경로(GameController)로 보내므로 패널·카드패·채팅 안내가 실제처럼 바뀐다.
    /// - 페이즈가 스스로 넘어가지 않으면(봇이 아직 내지 않음) 가짜 서버의 남은 시간을 건너뛴다(SkipFakePhase).
    /// - autoPlayOneCycle을 끄면 게임만 시작하고 직접 조작해 볼 수 있다.
    /// </summary>
    public sealed class GameTestRunner : MonoBehaviour
    {
        [Header("진행 (비우면 씬에서 찾는다)")]
        [SerializeField] private GameController controller;

        [Header("UI")]
        [SerializeField] private Button startButton;
        [SerializeField] private TextMeshProUGUI startButtonText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("자동 진행")]
        [Tooltip("켜 두면 시작 후 한 바퀴(다음 밤까지)를 자동으로 진행한다. 끄면 게임만 시작한다.")]
        [SerializeField] private bool autoPlayOneCycle = true;
        [Tooltip("각 페이즈를 화면에서 보여 주는 시간(초)")]
        [SerializeField] private float stepSeconds = 2f;
        [Tooltip("폴링(1초)을 기다리는 최대 시간(초)")]
        [SerializeField] private float waitTimeoutSeconds = 5f;

        private Coroutine running;
        private readonly System.Random random = new System.Random();

        private GameSession Session
        {
            get { return controller != null ? controller.Session : null; }
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<GameController>();
            }
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }
            SetStatus("[게임 시작]을 누르면 가짜 데이터로 한 판을 시작합니다.");
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(OnStartClicked);
            }
        }

        private void OnStartClicked()
        {
            if (controller == null)
            {
                SetStatus("GameController가 없습니다.");
                return;
            }
            if (running != null)
            {
                StopCoroutine(running);
            }
            running = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            SetButtonVisible(false);
            controller.EndGame();
            controller.BeginFakeGame();
            SetStatus("가짜 서버 게임 시작 (서버 연결 없음)");

            float waited = 0f;
            while (Session == null || Session.State == null || Session.Me == null)
            {
                if (waited > waitTimeoutSeconds)
                {
                    Finish("게임 상태를 받지 못했습니다.");
                    yield break;
                }
                waited += Time.deltaTime;
                yield return null;
            }
            Log("내 직업: " + Session.Me.roleName + " / 참가자 " + Session.State.players.Count + "명");

            if (!autoPlayOneCycle)
            {
                Finish("게임을 시작했습니다. 직접 진행해 보세요.");
                yield break;
            }

            int startDay = Session.State.day;
            int guard = 0;
            while (guard++ < 20)
            {
                GameSession session = Session;
                if (session == null || session.State == null)
                {
                    Finish("게임이 닫혔습니다.");
                    yield break;
                }
                GameStateDto state = session.State;
                if (state.phase == GamePhases.Ended)
                {
                    Finish("게임 종료 (" + (state.winner ?? state.endReason) + ") - 한 바퀴 테스트 끝");
                    yield break;
                }
                if (state.phase == GamePhases.Night && state.day > startDay)
                {
                    Finish(state.day + "일차 밤까지 한 바퀴를 돌았습니다.");
                    yield break;
                }

                string phaseName = GameScreenText.PhaseTitle(state.day, state.phase);
                SetStatus(phaseName + " 진행 중");
                yield return new WaitForSeconds(stepSeconds); // 화면을 볼 시간

                long version = Session.State.phaseVersion;
                string action = ActFor(Session);
                Log(phaseName + ": " + action);

                // 내 입력으로 바로 넘어갔는지 폴링 결과를 기다린다. 아니면 가짜 서버의 남은 시간을 건너뛴다.
                yield return WaitForVersionChange(version, 1.5f);
                if (Session != null && Session.State != null && Session.State.phaseVersion == version)
                {
                    controller.SkipFakePhase();
                    yield return WaitForVersionChange(version, waitTimeoutSeconds);
                }
            }
            Finish("진행이 끝나지 않아 멈췄습니다. Console을 확인하세요.");
        }

        /// <summary>지금 페이즈에서 "나"의 입력을 대신 한다. 무엇을 했는지 돌려준다.</summary>
        private string ActFor(GameSession session)
        {
            switch (session.State.phase)
            {
                case GamePhases.Night:
                    if (session.NightAbility != AbilityBlock.None)
                    {
                        return "능력 없음 (" + ReportFormatter.AbilityBlockMessage(session.NightAbility) + ")";
                    }
                    PlayerViewDto nightTarget = Pick(session.NightTargets, session.Me.playerId);
                    if (nightTarget == null)
                    {
                        controller.SkipNightAction();
                        return "고를 대상이 없어 능력 넘기기";
                    }
                    controller.SubmitNightAction(nightTarget.playerId);
                    return ReportFormatter.ActionName(session.Me.actionCode) + " → " + nightTarget.nickname;
                case GamePhases.Day:
                    if (!session.CanSkipDay)
                    {
                        return "토론 넘기기 불가 (사망)";
                    }
                    controller.SkipDay();
                    return "토론 넘기기";
                case GamePhases.Vote:
                    if (!session.CanVote)
                    {
                        return "투표 불가 (사망)";
                    }
                    PlayerViewDto voteTarget = Pick(session.VoteTargets, session.Me.playerId);
                    if (voteTarget == null)
                    {
                        return "투표할 대상 없음";
                    }
                    controller.Vote(voteTarget.playerId);
                    return "투표 → " + voteTarget.nickname;
                default:
                    return "결과 확인";
            }
        }

        /// <summary>자기 자신을 뺀 대상 중 무작위. 자기 자신만 있으면 자기 자신.</summary>
        private PlayerViewDto Pick(List<PlayerViewDto> candidates, long myId)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            var others = candidates.FindAll(p => p.playerId != myId);
            List<PlayerViewDto> pool = others.Count > 0 ? others : candidates;
            return pool[random.Next(pool.Count)];
        }

        private IEnumerator WaitForVersionChange(long version, float timeout)
        {
            float waited = 0f;
            while (waited < timeout)
            {
                GameSession session = Session;
                if (session == null || session.State == null || session.State.phaseVersion != version)
                {
                    yield break;
                }
                waited += Time.deltaTime;
                yield return null;
            }
        }

        private void Finish(string message)
        {
            Log(message);
            SetStatus(message);
            if (startButtonText != null)
            {
                startButtonText.text = "다시 시작";
            }
            SetButtonVisible(true);
            running = null;
        }

        private void SetButtonVisible(bool visible)
        {
            if (startButton != null)
            {
                startButton.gameObject.SetActive(visible);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private static void Log(string message)
        {
            Debug.Log("[GameTest] " + message);
        }
    }
}
