using UnityEngine;
using UnityEngine.UI;


public class AudioMuteButton : MonoBehaviour
{
    public AudioChannelType ChannelType;
    public Image activeMuteImage;
    public Image disabelMuteImage;

    // Runtime Value
    private Button button;
    private AudioMuteChannelData audioMuteChannelData;


    public void Awake()
    {
        button = GetComponent<Button>();
        if(button == null) button = gameObject.AddComponent<Button>();

        button.onClick.AddListener(DoMuteAudioChannel);

        audioMuteChannelData = AudioSystemManager.Instance.audioSaveData.GetOrAddMuteChannel(ChannelType.ToString());
        audioMuteChannelData.OnChangeMute += UpdateMuteImage;
        UpdateMuteImage();
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(DoMuteAudioChannel);
        if (audioMuteChannelData != null) audioMuteChannelData.OnChangeMute -= UpdateMuteImage;
    }




    private void DoMuteAudioChannel()
    {
        audioMuteChannelData.SetMute(!audioMuteChannelData.IsMute);
    }




    private void UpdateMuteImage()
    {
        if (activeMuteImage == null) return;
        if (disabelMuteImage == null) return;

        activeMuteImage.gameObject.SetActive(audioMuteChannelData.IsMute);
        disabelMuteImage.gameObject.SetActive(!audioMuteChannelData.IsMute);
    }
}