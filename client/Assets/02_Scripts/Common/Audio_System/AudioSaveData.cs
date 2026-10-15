using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[Serializable]
public class AudioSaveData
{
    public List<AudioMuteChannelData> Mutes = new();

    // 볼륨 (슬라이더 값 0~1. 실제 적용은 AudioSystemManager.VolumeCurve 로 제곱해서 쓴다)
    public float MasterVolume = AudioSystemManager.DefaultVolume;
    public bool MasterMute = false;
    public List<AudioVolumeChannelData> Volumes = new();


    // Mute 체크
    public bool IsMute(string channelID)
        => GetOrAddMuteChannel(channelID).IsMute;


    public AudioMuteChannelData GetMuteChannel(string channelID)
        => Mutes.FirstOrDefault(e => e.ChannelID == channelID);
    public AudioMuteChannelData AddMuteChannel(string channelID)
    {
        var data = new AudioMuteChannelData() { ChannelID = channelID, IsMute = false };
        Mutes.Add(data);
        return data;
    }

    public AudioMuteChannelData GetOrAddMuteChannel(string channelID)
        => GetMuteChannel(channelID) ?? AddMuteChannel(channelID);


    // Volume
    public AudioVolumeChannelData GetOrAddVolumeChannel(string channelID)
    {
        var data = Volumes.FirstOrDefault(e => e.ChannelID == channelID);
        if (data != null) return data;
        data = new AudioVolumeChannelData() { ChannelID = channelID, Volume = AudioSystemManager.DefaultVolume };
        Volumes.Add(data);
        return data;
    }
}

[Serializable]
public class AudioVolumeChannelData
{
    public string ChannelID;
    [Range(0, 1)] public float Volume = AudioSystemManager.DefaultVolume;
}

[Serializable]
public class AudioMuteChannelData
{
    [NonSerialized] public Action OnChangeMute;

    public string ChannelID;
    public bool IsMute; 

    public void SetMute(bool active)
    {
        if (IsMute == active) return;
        IsMute = active;
        OnChangeMute?.Invoke();
        AudioSystemManager.SetDirty();
        if (AudioSystemManager.HasInstance)
            AudioSystemManager.Instance.OnChangeMuteChannels?.Invoke(ChannelID);
        AudioSystemManager.NotifyMuteChanged();
    }
}