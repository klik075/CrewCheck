using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.RoomTest
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/RoomTest 폴더째 삭제 예정]
    ///
    /// RoomTest 씬에서 [나가기] 등으로 로비로 가면 메인 코드(RoomUIController)는 SceneLoader로 진짜 Lobby 씬을 불러온다.
    /// LobbyTest 씬이 있으면 이 라우터가 그 순간을 가로채 LobbyTest 씬으로 바꿔 준다.
    ///
    ///  1) 진짜 Lobby 씬이 로드되면(sceneLoaded) Start가 돌기 전에 루트 오브젝트를 모두 끈다. (진짜 서버로 요청이 나가지 않음)
    ///  2) 곧바로 LobbyTest 씬을 불러온다.
    ///  LobbyTest 씬이 없으면(삭제됨) 아무것도 하지 않는다. (원래처럼 진짜 Lobby → 로그인 정보가 없어 Title로 이동)
    ///
    /// "직전에 로드된 씬이 RoomTest일 때"만 동작하므로, 메인 씬끼리 오가는 평소 흐름에는 아무 영향이 없다.
    /// LobbyTest 폴더의 타입은 참조하지 않는다. (씬 경로 문자열로만 연결)
    /// </summary>
    internal static class RoomTestSceneRouter
    {
        public const string RoomTestScenePath = "Assets/99_Test/RoomTest/RoomTest.unity";
        public const string LobbyTestScenePath = "Assets/99_Test/LobbyTest/LobbyTest.unity";

        private static bool armed; // 직전에 로드된 씬이 RoomTest였는지

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

            if (scene.path == RoomTestScenePath)
            {
                armed = true;
                return;
            }

            bool fromRoomTest = armed;
            armed = false;
            if (!fromRoomTest || scene.name != SceneLoader.GetSceneName(SceneType.Lobby)) return;
            if (!File.Exists(LobbyTestScenePath)) return;

            // Awake/OnEnable까지만 돈 상태. 꺼 두면 Start가 불리지 않아 진짜 서버로 요청이 나가지 않는다.
            foreach (GameObject root in scene.GetRootGameObjects()) root.SetActive(false);
            Debug.Log("[RoomTest] 진짜 Lobby 씬 대신 LobbyTest 씬으로 이동합니다.");
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(LobbyTestScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
        }
    }

    // ======================================================================
    // 테스트 씬끼리 방 정보를 주고받는 형식 (JSON 문자열로만 주고받는다)
    // LobbyTest 폴더에도 같은 모양의 클래스가 따로 있다. 서로의 타입을 참조하지 않으므로
    // 둘 중 한 폴더만 지워도 남은 쪽은 문제없이 컴파일·동작한다.
    // ======================================================================
    [Serializable]
    public class TestRoomHandoff
    {
        public const string EnterRoomKey = "WhoisntCitizen.TestHandoff.EnterRoom"; // LobbyTest → RoomTest
        public const string LeaveRoomKey = "WhoisntCitizen.TestHandoff.LeaveRoom"; // RoomTest → LobbyTest

        public long myUserId;
        public string myNickname;
        public RoomDetailResponse room;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AppDomain.CurrentDomain.SetData(EnterRoomKey, null);
            AppDomain.CurrentDomain.SetData(LeaveRoomKey, null);
        }

        public static void Put(string key, TestRoomHandoff value)
        {
            AppDomain.CurrentDomain.SetData(key, value == null ? null : JsonUtility.ToJson(value));
        }

        /// <summary>꺼내면서 지운다. 없으면 null</summary>
        public static TestRoomHandoff Take(string key)
        {
            string json = AppDomain.CurrentDomain.GetData(key) as string;
            AppDomain.CurrentDomain.SetData(key, null);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<TestRoomHandoff>(json); }
            catch (Exception) { return null; }
        }
    }
}
