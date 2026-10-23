using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 버튼 기반 오디오 뮤트 토글.
/// knob 이동 애니메이션은 유지하고,
/// 배경은 On/Off 오브젝트를 애니메이션 중간 시점에 교체한다.
/// </summary>
public class AudioMuteToggleButton : MonoBehaviour
{
    [Header("Audio")]
    public AudioChannelType ChannelType;

    [Header("UI References")]
    public Button toggleButton;
    public RectTransform knob;
    public GameObject backgroundOnObject;  // unmuted(On)
    public GameObject backgroundOffObject; // muted(Off)

    [Header("Visual Settings")]
    public Vector2 knobOnAnchoredPos;
    [SerializeField] private bool useCustomKnobOffPosition;
    [SerializeField] private Vector2 knobOffAnchoredPos;
    [SerializeField] private float toggleAnimDuration = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float backgroundSwapNormalizedTime = 0.5f;

    private Vector2 _knobOffAnchoredPos;
    private AudioMuteChannelData _muteData;
    private Tween _knobTween;
    private Tween _backgroundSwapTween;

    private void Start()
    {
        if (toggleButton == null)
        {
            toggleButton = GetComponent<Button>();
            if (toggleButton == null)
                toggleButton = gameObject.AddComponent<Button>();
        }

        toggleButton.onClick.AddListener(ToggleMute);

        _muteData = AudioSystemManager.Instance.audioSaveData.GetOrAddMuteChannel(ChannelType.ToString());
        if (_muteData != null)
        {
            _muteData.OnChangeMute += HandleMuteChanged;
        }

        if (knob != null)
        {
            _knobOffAnchoredPos = useCustomKnobOffPosition ? knobOffAnchoredPos : knob.anchoredPosition;
            if (knobOnAnchoredPos == Vector2.zero)
            {
                knobOnAnchoredPos = new Vector2(-_knobOffAnchoredPos.x, _knobOffAnchoredPos.y);
            }
        }

        RefreshVisual(true);
    }

    private void OnDisable()
    {
        KillTweens();
    }

    private void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(ToggleMute);

        if (_muteData != null)
            _muteData.OnChangeMute -= HandleMuteChanged;

        KillTweens();
    }

    private void ToggleMute()
    {
        if (_muteData == null)
            return;

        _muteData.SetMute(!_muteData.IsMute);
        RefreshVisual();
    }

    private void HandleMuteChanged()
    {
        RefreshVisual();
    }

    private void RefreshVisual(bool instant = false)
    {
        bool isMute = _muteData != null && _muteData.IsMute;
        bool isOn = !isMute;

        KillTweens();

        if (instant || !Application.isPlaying || knob == null)
        {
            ApplyBackgroundState(isOn);

            if (knob != null)
            {
                Vector2 targetPos = isOn ? knobOnAnchoredPos : _knobOffAnchoredPos;
                knob.anchoredPosition = targetPos;
            }

            return;
        }

        float swapDelay = toggleAnimDuration * Mathf.Clamp01(backgroundSwapNormalizedTime);
        _backgroundSwapTween = DOVirtual.DelayedCall(swapDelay, () => ApplyBackgroundState(isOn)).SetLink(gameObject);

        Vector2 animatedTargetPos = isOn ? knobOnAnchoredPos : _knobOffAnchoredPos;
        _knobTween = knob.DOAnchorPos(animatedTargetPos, toggleAnimDuration)
            .SetEase(Ease.OutQuad)
            .SetLink(knob.gameObject);
    }

    private void ApplyBackgroundState(bool isOn)
    {
        if (backgroundOnObject != null)
            backgroundOnObject.SetActive(isOn);

        if (backgroundOffObject != null)
            backgroundOffObject.SetActive(!isOn);
    }

    private void KillTweens()
    {
        _knobTween?.Kill();
        _knobTween = null;

        _backgroundSwapTween?.Kill();
        _backgroundSwapTween = null;
    }

    private void OnValidate()
    {
        if (toggleAnimDuration < 0f)
            toggleAnimDuration = 0f;

        backgroundSwapNormalizedTime = Mathf.Clamp01(backgroundSwapNormalizedTime);

        if (!useCustomKnobOffPosition)
            return;

        _knobOffAnchoredPos = knobOffAnchoredPos;
    }
}
