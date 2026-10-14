using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[Serializable]
public class AudioSaveData
{
    public List<AudioMuteChannelData> Mutes = new();


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
            AudioSystemManager.Instance.OnChangeMuteChannels?.Invoke();
    }
}