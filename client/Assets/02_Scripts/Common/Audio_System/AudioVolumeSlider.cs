using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum AudioVolumeTarget
{
    Master = 0,
    BGM,
    SFX,
}

/// <summary>
/// 마스터 / BGM / 효과음 볼륨 슬라이더.
/// Unity Slider 에 붙이고 Target 만 고르면 된다. (0~1, 실제 적용은 제곱 커브)
/// - 열릴 때 저장된 값으로 위치를 맞춘다.
/// - 움직이면 즉시 적용, 저장은 손을 뗀 뒤 잠시 후 한 번.
/// - 같은 대상을 조절하는 다른 슬라이더와 자동 동기화.
/// 뮤트와는 별개다. (뮤트는 AudioMute* 컴포넌트)
/// </summary>
[RequireComponent(typeof(Slider))]
public class AudioVolumeSlider : MonoBehaviour
{
    public AudioVolumeTarget Target = AudioVolumeTarget.Master;

    [Tooltip("선택. 퍼센트 표시용 텍스트 (예: 70%)")]
    public TMP_Text valueText;

    private Slider slider;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(OnSliderChanged);
    }

    private void OnEnable()
    {
        AudioSystemManager.OnChangeVolume += SyncFromData;
        SyncFromData();
    }

    private void OnDisable()
    {
        AudioSystemManager.OnChangeVolume -= SyncFromData;
    }

    private void OnDestroy()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void OnSliderChanged(float value)
    {
        switch (Target)
        {
            case AudioVolumeTarget.Master: AudioSystemManager.SetMasterVolume(value); break;
            case AudioVolumeTarget.BGM: AudioSystemManager.SetVolume(AudioChannelType.BGM, value); break;
            case AudioVolumeTarget.SFX: AudioSystemManager.SetVolume(AudioChannelType.SFX, value); break;
        }
        UpdateText(value);
    }

    private void SyncFromData()
    {
        if (slider == null) return;
        float value = GetValue();
        slider.SetValueWithoutNotify(value);
        UpdateText(value);
    }

    private float GetValue() => Target switch
    {
        AudioVolumeTarget.BGM => AudioSystemManager.GetVolume(AudioChannelType.BGM),
        AudioVolumeTarget.SFX => AudioSystemManager.GetVolume(AudioChannelType.SFX),
        _ => AudioSystemManager.GetMasterVolume(),
    };

    private void UpdateText(float value)
    {
        if (valueText != null) valueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }
}
