using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.TestScenes.LobbyTest
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/LobbyTest 폴더째 삭제 예정]
    ///
    /// LobbyTest 씬에서 방 입장/생성에 성공하면 메인 코드(LobbyUIController, CreateRoomPopup)는
    /// SceneLoader로 진짜 Room 씬을 불러온다. 이 라우터가 그 순간을 가로채 RoomTest 씬으로 바꿔 준다.
    ///
    ///  1) 진짜 Room 씬이 로드되면(sceneLoaded) Start가 돌기 전에 루트 오브젝트를 모두 끈다.
    ///     → RoomUIController / 채팅이 진짜 서버에 요청을 보내지 않는다.
    ///  2) 곧바로 RoomTest 씬을 불러온다. RoomTest 씬이 없으면(삭제됨) LobbyTest로 돌려보낸다.
    ///
    /// "직전에 로드된 씬이 LobbyTest일 때"만 동작하므로, 메인 씬끼리 오가는 평소 흐름에는 아무 영향이 없다.
    /// 에디터에서만 동작한다.
    /// </summary>
    internal static class LobbyTestSceneRouter
    {
        public const string LobbyTestScenePath = "Assets/99_Test/LobbyTest/LobbyTest.unity";
        public const string RoomTestScenePath = "Assets/99_Test/RoomTest/RoomTest.unity";

        private static bool armed; // 직전에 로드된 씬이 LobbyTest였는지

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            armed = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 테스트 씬의 Bootstrap.Awake에서 부른다. (에디터에서 플레이를 시작한 첫 씬은 sceneLoaded가 오지 않을 수 있어서)
        /// </summary>
        public static void Arm()
        {
            armed = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!Application.isEditor) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            if (scene.path == LobbyTestScenePath)
            {
                armed = true;
                return;
            }

            bool fromLobbyTest = armed;
            armed = false;
            if (!fromLobbyTest || scene.name != SceneLoader.GetSceneName(SceneType.Room)) return;

            // Awake/OnEnable까지만 돈 상태. 꺼 두면 Start가 불리지 않아 진짜 서버로 요청이 나가지 않는다.
            foreach (GameObject root in scene.GetRootGameObjects()) root.SetActive(false);

            if (File.Exists(RoomTestScenePath))
            {
                Debug.Log("[LobbyTest] 진짜 Room 씬 대신 RoomTest 씬으로 이동합니다.");
                Load(RoomTestScenePath);
            }
            else
            {
                Debug.LogWarning("[LobbyTest] RoomTest 씬이 없어 LobbyTest로 돌아갑니다. (입장 자체는 성공)");
                RoomSession.SetLobbyNotice("[테스트] 입장 성공! RoomTest 씬이 없어 로비로 돌아왔습니다.");
                Load(LobbyTestScenePath); // 넘겨주려던 방 정보는 LobbyTestBootstrap이 '나가기'로 처리한다
            }
        }

        private static void Load(string path)
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
        }
    }
}
