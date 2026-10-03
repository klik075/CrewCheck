using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Game
{
    /// <summary>게임 결과 패널. 승리 진영, 내 승패, 전원의 실제 직업. "대기실로" 버튼을 누르면 BackRequested.</summary>
    public sealed class GameResultPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TextMeshProUGUI winnerText;
        [SerializeField] private TextMeshProUGUI outcomeText;
        [SerializeField] private TextMeshProUGUI playersText;
        [SerializeField] private Button backButton;

        /// <summary>"대기실로" 버튼</summary>
        public event Action BackRequested;

        /// <summary>켜고 끌 오브젝트. 비어 있으면 자기 자신 (꺼진 채로 시작해 Awake 전에 불려도 안전하게).</summary>
        private GameObject Root
        {
            get { return root != null ? root : gameObject; }
        }

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackClicked);
            }
        }

        private void OnDestroy()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackClicked);
            }
        }

        public void Show(GameResultDto result, MyRoleDto me)
        {
            if (winnerText != null)
            {
                winnerText.text = ReportFormatter.ResultHeadline(result); // 취소된 게임이면 "게임 취소"
            }
            if (outcomeText != null)
            {
                outcomeText.text = GameScreenText.ResultOutcome(result, me != null ? me.faction : null);
            }
            if (playersText != null)
            {
                playersText.text = GameScreenText.NoRichText(string.Join("\n", ReportFormatter.ResultLines(result).ToArray()));
            }
            Root.SetActive(true);
        }

        public void Hide()
        {
            Root.SetActive(false);
        }

        private void OnBackClicked()
        {
            if (BackRequested != null)
            {
                BackRequested();
            }
        }
    }
}
