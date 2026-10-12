using System.Collections;
using TMPro;
using UnityEngine;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// TMP_InputField를 전송 후 비우고 포커스를 유지하는 도우미.
    ///
    /// 한글 IME로 글자를 조합하는 중에 Enter로 전송하고 같은 프레임에 text = ""로 비우면,
    /// 조합 중이던 글자가 뒤늦게 확정되거나 입력칸의 내부 커서 위치(stringPosition)가 예전 길이로 남는다.
    /// 그 상태에서 Backspace를 누르면 TMP_InputField.Backspace()에서
    /// ArgumentOutOfRangeException(String.Remove)이 난다.
    /// 그래서 한 프레임 기다려 IME가 조합을 끝낸 뒤 비우고, 커서·선택 위치도 0으로 맞춘다.
    /// </summary>
    public static class InputFieldExtensions
    {
        /// <summary>다음 프레임에 입력칸을 비우고 다시 포커스한다. (채팅 연속 입력용)</summary>
        public static void ClearAndRefocus(this TMP_InputField input, MonoBehaviour host)
        {
            if (input == null) return;
            if (host == null || !host.isActiveAndEnabled)
            {
                input.ClearSafely();
                return;
            }
            host.StartCoroutine(ClearNextFrame(input));
        }

        /// <summary>입력칸을 바로 비우고 커서·선택 위치를 0으로 맞춘다. (onValueChanged는 부르지 않음)</summary>
        public static void ClearSafely(this TMP_InputField input)
        {
            if (input == null) return;
            input.SetTextWithoutNotify(string.Empty);
            input.stringPosition = 0; // 문자열 기준 커서·선택 위치를 함께 0으로
            input.caretPosition = 0;
            input.selectionAnchorPosition = 0;
            input.selectionFocusPosition = 0;
            input.ForceLabelUpdate(); // 글자 정보(textInfo)를 빈 문자열 기준으로 다시 계산
        }

        private static IEnumerator ClearNextFrame(TMP_InputField input)
        {
            yield return null; // IME가 조합 중이던 글자를 확정할 시간을 준다
            if (input == null || !input.gameObject.activeInHierarchy) yield break;
            input.ClearSafely();
            input.ActivateInputField(); // 연속 입력이 가능하도록 포커스 유지
        }
    }
}
