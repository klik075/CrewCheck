using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 내 친구 목록의 한 줄 (FriendItem 프리팹 루트에 부착).
    /// FriendUIController가 목록을 받을 때마다 프리팹을 생성하고 Bind()로 데이터를 넣는다.
    /// 삭제 버튼을 눌러도 실제 요청은 하지 않고 FriendUIController에 알리기만 한다.
    /// </summary>
    public class FriendItemView : MonoBehaviour
    {
        [Header("UI (FriendItem 프리팹의 자식)")]
        [SerializeField] private TMP_Text nicknameText;
        [Tooltip("레벨/전적 표시. 예) Lv.3 · 12전 7승 · 58.3%")]
        [SerializeField] private TMP_Text recordText;
        [SerializeField] private Button removeButton;

        private FriendResponse friend;
        private Action<FriendResponse> onRemove;

        public FriendResponse Friend => friend;

        private void Awake()
        {
            if (removeButton != null && removeButton.onClick.GetPersistentEventCount() == 0)
                removeButton.onClick.AddListener(OnRemoveClicked);
        }

        private void OnDestroy()
        {
            if (removeButton != null) removeButton.onClick.RemoveListener(OnRemoveClicked);
        }

        public void Bind(FriendResponse data, Action<FriendResponse> onRemoveClicked)
        {
            friend = data;
            onRemove = onRemoveClicked;

            if (nicknameText != null) nicknameText.text = data.nickname;
            if (recordText != null)
                recordText.text = $"Lv.{data.level} · {data.playCount}전 {data.winCount}승 · {data.WinRatePercent:0.#}%";
        }

        /// <summary>요청 중에 다른 항목을 누르지 못하도록 버튼을 잠그거나 푼다.</summary>
        public void SetLocked(bool value)
        {
            if (removeButton != null) removeButton.interactable = !value;
        }

        public void OnRemoveClicked()
        {
            if (friend != null) onRemove?.Invoke(friend);
        }
    }
}
