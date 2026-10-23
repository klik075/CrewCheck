using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 게임 내에서 오디오를 재생하는 컴포넌트.
/// - PlayOnEnable = true : 오브젝트 활성화 시 audioClipcontainer 를 자동 재생 (BGM / 상시 사운드용, 풀로 반환되지 않음)
/// - PlayOnEnable = false: AudioSystemManager.Play(...) 로 풀에서 꺼내 쓰는 단발성 효과음. 재생이 끝나면 자동으로 풀에 반환된다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioPlayer : MonoBehaviour
{
    public AudioClipContainer audioClipcontainer;

    public bool PlayOnEnable;
    public bool UseScaledTime;
    public bool loop;

    [Range(0, 1)]
    public float AudioCursor;

    // Runtime Value
    private bool isInit = false;
    private AudioSource audioSource;
    private Coroutine releaseRoutine;
    private Coroutine fadeRoutine;
    private float baseVolume = 1f;      // 컨테이너/호출자가 지정한 볼륨
    private float fadeMultiplier = 1f;  // FadeOut 진행도 (1 -> 0)
    private AudioChannelType channel = AudioChannelType.None;

    public AudioSource Source { get { Init(); return audioSource; } }

    /// <summary>현재 재생 중인 소리의 채널 (채널 볼륨 적용 기준)</summary>
    public AudioChannelType Channel => channel;

    private void Awake()
    {
        Init();
        // 오디오 시스템 매니저에 현재 AudioPlayer 등록
        var manager = AudioSystemManager.Instance;
        if (manager != null && !manager.ActiveAudioPlayers.Contains(this))
            manager.ActiveAudioPlayers.Add(this);
    }

    private void OnDestroy()
    {
        if (AudioSystemManager.HasInstance)
            AudioSystemManager.Instance.ActiveAudioPlayers.Remove(this);
    }

    /// <summary>
    /// AudioSource를 초기화합니다. (필요 시 동적으로 추가)
    /// </summary>
    protected void Init()
    {
        if (isInit) return;
        isInit = true;

        audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    /// <summary>
    /// 오브젝트가 활성화될 때 자동 재생을 수행합니다.
    /// </summary>
    private void OnEnable()
    {
        if (PlayOnEnable)
        {
            Refresh();
        }
    }

    private void OnDisable()
    {
        releaseRoutine = null;
        fadeRoutine = null;
    }

    /// <summary>
    /// 현재 설정된 AudioClipContainer를 다시 재생합니다. (PlayOnEnable 인 경우만)
    /// </summary>
    public void Refresh()
    {
        if (!PlayOnEnable || audioClipcontainer == null) return;
        if (AudioSystemManager.IsMute(audioClipcontainer.ChannelType)) return;

        Play(audioClipcontainer.GetAudioClip(), loop, audioClipcontainer.Volmue, audioClipcontainer.ChannelType);
    }

    /// <summary>
    /// 지정된 오디오 리소스를 재생합니다.
    /// 최종 볼륨 = volume × 채널 볼륨(BGM/SFX) × 페이드. 마스터 볼륨은 AudioListener 에서 일괄 적용된다.
    /// </summary>
    public void Play(AudioResource audioResource, bool loop = false, float volume = 1, AudioChannelType channel = AudioChannelType.SFX)
    {
        Init();
        if (audioResource == null) return;

        StopRoutines();

        baseVolume = volume;
        fadeMultiplier = 1f;
        this.channel = channel;

        audioSource.loop = loop;
        audioSource.resource = audioResource;
        ApplyVolume();
        audioSource.Play();

        if (PlayOnEnable) return;

        if (!loop)
        {
            releaseRoutine = StartCoroutine(ReleaseWhenFinished());
        }
    }

    /// <summary>
    /// 채널 볼륨이 바뀌었을 때 등, 현재 설정으로 AudioSource 볼륨을 다시 계산합니다.
    /// </summary>
    public void ApplyVolume()
    {
        if (audioSource == null) return;
        audioSource.volume = baseVolume * AudioSystemManager.GetChannelVolumeScale(channel) * fadeMultiplier;
    }

    /// <summary>
    /// 오디오 재생을 중지합니다.
    /// </summary>
    public void Stop()
    {
        Init();
        StopRoutines();

        audioSource.Stop();
        audioSource.resource = null;
    }

    /// <summary>
    /// 재생이 끝나면 풀에 반환합니다. (언스케일드 타임 기준)
    /// </summary>
    private IEnumerator ReleaseWhenFinished()
    {
        yield return null; // Play 직후 한 프레임 대기
        while (audioSource != null && audioSource.isPlaying)
            yield return null;

        releaseRoutine = null;
        Release();
    }

    /// <summary>
    /// 지정된 시간 동안 볼륨을 줄인 후 정지합니다.
    /// 풀링된 플레이어(PlayOnEnable=false)는 정지 후 풀에 반환됩니다.
    /// </summary>
    public void FadeOut(float duration)
    {
        if (audioSource == null || !audioSource.isPlaying || !isActiveAndEnabled) return;

        if (releaseRoutine != null) { StopCoroutine(releaseRoutine); releaseRoutine = null; }
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOutRoutine(duration));
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        float startMultiplier = fadeMultiplier;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += UseScaledTime ? Time.deltaTime : Time.unscaledDeltaTime;
            fadeMultiplier = Mathf.Lerp(startMultiplier, 0f, elapsedTime / duration);
            ApplyVolume(); // 페이드 중 슬라이더를 움직여도 반영된다
            yield return null;
        }

        fadeRoutine = null;
        fadeMultiplier = 0f;
        ApplyVolume();
        Stop();
        if (!PlayOnEnable) Release();
    }

    private void StopRoutines()
    {
        if (releaseRoutine != null) { StopCoroutine(releaseRoutine); releaseRoutine = null; }
        if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; }
    }

    /// <summary>
    /// 오디오 플레이어를 풀에 반환합니다.
    /// </summary>
    private void Release()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.resource = null;
        }

        if (AudioSystemManager.HasInstance)
            AudioSystemManager.Instance.ReleasePlayer(this);
        else
            gameObject.SetActive(false);
    }

    private void Update()
    {
        if (audioSource != null && audioSource.clip != null && audioSource.clip.length > 0f)
        {
            AudioCursor = audioSource.time / audioSource.clip.length; // 현재 재생 위치를 0~1 사이 값으로 변환
        }
    }
}
