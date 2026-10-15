using UnityEngine;

/// <summary>
/// 씬에 배치해서 "이 씬에서는 이 BGM" 을 매니저에 요청하는 컴포넌트.
/// 직접 소리를 내지 않는다. 실제 재생은 AudioSystemManager 의 BGM 플레이어(DontDestroyOnLoad)가 한다.
///
/// - 이전 씬과 같은 곡(반복)이면 PlayBGM 이 아무것도 하지 않으므로 끊기지 않고 이어진다.
/// - 다른 곡이면 그 곡으로 바뀐다.
/// - bgm 이 비어 있고 stopIfEmpty 가 켜져 있으면 BGM 을 끈다.
/// - 이 오브젝트가 파괴돼도 BGM 은 멈추지 않는다 (다음 씬에서 이어지도록).
/// </summary>
public class SceneBGM : MonoBehaviour
{
    [Tooltip("이 씬에서 틀 BGM (ChannelType = BGM 권장)")]
    public AudioClipContainer bgm;

    [Tooltip("반복 재생 여부")]
    public bool loop = true;

    [Tooltip("bgm 이 비어 있으면 지금 흐르는 BGM 을 끈다")]
    public bool stopIfEmpty = true;

    [Tooltip("BGM 을 끌 때 페이드아웃 시간(초). 0 이면 즉시")]
    [Min(0f)] public float fadeOut = 0.5f;

    private void Start()
    {
        Apply();
    }

    /// <summary>이 컴포넌트의 설정대로 BGM 을 다시 적용한다.</summary>
    public void Apply()
    {
        if (bgm != null)
        {
            AudioSystemManager.PlayBGM(bgm, loop);
        }
        else if (stopIfEmpty)
        {
            AudioSystemManager.StopBGM(fadeOut);
        }
    }
}
