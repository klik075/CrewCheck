using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체크박스(Toggle) 형태의 뮤트 스위치. Master / BGM / SFX 공용.
/// - 체크 = 소리 켜짐, 체크 해제 = 뮤트
/// - volumeSlider 를 연결하면 뮤트 상태일 때 슬라이더를 비활성화(흐리게)한다.
/// - 마스터가 뮤트면 BGM/SFX 토글은 체크 해제로 보이고 토글·슬라이더 모두 비활성화된다.
///   (BGM/SFX 자신의 뮤트 설정은 바꾸지 않으므로, 마스터를 다시 켜면 원래 상태로 돌아온다)
/// </summary>
[RequireComponent(typeof(Toggle))]
public class AudioMuteToggle : MonoBehaviour
{
    public AudioVolumeTarget Target = AudioVolumeTarget.Master;

    [Tooltip("선택. 같은 대상의 볼륨 슬라이더. 뮤트일 때 조작할 수 없게 된다")]
    public Slider volumeSlider;

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    private void OnEnable()
    {
        AudioSystemManager.OnChangeMute += Sync;
        Sync();
    }

    private void OnDisable()
    {
        AudioSystemManager.OnChangeMute -= Sync;
    }

    private void OnDestroy()
    {
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }

    private void OnToggleChanged(bool isOn)
    {
        bool mute = !isOn;
        switch (Target)
        {
            case AudioVolumeTarget.Master:
                AudioSystemManager.SetMasterMute(mute);
                break;
            case AudioVolumeTarget.BGM:
                if (AudioSystemManager.IsMasterMute()) { Sync(); return; } // 마스터 뮤트 중에는 변경 불가
                AudioSystemManager.SetMute(AudioChannelType.BGM, mute);
                break;
            case AudioVolumeTarget.SFX:
                if (AudioSystemManager.IsMasterMute()) { Sync(); return; }
                AudioSystemManager.SetMute(AudioChannelType.SFX, mute);
                break;
        }
        Sync();
    }

    /// <summary>저장된 뮤트 상태로 토글/슬라이더 표시를 맞춘다.</summary>
    private void Sync()
    {
        if (toggle == null) return;

        bool masterMuted = AudioSystemManager.IsMasterMute();
        bool blockedByMaster = Target != AudioVolumeTarget.Master && masterMuted;

        bool selfMuted = Target switch
        {
            AudioVolumeTarget.BGM => AudioSystemManager.IsMute(AudioChannelType.BGM),
            AudioVolumeTarget.SFX => AudioSystemManager.IsMute(AudioChannelType.SFX),
            _ => masterMuted,
        };

        bool showMuted = selfMuted || blockedByMaster;

        toggle.SetIsOnWithoutNotify(!showMuted);
        toggle.interactable = !blockedByMaster;
        if (volumeSlider != null) volumeSlider.interactable = !showMuted;
    }
}
