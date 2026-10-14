using System.Collections.Generic;
using UnityEngine;

public enum AudioChannelType
{
    None = 0,
    SFX,
    BGM,
}

[CreateAssetMenu(menuName = "System/Audio/ClipContainer")]
public class AudioClipContainer : ScriptableObject
{
    [Tooltip("뮤트 채널. None 이면 어떤 뮤트 토글에도 영향을 받지 않는다.")]
    public AudioChannelType ChannelType = AudioChannelType.SFX;
    public AudioClip AudioClip;
    public List<AudioClip> AuidoClips = new();

    [Header("Data")]
    public bool UseRandomPlay = false;
    [Range(0, 1)] public float Volmue = 0.8f;


    public AudioClip GetAudioClip()
    {
        if (!UseRandomPlay || AuidoClips == null || AuidoClips.Count == 0) return AudioClip;
        return AuidoClips[Random.Range(0, AuidoClips.Count)];
    }

    public void Play()
    {
        AudioSystemManager.Play(this);
    }
}