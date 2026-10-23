using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// GameScene3의 낮 원탁 배치 값 (Resources/DayTable/RoundTableLayout). 인스펙터에서 바꾸면 Play 중인 원탁이 바로 다시
    /// 그려진다. Play 중에 바꾼 값도 이 에셋에 그대로 남는다 (씬 오브젝트와 다르다).
    /// Play 전(Edit 모드)에는 씬에 원탁 미리보기가 떠 있고, 값을 바꾸면 그것이 다시 그려진다 (Assets/Editor/RoundTablePreview.cs).
    /// 자리는 인원수만큼 테이블 둘레에 같은 간격으로 놓인다: 테이블을 감싼 타원(자리 고리) 위에 각 캐릭터의 몸 가운데를 둔다.
    /// 원탁 단위: 높이 850짜리 원탁 상자 기준, 가운데가 0, 위가 +. 무대 높이에 맞춰 줄거나 커진다(무대 높이 / 850배).
    /// 화면 단위(무대, 갑판): 가로 1080 화면 기준, 위에서부터.
    /// </summary>
    [CreateAssetMenu(fileName = "RoundTableLayout", menuName = "Game UI/Round Table Layout")]
    public sealed class RoundTableLayout : ScriptableObject
    {
        [Header("무대 (화면 단위: 가로 1080, 위에서부터)")]
        [Tooltip("무대 윗변. 상단 HUD는 0~236. 원탁과 밤 방이 이 칸에 들어간다")]
        public float stageTop = 236f;
        [Tooltip("무대 높이. 원탁 전체가 이 높이에 맞춰 줄거나 커진다(높이 850 기준). 무대 아래부터 채팅이 시작한다. 밤 방은 다음 Play부터 바뀐다")]
        public float stageHeight = 810f;
        [Tooltip("갑판 배경을 위로 올린 정도. 클수록 바다가 덜 보이고 갑판이 위로 온다")]
        public float deckRaise = 380f;

        [Header("테이블 (원탁 단위)")]
        [Tooltip("테이블 그림의 너비")]
        [Range(400f, 1000f)] public float tableWidth = 765f;
        [Tooltip("테이블 위아래 위치. 자리도 같이 움직인다")]
        [Range(-250f, 250f)] public float tableY = 50f;

        [Header("자리 (원탁 단위)")]
        [Tooltip("의자 높이(12명 기준). 캐릭터는 의자 높이의 1.12배. 앞줄은 조금 크게(1.1배), 뒷줄은 조금 작게(0.9배) 그린다")]
        [Range(80f, 320f)] public float chairSize = 184f;
        [Tooltip("인원이 4명일 때 의자 크기 배율 (4~12명 사이는 비례)")]
        [Range(0.8f, 1.5f)] public float fewPlayersScale = 1.12f;
        [Tooltip("자리 고리가 테이블 좌우 끝에서 떨어진 거리. 작을수록 옆자리가 테이블에 붙는다")]
        [Range(-150f, 250f)] public float ringPaddingX = 40f;
        [Tooltip("자리 고리가 테이블 위(뒤 테두리)·아래(받침 아래)에서 떨어진 거리. 작을수록 앞뒤 자리가 테이블에 붙는다")]
        [Range(-150f, 250f)] public float ringPaddingY = 30f;
        [Tooltip("자리 고리 전체를 위아래로 옮긴다 (테이블은 그대로)")]
        [Range(-200f, 200f)] public float ringOffsetY = 0f;
        [Tooltip("첫 자리(서버 순서 0번)의 각도. 0이면 12시, 180이면 6시. 나머지는 시계 방향으로 같은 간격")]
        [Range(-180f, 180f)] public float seatRotation = 0f;

        [Header("기타")]
        [Tooltip("테이블 위 투표 카드 자리가 상판 가장자리에서 들어온 정도 (1이면 상판 테두리)")]
        [Range(0.3f, 1f)] public float cardSpotRadius = 0.7f;
        [Tooltip("자리 이름 글자 크기")]
        [Range(14f, 40f)] public float nameSize = 26f;

        [Header("Edit 모드 미리보기 (Tools/Game Scene/원탁 미리보기)")]
        [Tooltip("씬을 편집하는 동안 원탁에 앉혀 보는 인원. 게임에는 쓰지 않는다 (게임은 실제 인원수만큼 앉는다)")]
        [Range(4, 12)] public int previewPlayers = 8;

        /// <summary>Play 중에 인스펙터에서 값이 바뀌었다.</summary>
        public static event Action Changed;

        /// <summary>Play 중이 아닐 때 값이 바뀌었다 (Edit 모드 원탁 미리보기가 다시 그린다).</summary>
        public static event Action EditModeChanged;

        private static RoundTableLayout current;

        /// <summary>Resources/DayTable/RoundTableLayout, 없으면 기본값.</summary>
        public static RoundTableLayout Current
        {
            get
            {
                if (current == null) current = Resources.Load<RoundTableLayout>("DayTable/RoundTableLayout");
                if (current == null) current = CreateInstance<RoundTableLayout>();
                return current;
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Changed?.Invoke();
            else EditModeChanged?.Invoke();
        }
    }
}
