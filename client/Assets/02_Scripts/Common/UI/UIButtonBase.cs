using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIButtonBase : MonoBehaviour
{
    // public GameObject customTargetObject; // 버튼 클릭 시 애니메이션 이벤트 플레이 대상 객체를 직접 커스텀 할 수 있도록 옵션 추가

    public bool useOverrideAudio;
    public AudioClipContainer overrideOnClickAudioClip;

    // Runtime Value
    protected Button button;


    protected virtual void Awake()
    {
        button = gameObject.GetComponent<Button>();
        if(button == null) button = gameObject.AddComponent<Button>();

        button.onClick.RemoveListener(PlayOnClickAudio);
        button.onClick.AddListener(PlayOnClickAudio);
    }

    private void PlayOnClickAudio()
    {
        if (useOverrideAudio) AudioSystemManager.Play(overrideOnClickAudioClip);
        else AudioSystemManager.PlayButtonClick(); // AudioSystemManager.DefaultButtonClickAudio
    }
}