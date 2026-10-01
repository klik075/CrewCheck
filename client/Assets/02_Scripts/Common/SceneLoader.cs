using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 게임 전체에서 사용하는 씬 전환 매니저.
    ///
    /// 사용법
    ///   SceneLoader.Load(SceneType.Lobby);
    ///
    /// 특징
    ///   - 게임이 시작될 때 자동으로 생성되고(DontDestroyOnLoad) 씬이 바뀌어도 유지된다.
    ///     그래서 씬마다 SceneLoader 오브젝트를 배치할 필요가 없다.
    ///   - 비동기(LoadSceneAsync)로 로드한다.
    ///   - 로딩 중에 다시 Load를 호출하면 무시한다. 버튼을 연타해도 씬 전환이 한 번만 일어난다.
    ///   - LoadStarted / ProgressChanged / LoadCompleted 이벤트를 제공한다.
    ///     나중에 로딩 화면이나 페이드 연출을 붙일 때 이 이벤트를 구독하면 된다.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 씬 enum ↔ 실제 씬 파일 이름 매핑
        // 씬 이름이 바뀌면 여기만 고치면 된다.
        // (주의) 여기 적힌 씬은 모두 File > Build Settings > Scenes In Build 에 등록되어 있어야 한다.
        // ------------------------------------------------------------------
        private static readonly Dictionary<SceneType, string> SceneNames = new Dictionary<SceneType, string>
        {
            { SceneType.Title, "Title" },   // 로그인 / 회원가입 (TitleController)
            { SceneType.Lobby, "Lobby" },
            { SceneType.Room,  "Room" },      // 대기실 (RoomUIController)
            { SceneType.Game,  "GameScene" }, // 게임 진행
        };

        private static SceneLoader instance;

        /// <summary>현재 씬을 로딩 중인지 여부. 로딩 중에는 새 Load 요청이 무시된다.</summary>
        public static bool IsLoading { get; private set; }

        /// <summary>로드를 시작할 때 호출된다. (인자: 이동할 씬)</summary>
        public static event Action<SceneType> LoadStarted;

        /// <summary>로딩 진행률(0~1)이 바뀔 때마다 호출된다. 로딩 바 표시용.</summary>
        public static event Action<float> ProgressChanged;

        /// <summary>새 씬이 활성화된 뒤 호출된다. (인자: 도착한 씬)</summary>
        public static event Action<SceneType> LoadCompleted;

        // ------------------------------------------------------------------
        // 자동 생성
        // ------------------------------------------------------------------

        // 에디터에서 "Enter Play Mode Options(도메인 리로드 끔)"를 쓰면 static 값이 이전 플레이에서 남는다.
        // 플레이를 시작할 때마다 static 값을 초기화해서 그런 문제를 막는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            IsLoading = false;
            LoadStarted = null;
            ProgressChanged = null;
            LoadCompleted = null;
        }

        // 첫 씬이 로드되기 전에 SceneLoader 오브젝트를 하나 만들어 둔다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateOnStartup()
        {
            EnsureInstance();
        }

        private static SceneLoader EnsureInstance()
        {
            if (instance != null) return instance;

            var go = new GameObject("[SceneLoader]");
            instance = go.AddComponent<SceneLoader>(); // Awake에서 DontDestroyOnLoad 처리
            return instance;
        }

        private void Awake()
        {
            // 혹시 씬에 SceneLoader가 직접 배치되어 있어 두 개가 되면, 나중에 생긴 쪽을 제거한다.
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ------------------------------------------------------------------
        // 공개 API
        // ------------------------------------------------------------------

        /// <summary>
        /// 지정한 씬으로 이동한다.
        /// </summary>
        /// <returns>로드를 시작했으면 true. 이미 로딩 중이거나 씬이 Build Settings에 없으면 false.</returns>
        public static bool Load(SceneType sceneType)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[SceneLoader] 이미 씬을 로딩 중이라 {sceneType} 요청을 무시합니다.");
                return false;
            }

            string sceneName = GetSceneName(sceneType);

            // Build Settings에 등록되지 않은 씬은 LoadSceneAsync가 실패하므로 미리 확인해서 원인을 알려준다.
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneLoader] '{sceneName}' 씬을 로드할 수 없습니다. " +
                               "File > Build Settings > Scenes In Build 에 등록되어 있는지 확인하세요.");
                return false;
            }

            EnsureInstance().StartCoroutine(instance.LoadRoutine(sceneType, sceneName));
            return true;
        }

        /// <summary>enum에 연결된 실제 씬 이름을 돌려준다.</summary>
        public static string GetSceneName(SceneType sceneType)
        {
            return SceneNames.TryGetValue(sceneType, out string name) ? name : sceneType.ToString();
        }

        /// <summary>현재 활성 씬이 지정한 씬인지 확인한다.</summary>
        public static bool IsCurrent(SceneType sceneType)
        {
            return SceneManager.GetActiveScene().name == GetSceneName(sceneType);
        }

        // ------------------------------------------------------------------
        // 내부 로딩 처리
        // ------------------------------------------------------------------

        private IEnumerator LoadRoutine(SceneType sceneType, string sceneName)
        {
            IsLoading = true;
            Debug.Log($"[SceneLoader] {sceneName} 씬 로드 시작");
            LoadStarted?.Invoke(sceneType);

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                // CanStreamedLevelBeLoaded를 통과했는데도 실패하는 드문 경우
                Debug.LogError($"[SceneLoader] '{sceneName}' 씬 로드에 실패했습니다.");
                IsLoading = false;
                yield break;
            }

            // op.progress는 0~0.9까지 로딩, 0.9 이후는 활성화 단계라 0.9로 나눠서 0~1로 맞춘다.
            while (!op.isDone)
            {
                ProgressChanged?.Invoke(Mathf.Clamp01(op.progress / 0.9f));
                yield return null;
            }

            ProgressChanged?.Invoke(1f);
            IsLoading = false;
            Debug.Log($"[SceneLoader] {sceneName} 씬 로드 완료");
            LoadCompleted?.Invoke(sceneType);
        }
    }
}
