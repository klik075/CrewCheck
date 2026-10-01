using TMPro;
using UnityEngine;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 상태/에러 메시지를 보여주는 텍스트 컴포넌트.
    /// StatusMessageText 오브젝트(TextMeshPro 텍스트)에 붙여서 사용한다.
    ///
    /// 사용법
    ///   statusMessage.ShowInfo("방 목록을 불러오는 중...");
    ///   statusMessage.ShowError("방이 가득 찼습니다.");
    ///
    /// - 종류(정보/성공/에러)에 따라 글자색이 바뀐다.
    /// - autoHideSeconds가 지나면 메시지가 자동으로 지워진다. (0이면 계속 유지)
    /// - 자동 숨김은 코루틴이 아니라 Update에서 처리한다.
    ///   비활성화된 오브젝트(예: 닫혀 있는 팝업)에서 호출해도 에러가 나지 않게 하기 위해서다.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class StatusMessageView : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color infoColor = Color.white;
        [SerializeField] private Color successColor = new Color(0.43f, 0.91f, 0.53f); // 초록
        [SerializeField] private Color errorColor = new Color(1f, 0.48f, 0.45f);      // 빨강

        [Header("Auto Hide")]
        [Tooltip("메시지를 표시한 뒤 자동으로 지우기까지의 시간(초). 0이면 지우지 않는다.")]
        [SerializeField] private float autoHideSeconds = 4f;

        private TMP_Text text;
        private float hideAt = -1f;     // 메시지를 지울 시각 (Time.unscaledTime 기준, -1이면 예약 없음)
        private string currentMessage;  // 현재 표시 중인 메시지 (없으면 null)

        // Awake 전에 Show가 호출될 수 있어서(비활성 오브젝트 등) 필요할 때 컴포넌트를 가져온다.
        private TMP_Text Text => text != null ? text : (text = GetComponent<TMP_Text>());

        private void Awake()
        {
            // 씬에 적혀 있던 임시 텍스트는 시작할 때 지운다. (이미 메시지가 표시 중이면 유지)
            if (string.IsNullOrEmpty(currentMessage)) Text.text = string.Empty;
        }

        private void Update()
        {
            if (hideAt > 0f && Time.unscaledTime >= hideAt) Clear();
        }

        /// <summary>현재 표시 중인 메시지 (없으면 null). 특정 문구가 떠 있는지 확인할 때 사용</summary>
        public string CurrentMessage => currentMessage;

        /// <summary>일반 안내 메시지 (흰색)</summary>
        public void ShowInfo(string message, bool keep = false) => Show(message, infoColor, keep);

        /// <summary>성공 메시지 (초록색)</summary>
        public void ShowSuccess(string message, bool keep = false) => Show(message, successColor, keep);

        /// <summary>에러 메시지 (빨간색)</summary>
        public void ShowError(string message, bool keep = false) => Show(message, errorColor, keep);

        /// <summary>메시지를 지운다.</summary>
        public void Clear()
        {
            currentMessage = null;
            hideAt = -1f;
            Text.text = string.Empty;
        }

        /// <param name="keep">true면 자동 숨김 없이 계속 표시한다. (예: "로딩 중...")</param>
        private void Show(string message, Color color, bool keep)
        {
            currentMessage = message;
            Text.text = message;
            Text.color = color;
            hideAt = (!keep && autoHideSeconds > 0f) ? Time.unscaledTime + autoHideSeconds : -1f;
        }
    }
}
