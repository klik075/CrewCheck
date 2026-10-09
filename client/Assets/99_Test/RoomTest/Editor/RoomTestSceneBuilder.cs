using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhoisntCitizen.RoomTest;

namespace WhoisntCitizen.RoomTest.EditorTools
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/RoomTest 폴더째 삭제 예정]
    /// 메뉴: Tools/RoomTest/...
    ///  - "Room 씬으로 RoomTest 씬 다시 만들기": Room.unity를 복사해 RoomTest.unity를 만들고 RoomTestBootstrap 오브젝트를 넣는다.
    ///    Room 씬을 고친 뒤 테스트 씬에도 반영하고 싶을 때 다시 실행한다. (기존 RoomTest 씬과 Inspector 가짜 데이터는 덮어씀)
    ///  - "RoomTest 씬 열기"
    /// Room 씬 자체와 Build Settings는 건드리지 않는다.
    /// </summary>
    public static class RoomTestSceneBuilder
    {
        private const string SourceScenePath = "Assets/01_Scenes/Room.unity";
        private const string TestScenePath = "Assets/99_Test/RoomTest/RoomTest.unity";
        private const string BootstrapName = "[RoomTest] FakeServer";

        [MenuItem("Tools/RoomTest/Room 씬으로 RoomTest 씬 다시 만들기")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[RoomTest] 플레이 중에는 만들 수 없습니다.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null && !AssetDatabase.DeleteAsset(TestScenePath))
            {
                Debug.LogError("[RoomTest] 기존 RoomTest 씬을 지우지 못했습니다.");
                return;
            }
            if (!AssetDatabase.CopyAsset(SourceScenePath, TestScenePath))
            {
                Debug.LogError($"[RoomTest] {SourceScenePath}를 복사하지 못했습니다.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            var go = new GameObject(BootstrapName);
            go.AddComponent<RoomTestBootstrap>();
            go.transform.SetAsFirstSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = go;
            Debug.Log($"[RoomTest] {TestScenePath} 생성 완료. 가짜 데이터는 '{BootstrapName}' 오브젝트의 Inspector에서 고칩니다.");
        }

        [MenuItem("Tools/RoomTest/RoomTest 씬 열기")]
        public static void Open()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                Debug.LogWarning("[RoomTest] RoomTest 씬이 없습니다. 'Room 씬으로 RoomTest 씬 다시 만들기'를 먼저 실행하세요.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }
    }
}
