using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class AudioTag
{
    public static readonly string BGM = nameof(AudioChannelType.BGM);
    public static readonly string SFX = nameof(AudioChannelType.SFX);
}

/// <summary>
/// 오디오 시스템 매니저 (싱글턴).
///
/// 사용법
///   AudioSystemManager.Play("SFX_FAIL");          // 컨테이너 이름으로 재생
///   AudioSystemManager.Play(audioClipContainer);  // 컨테이너로 재생
///   AudioSystemManager.SetMute(AudioChannelType.BGM, true);
///
/// 특징
///   - AudioSystemManager 프리팹을 첫 씬에 배치해 두면 그 인스턴스를 사용한다.
///     (IsDontDestroy = true 면 씬이 바뀌어도 유지)
///   - 씬에 없으면 Resources/AudioSystemManager 프리팹을 찾아 생성하고, 그것도 없으면 빈 매니저를 만든다(경고 출력).
///   - 뮤트 설정은 PlayerPrefs(JSON)에 저장된다.
///   - 효과음 재생용 AudioPlayer는 내부 오브젝트 풀로 재사용한다.
/// </summary>
public class AudioSystemManager : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Singleton
    // ------------------------------------------------------------------
    private const string ResourcesPrefabPath = "AudioSystemManager";
    private static AudioSystemManager instance;
    private static bool isQuitting;

    public static bool HasInstance => instance != null;

    public static AudioSystemManager Instance
    {
        get
        {
            if (instance != null) return instance;
            if (isQuitting || !Application.isPlaying) return null;

            // 1) 씬에 배치된 매니저
            instance = FindFirstObjectByType<AudioSystemManager>(FindObjectsInactive.Include);

            // 2) Resources 프리팹
            if (instance == null)
            {
                var prefab = Resources.Load<AudioSystemManager>(ResourcesPrefabPath);
                if (prefab != null)
                {
                    instance = Instantiate(prefab);
                    instance.name = prefab.name;
                    instance.IsDontDestroy = true;
                }
            }

            // 3) 빈 매니저 (AudioSystemSetting 이 없으므로 재생 불가 - 경고)
            if (instance == null)
            {
                Debug.LogWarning("[AudioSystemManager] 씬/Resources 에서 매니저를 찾지 못해 빈 매니저를 생성합니다. AudioSystemSetting 이 없어 재생되지 않습니다.");
                instance = new GameObject(nameof(AudioSystemManager)).AddComponent<AudioSystemManager>();
                instance.IsDontDestroy = true;
            }

            instance.Init();
            return instance;
        }
    }

    // 도메인 리로드를 끈 경우 static 값이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isQuitting = false;
        IsDirty = false;
    }

    [Header("Singleton")]
    public bool IsDontDestroy = true;

    // ------------------------------------------------------------------
    // Settings
    // ------------------------------------------------------------------
    [Header("Audio")]
    public AudioSystemSetting AudioSystemSetting;
    public string AudioResourcePath = "Audio";

    [Space]
    public List<AudioClipContainer> AuniClipContainers = new();
    private AudioClipContainer GetAudioClipContainer(string name)
        => AuniClipContainers.FirstOrDefault(e => e != null && e.name == name);

    public GameObject BgmObject;

    [Header("UI Default Sound")]
    [Tooltip("UIButtonBase 에서 useOverrideAudio 를 쓰지 않을 때 재생되는 기본 버튼 클릭음")]
    public AudioClipContainer DefaultButtonClickAudio;

    // Event
    public Action OnChangeMuteChannels;

    // Runtime Value
    public AudioSaveData audioSaveData;
    public List<AudioPlayer> ActiveAudioPlayers = new();
    private Transform audioPlayerContainer;
    private readonly Queue<AudioPlayer> audioPlayerPool = new();
    private bool isInit;

    private const string SaveKey = "audio_save_data";

    public static bool IsDirty;
    public static void SetDirty(bool active = true) => IsDirty = active;
    public static bool IsMute(string channelID) => Instance != null && Instance.audioSaveData.IsMute(channelID);
    public static bool IsMute(AudioChannelType channel) => IsMute(channel.ToString());

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Init();
    }

    private void Init()
    {
        if (isInit) return;
        isInit = true;

        if (IsDontDestroy)
        {
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        audioSaveData = LoadSaveData();
        SetDirty();

        // 풀 컨테이너는 매니저 자식으로 둔다 (씬 전환 시 함께 유지)
        audioPlayerContainer = new GameObject("AudioPlayer Container").transform;
        audioPlayerContainer.SetParent(transform, false);

        OnChangeMuteChannels += RefreshAudioChannel;

        if (BgmObject != null) BgmObject.SetActive(!audioSaveData.IsMute(AudioTag.BGM));
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
        if (IsDirty) SaveSaveData();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            if (IsDirty) SaveSaveData();
            instance = null;
        }
    }

    private void Update()
    {
        if (IsDirty) SaveSaveData();
    }

    // ------------------------------------------------------------------
    // Save / Load (PlayerPrefs + JSON)
    // ------------------------------------------------------------------
    private static AudioSaveData LoadSaveData()
    {
        string json = PlayerPrefs.GetString(SaveKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new AudioSaveData();

        try
        {
            return JsonUtility.FromJson<AudioSaveData>(json) ?? new AudioSaveData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AudioSystemManager] 저장 데이터 로드 실패, 초기화합니다. {e.Message}");
            return new AudioSaveData();
        }
    }

    private void SaveSaveData()
    {
        IsDirty = false;
        if (audioSaveData == null) return;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(audioSaveData));
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Mute
    // ------------------------------------------------------------------
    public static void SetMute(string channelID, bool muteActive)
    {
        if (Instance == null) return;
        Instance.audioSaveData.GetOrAddMuteChannel(channelID).SetMute(muteActive);
    }
    public static void SetMute(AudioChannelType channel, bool muteActive) => SetMute(channel.ToString(), muteActive);

    public void RefreshAudioChannel()
    {
        // 파괴된 플레이어 정리 후 재생 상태 갱신
        ActiveAudioPlayers.RemoveAll(e => e == null);
        foreach (var player in ActiveAudioPlayers.ToArray())
        {
            if (!player.PlayOnEnable) continue; // 풀링된 단발성 효과음은 건드리지 않는다
            player.Stop();
            if (player.isActiveAndEnabled) player.Refresh();
        }

        if (BgmObject != null) BgmObject.SetActive(!audioSaveData.IsMute(AudioTag.BGM));
    }

    // ------------------------------------------------------------------
    // Pool
    // ------------------------------------------------------------------
    private AudioPlayer GetPooledPlayer()
    {
        if (AudioSystemSetting == null || AudioSystemSetting.AudioSourcePlayObject == null)
        {
            Debug.LogWarning("[AudioSystemManager] AudioSystemSetting.AudioSourcePlayObject is not assigned.");
            return null;
        }

        while (audioPlayerPool.Count > 0)
        {
            var pooled = audioPlayerPool.Dequeue();
            if (pooled == null) continue;
            pooled.gameObject.SetActive(true);
            return pooled;
        }

        var go = Instantiate(AudioSystemSetting.AudioSourcePlayObject, audioPlayerContainer);
        go.name = AudioSystemSetting.AudioSourcePlayObject.name;
        var player = go.GetComponent<AudioPlayer>();
        if (player == null) player = go.AddComponent<AudioPlayer>();
        player.PlayOnEnable = false;
        go.SetActive(true);
        return player;
    }

    public void ReleasePlayer(AudioPlayer player)
    {
        if (player == null) return;
        player.gameObject.SetActive(false);
        player.transform.SetParent(audioPlayerContainer, false);
        if (!audioPlayerPool.Contains(player)) audioPlayerPool.Enqueue(player);
    }

    // ------------------------------------------------------------------
    // Play
    // ------------------------------------------------------------------
    #region Audio Play Methods
    public static AudioPlayer Play(string audioID)
    {
        if (Instance == null) return null;
        var container = Instance.GetAudioClipContainer(audioID);
        if (container == null)
        {
            Debug.LogWarning($"[AudioSystemManager] AudioClipContainer '{audioID}' 를 찾을 수 없습니다.");
            return null;
        }
        return Play(container);
    }

    public static AudioPlayer Play(AudioClipContainer clipContainer)
    {
        if (clipContainer == null) return null;
        if (IsMute(clipContainer.ChannelType)) return null;

        return Play(clipContainer.GetAudioClip(), clipContainer.Volmue);
    }

    /// <summary>기본 버튼 클릭 효과음을 재생합니다. (DefaultButtonClickAudio 미설정 시 무시)</summary>
    public static AudioPlayer PlayButtonClick()
    {
        if (Instance == null || Instance.DefaultButtonClickAudio == null) return null;
        return Play(Instance.DefaultButtonClickAudio);
    }

    public static AudioPlayer Play(AudioResource resource, float volume = 1)
    {
        if (resource == null || Instance == null) return null;

        var audioPlayer = Instance.GetPooledPlayer();
        if (audioPlayer == null) return null;

        audioPlayer.transform.localPosition = Vector3.zero;
        audioPlayer.Play(resource, volume: volume);
        return audioPlayer;
    }
    #endregion

    // ------------------------------------------------------------------
    // BGM (코드에서 곡을 바꿔 가며 트는 전용 플레이어. 한 번에 한 곡)
    // ------------------------------------------------------------------
    #region BGM
    private AudioPlayer bgmPlayer;
    private AudioClipContainer currentBgm;

    /// <summary>지금 PlayBGM 으로 틀고 있는 컨테이너 (없으면 null).</summary>
    public static AudioClipContainer CurrentBGM => HasInstance ? instance.currentBgm : null;

    /// <summary>
    /// BGM 을 바꾼다. 이전 곡은 바로 멈춘다. 같은 곡(loop)이 이미 재생 중이면 처음부터 다시 틀지 않는다.
    /// - loop = true : 반복 재생. 뮤트를 풀면 다시 재생된다.
    /// - loop = false: 한 번만 재생 (승리/패배 등). 뮤트를 바꾸면 멈추고 다시 틀지 않는다.
    /// 뮤트는 컨테이너의 ChannelType 기준.
    /// </summary>
    public static AudioPlayer PlayBGM(string audioID, bool loop = true)
    {
        if (Instance == null) return null;
        var container = Instance.GetAudioClipContainer(audioID);
        if (container == null)
        {
            Debug.LogWarning($"[AudioSystemManager] AudioClipContainer '{audioID}' 를 찾을 수 없습니다.");
            return null;
        }
        return PlayBGM(container, loop);
    }

    public static AudioPlayer PlayBGM(AudioClipContainer container, bool loop = true)
    {
        if (container == null || Instance == null) return null;
        var player = Instance.GetBgmPlayer();

        if (loop && Instance.currentBgm == container && player.loop && player.Source.isPlaying)
            return player; // 같은 곡이 이미 흐르는 중

        Instance.currentBgm = container;
        player.Stop();
        player.loop = loop;
        // 반복 곡만 컨테이너를 기억시켜 뮤트 해제 시 Refresh 로 다시 재생되게 한다
        player.audioClipcontainer = loop ? container : null;

        if (IsMute(container.ChannelType)) return player;
        player.Play(container.GetAudioClip(), loop, container.Volmue);
        return player;
    }

    /// <summary>PlayBGM 으로 튼 곡을 멈춘다. fadeOut > 0 이면 서서히 줄인다.</summary>
    public static void StopBGM(float fadeOut = 0f)
    {
        if (!HasInstance || instance.bgmPlayer == null) return;
        var player = instance.bgmPlayer;
        instance.currentBgm = null;
        player.audioClipcontainer = null;
        if (fadeOut > 0f && player.Source.isPlaying) player.FadeOut(fadeOut);
        else player.Stop();
    }

    private AudioPlayer GetBgmPlayer()
    {
        if (bgmPlayer != null) return bgmPlayer;
        var go = new GameObject("BGM Player (Code)");
        go.transform.SetParent(transform, false);
        bgmPlayer = go.AddComponent<AudioPlayer>();
        bgmPlayer.PlayOnEnable = true; // 풀에 반환되지 않고, 뮤트 변경 시 Refresh 대상이 된다
        return bgmPlayer;
    }
    #endregion

#if UNITY_EDITOR
    [ContextMenu("Setup AudioClip Containers")]
    private void _Editor_SetupAudioClipContainers()
    {
        AuniClipContainers.Clear();

        foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(AudioClipContainer)))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var container = AssetDatabase.LoadAssetAtPath<AudioClipContainer>(assetPath);
            if (container != null) AuniClipContainers.Add(container);
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }
#endif
}
