using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Vote
{
    // 플레이어 프로필 카드 하나. 표시만 담당하고, 클릭하면 playerId를 콜백으로 넘긴다.
    // 투표뿐 아니라 밤 능력 대상 선택 등 "플레이어 한 명 고르기" UI에 재사용할 수 있다.
    public class PlayerProfileItem : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI nicknameText;
        [SerializeField] private GameObject selectedFrame;
        [SerializeField] private GameObject meBadge;
        [SerializeField] private GameObject voteCountBadge;
        [SerializeField] private TextMeshProUGUI voteCountText;
        [SerializeField] private GameObject deadOverlay;

        [Header("Style")]
        [SerializeField, Range(0f, 1f)] private float deadAlpha = 0.55f;

        public long PlayerId { get; private set; }
        public string Nickname { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public bool IsMe { get; private set; }

        private Action<long> onClick;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        public void Setup(PlayerView data, bool isMe, Sprite portrait, Action<long> clickCallback)
        {
            PlayerId = data.playerId;
            IsMe = isMe;
            onClick = clickCallback;
            name = $"PlayerProfileItem_{data.playerId}";

            if (portraitImage != null)
            {
                portraitImage.sprite = portrait;
                portraitImage.enabled = portrait != null;
            }
            if (meBadge != null) meBadge.SetActive(isMe);

            SetSelected(false);
            SetVoteCount(0);
            Refresh(data);
        }

        // 폴링으로 받은 최신 상태 반영 (아이템을 다시 만들지 않는다)
        public void Refresh(PlayerView data)
        {
            Nickname = data.nickname;
            if (nicknameText != null) nicknameText.text = data.nickname;
            SetDead(!data.alive);
        }

        public void SetSelected(bool selected)
        {
            if (selectedFrame != null) selectedFrame.SetActive(selected);
        }

        public void SetDead(bool dead)
        {
            IsAlive = !dead;
            if (deadOverlay != null) deadOverlay.SetActive(dead);
            if (canvasGroup != null) canvasGroup.alpha = dead ? deadAlpha : 1f;
        }

        public void SetClickable(bool clickable)
        {
            if (button != null) button.interactable = clickable;
        }

        public void SetVoteCount(int count)
        {
            if (voteCountBadge != null) voteCountBadge.SetActive(count > 0);
            if (voteCountText != null) voteCountText.text = count.ToString();
        }

        private void HandleClick() => onClick?.Invoke(PlayerId);
    }
}
