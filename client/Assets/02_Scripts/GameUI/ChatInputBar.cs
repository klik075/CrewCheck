using System;
using TMPro;
using WhoisntCitizen.Common;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// 채팅 입력 바: [채팅 입력..........] [전송]
    /// GameScene 하단 탭 바(BottomTabController)에서 [+] 버튼과 서랍(Drawer)을 뺀 간단한 버전입니다. (Room 씬 대기실 채팅)
    /// Enter 또는 전송 버튼으로 ChatSubmitted 이벤트를 보내고, 입력칸을 비운 뒤 포커스를 유지합니다.
    /// 서버 통신은 하지 않습니다. GameChatController가 ChatSubmitted를 받아 서버로 보냅니다.
    /// </summary>
    public class ChatInputBar : MonoBehaviour
    {
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private Button sendButton;

        public event Action<string> ChatSubmitted;

        private void OnEnable()
        {
            if (chatInput != null) chatInput.onSubmit.AddListener(Submit);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendClicked);
        }

        private void OnDisable()
        {
            if (chatInput != null) chatInput.onSubmit.RemoveListener(Submit);
            if (sendButton != null) sendButton.onClick.RemoveListener(OnSendClicked);
        }

        private void OnSendClicked()
        {
            Submit(chatInput != null ? chatInput.text : null);
        }

        private void Submit(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatSubmitted?.Invoke(text);
            // 한글 IME 조합 중 Enter로 보낸 경우를 위해 다음 프레임에 비운다 (InputFieldExtensions 참고)
            if (chatInput != null) chatInput.ClearAndRefocus(this);
        }
    }
}
