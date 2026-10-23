using UnityEngine;

public class AudioSpawner : MonoBehaviour
{
    public AudioClipContainer audioClipContainer;
    public bool PlayOnEnable;


    /// <summary>
    /// 오브젝트가 활성화될 때 자동 재생을 수행합니다.
    /// </summary>
    private void OnEnable()
    {
        if (PlayOnEnable)
        {
            SpawnAudioPlayer();
        }
    }


    private void SpawnAudioPlayer()
    {
        if (audioClipContainer == null)
            return;

        AudioSystemManager.Play(audioClipContainer);
    }
}