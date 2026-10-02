using System;
using TMPro;
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
            if (chatInput != null)
            {
                chatInput.text = "";
                chatInput.ActivateInputField(); // 연속 입력이 가능하도록 포커스 유지
            }
        }
    }
}
