using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhoisntCitizen.TestScenes.LobbyTest.EditorTools
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/LobbyTest 폴더째 삭제 예정]
    /// 메뉴: Tools/LobbyTest/...
    ///  - "Lobby 씬으로 LobbyTest 씬 다시 만들기": Lobby.unity를 복사해 LobbyTest.unity를 만들고 LobbyTestBootstrap 오브젝트를 넣는다.
    ///    Lobby 씬을 고친 뒤 테스트 씬에도 반영하고 싶을 때 다시 실행한다. (기존 LobbyTest 씬과 Inspector 가짜 데이터는 덮어씀)
    ///  - "LobbyTest 씬 열기"
    /// Lobby 씬 자체와 Build Settings는 건드리지 않는다.
    /// </summary>
    public static class LobbyTestSceneBuilder
    {
        private const string SourceScenePath = "Assets/01_Scenes/Lobby.unity";
        private const string TestScenePath = "Assets/99_Test/LobbyTest/LobbyTest.unity";
        private const string BootstrapName = "[LobbyTest] FakeServer";

        [MenuItem("Tools/LobbyTest/Lobby 씬으로 LobbyTest 씬 다시 만들기")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LobbyTest] 플레이 중에는 만들 수 없습니다.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null && !AssetDatabase.DeleteAsset(TestScenePath))
            {
                Debug.LogError("[LobbyTest] 기존 LobbyTest 씬을 지우지 못했습니다.");
                return;
            }
            if (!AssetDatabase.CopyAsset(SourceScenePath, TestScenePath))
            {
                Debug.LogError($"[LobbyTest] {SourceScenePath}를 복사하지 못했습니다.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            var go = new GameObject(BootstrapName);
            go.AddComponent<LobbyTestBootstrap>();
            go.transform.SetAsFirstSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = go;
            Debug.Log($"[LobbyTest] {TestScenePath} 생성 완료. 가짜 데이터는 '{BootstrapName}' 오브젝트의 Inspector에서 고칩니다.");
        }

        [MenuItem("Tools/LobbyTest/LobbyTest 씬 열기")]
        public static void Open()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                Debug.LogWarning("[LobbyTest] LobbyTest 씬이 없습니다. 'Lobby 씬으로 LobbyTest 씬 다시 만들기'를 먼저 실행하세요.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }
    }
}
