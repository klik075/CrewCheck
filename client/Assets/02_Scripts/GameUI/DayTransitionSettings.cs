using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// How long each beat of the day/night cut-scenes lasts (<see cref="DayTransitionView"/>), in seconds.
    /// The scenes are drawn at their default timing; a longer or shorter beat plays its part slower or faster.
    /// Edit Resources/DayTransition/DayTransitionSettings (Tools > 낮·밤 전환 연출 시간 shows them with a preview);
    /// a cut-scene reads them each time it starts, so changes made while playing show on the next one.
    /// </summary>
    [CreateAssetMenu(fileName = "DayTransitionSettings", menuName = "Game UI/Day Transition Settings")]
    public sealed class DayTransitionSettings : ScriptableObject
    {
        public const string ResourcePath = "DayTransition/DayTransitionSettings";
        public const float MinBeat = 0.05f;

        [Serializable]
        public sealed class KilledBeats
        {
            [Tooltip("밤이 걷힘: 화면이 어두워지고 \"밤이 지나갑니다\"")] public float nightLifts = 0.7f;
            [Tooltip("습격: 붉은 칼자국 세 줄과 흔들림")] public float strike = 0.8f;
            [Tooltip("희생자: 수배서가 떨어지고 이름이 보임")] public float victim = 1.7f;
            [Tooltip("새벽: 해돋이와 \"N일차 낮\" 명패 (여기서 낮을 기다린다)")] public float dawn = 0.8f;
        }

        [Serializable]
        public sealed class ProtectedBeats
        {
            [Tooltip("밤이 걷힘")] public float nightLifts = 0.7f;
            [Tooltip("습격: 사망과 같은 칼자국")] public float strike = 0.8f;
            [Tooltip("막아냄: 금빛 고리와 \"선의의 보호\"")] public float shield = 1.3f;
            [Tooltip("새벽: \"N일차 낮\" 명패 (여기서 낮을 기다린다)")] public float dawn = 0.9f;
        }

        [Serializable]
        public sealed class QuietBeats
        {
            [Tooltip("밤이 걷힘")] public float nightLifts = 0.7f;
            [Tooltip("고요한 밤바다")] public float calmSea = 0.9f;
            [Tooltip("해돋이: \"N일차 낮\" 명패 (여기서 낮을 기다린다)")] public float sunrise = 1.0f;
        }

        [Serializable]
        public sealed class ExecutedBeats
        {
            [Tooltip("개표: 테이블의 카드 더미를 그대로 보여 준다")] public float tally = 0.8f;
            [Tooltip("판결: 판결문이 펼쳐지고 \"처형\" 도장")] public float verdict = 1.0f;
            [Tooltip("널빤지: 끝에 서서 떨다가 떨어진다")] public float plank = 1.3f;
            [Tooltip("첨벙: 물보라와 처형 문구")] public float splash = 0.8f;
            [Tooltip("밤으로: \"N일차 밤\" 명패 (여기서 밤을 기다린다)")] public float nightFalls = 0.6f;
        }

        [Serializable]
        public sealed class NotExecutedBeats
        {
            [Tooltip("개표")] public float tally = 0.8f;
            [Tooltip("판결 불발: \"무효\" 도장이 갈라짐")] public float verdict = 1.4f;
            [Tooltip("밤으로: \"N일차 밤\" 명패 (여기서 밤을 기다린다)")] public float nightFalls = 0.6f;
        }

        [Serializable]
        public sealed class DraggedBeats
        {
            [Tooltip("밤이 걷힘")] public float nightLifts = 0.7f;
            [Tooltip("수면이 갈라짐: 청록 물결 고리와 물밑의 주황 눈")] public float rise = 0.6f;
            [Tooltip("촉수: 크라켄 그림이 덮치며 두 번 흔들림")] public float tentacle = 0.9f;
            [Tooltip("끌려간 자들: 먹물에 젖은 수배서가 아래로 끌려감 (2명부터는 한 명마다 조금씩 길어진다)")] public float victims = 1.4f;
            [Tooltip("잔물결·새벽: 먹물 자국과 \"N일차 낮\" 명패 (여기서 낮을 기다린다)")] public float dawn = 0.8f;
        }

        [Header("밤사이 사망")] public KilledBeats killed = new KilledBeats();
        [Header("선의 보호로 생존")] public ProtectedBeats protectedNight = new ProtectedBeats();
        [Header("아무 일 없음")] public QuietBeats quiet = new QuietBeats();
        [Header("투표로 처형")] public ExecutedBeats executed = new ExecutedBeats();
        [Header("처형 없음 (동점·투표 없음)")] public NotExecutedBeats notExecuted = new NotExecutedBeats();
        [Header("크라켄에게 끌려감")] public DraggedBeats dragged = new DraggedBeats();

        [Header("공통")]
        [Tooltip("마지막 명패에서 레터박스가 열리며 걷히는 시간")] public float outLength = 0.5f;
        [Tooltip("마지막 명패에서 다음 페이즈를 기다리는 최대 시간. 넘으면 기다리지 않고 걷힌다")] public float maxHold = 2.5f;

        private static DayTransitionSettings fallback;

        /// <summary>The settings asset in Resources, or the defaults when there is none.</summary>
        public static DayTransitionSettings Load()
        {
            DayTransitionSettings settings = Resources.Load<DayTransitionSettings>(ResourcePath);
            if (settings != null) return settings;
            if (fallback == null)
            {
                fallback = CreateInstance<DayTransitionSettings>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }

        /// <summary>This scene's beats in order, each at least <see cref="MinBeat"/> seconds.</summary>
        public float[] Beats(DayTransitionKind kind)
        {
            float[] beats;
            switch (kind)
            {
                case DayTransitionKind.Killed: beats = new[] { killed.nightLifts, killed.strike, killed.victim, killed.dawn }; break;
                case DayTransitionKind.Protected: beats = new[] { protectedNight.nightLifts, protectedNight.strike, protectedNight.shield, protectedNight.dawn }; break;
                case DayTransitionKind.Quiet: beats = new[] { quiet.nightLifts, quiet.calmSea, quiet.sunrise }; break;
                case DayTransitionKind.Executed: beats = new[] { executed.tally, executed.verdict, executed.plank, executed.splash, executed.nightFalls }; break;
                case DayTransitionKind.Dragged: beats = new[] { dragged.nightLifts, dragged.rise, dragged.tentacle, dragged.victims, dragged.dawn }; break;
                default: beats = new[] { notExecuted.tally, notExecuted.verdict, notExecuted.nightFalls }; break;
            }
            for (int i = 0; i < beats.Length; i++) beats[i] = Mathf.Max(MinBeat, beats[i]);
            return beats;
        }

        /// <summary>The timing the scenes are drawn at (the defaults above), beat by beat.</summary>
        public static float[] DesignBeats(DayTransitionKind kind)
        {
            switch (kind)
            {
                case DayTransitionKind.Killed: return new[] { 0.7f, 0.8f, 1.7f, 0.8f };
                case DayTransitionKind.Protected: return new[] { 0.7f, 0.8f, 1.3f, 0.9f };
                case DayTransitionKind.Quiet: return new[] { 0.7f, 0.9f, 1.0f };
                case DayTransitionKind.Executed: return new[] { 0.8f, 1.0f, 1.3f, 0.8f, 0.6f };
                case DayTransitionKind.Dragged: return new[] { 0.7f, 0.6f, 0.9f, 1.4f, 0.8f };
                default: return new[] { 0.8f, 1.4f, 0.6f };
            }
        }

        /// <summary>Field names (for SerializedObject) and Korean labels of a scene's beats, in order.</summary>
        public static (string field, string label)[] BeatFields(DayTransitionKind kind)
        {
            switch (kind)
            {
                case DayTransitionKind.Killed:
                    return new[] { ("killed.nightLifts", "밤이 걷힘"), ("killed.strike", "습격"), ("killed.victim", "희생자 수배서"), ("killed.dawn", "새벽 · N일차 낮") };
                case DayTransitionKind.Protected:
                    return new[] { ("protectedNight.nightLifts", "밤이 걷힘"), ("protectedNight.strike", "습격"), ("protectedNight.shield", "막아냄 · 선의의 보호"), ("protectedNight.dawn", "새벽 · N일차 낮") };
                case DayTransitionKind.Quiet:
                    return new[] { ("quiet.nightLifts", "밤이 걷힘"), ("quiet.calmSea", "고요한 밤바다"), ("quiet.sunrise", "해돋이 · N일차 낮") };
                case DayTransitionKind.Executed:
                    return new[] { ("executed.tally", "개표 (테이블)"), ("executed.verdict", "판결 · 도장"), ("executed.plank", "널빤지"), ("executed.splash", "첨벙"), ("executed.nightFalls", "밤으로 · N일차 밤") };
                case DayTransitionKind.Dragged:
                    return new[] { ("dragged.nightLifts", "밤이 걷힘"), ("dragged.rise", "수면이 갈라짐"), ("dragged.tentacle", "촉수"), ("dragged.victims", "끌려간 자들"), ("dragged.dawn", "잔물결 · N일차 낮") };
                default:
                    return new[] { ("notExecuted.tally", "개표 (테이블)"), ("notExecuted.verdict", "판결 불발 · 무효"), ("notExecuted.nightFalls", "밤으로 · N일차 밤") };
            }
        }
    }
}
