using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// 슬라이더 형태의 뮤트 스위치. (0 = 소리 켜짐, 1 = 뮤트)
/// 드래그/클릭 후 손을 떼면 가까운 쪽(0 또는 1)으로 스냅하고, 그 값으로 뮤트를 적용한다.
/// </summary>
[RequireComponent(typeof(Slider))]
public class AudioMuteSlider : MonoBehaviour, IPointerUpHandler
{
    public AudioChannelType ChannelType;

    // Runtime Value
    private Slider slider;
    private AudioMuteChannelData audioMuteChannelData;

    private const int MinValue = 0;
    private const int MaxValue = 1;

    private void Awake()
    {
        slider = GetComponent<Slider>();

        slider.minValue = MinValue;
        slider.maxValue = MaxValue;
        slider.wholeNumbers = false;

        audioMuteChannelData = AudioSystemManager.Instance.audioSaveData.GetOrAddMuteChannel(ChannelType.ToString());
        audioMuteChannelData.OnChangeMute += SyncFromData;

        SyncFromData();
    }

    private void OnDestroy()
    {
        if (audioMuteChannelData != null) audioMuteChannelData.OnChangeMute -= SyncFromData;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (audioMuteChannelData == null || !slider.interactable) return;
        int target = slider.value >= 0.5f ? MaxValue : MinValue;
        SnapTo(target);
    }

    private void SnapTo(int target)
    {
        slider.DOKill();
        slider.DOValue(target, 0.15f).SetEase(Ease.OutQuad).SetLink(gameObject).OnComplete(() =>
        {
            audioMuteChannelData.SetMute(target == MaxValue);
        });
    }

    // 다른 UI(버튼 등)에서 뮤트가 바뀌었을 때 슬라이더 위치 동기화
    private void SyncFromData()
    {
        if (slider == null) return;
        slider.DOKill();
        slider.SetValueWithoutNotify(audioMuteChannelData.IsMute ? MaxValue : MinValue);
    }
}
