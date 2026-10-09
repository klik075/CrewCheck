using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WhoisntCitizen.Chat;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.EditorTools
{
    // Tools > Game Scene > 게임 테스트 씬 만들기 (GameTest)
    // GameScene을 GameTest 씬으로 복사하고, 서버 없이 테스트하도록 바꾼다.
    //   GameFlow   GameController: 가짜 서버 켬(useFakeServer, fakeBotsAct, fakeSeed = -1) / WaitingRoomController: 꺼 둠(자동 시작·씬 이동 안 함)
    //   GameChat   꺼 둠 (서버 채팅 폴링 안 함 → 안내는 GameScreen이 직접 남긴다)
    //   GameCanvas/GameTestPanel (새로) [게임 시작] 버튼 + 상태 표시 + GameTestRunner(한 바퀴 자동 진행)
    // 다시 실행하면 GameTest를 GameScene에서 새로 복사한다(GameTest에서 손으로 고친 것은 사라진다).
    public static class GameTestSceneSetupTool
    {
        private const string GameScenePath = "Assets/01_Scenes/GameScene.unity";
        private const string TestScenePath = "Assets/99_Test/GameTest/GameTest.unity"; // [99_Test] 테스트 폴더로 이동
        private const string FontPath = "Assets/Fonts/MalgunGothic SDF.asset";

        private static readonly Color AccentColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        private static readonly Color DarkText = new Color32(0x1B, 0x1E, 0x26, 0xFF);
        private static readonly Color StatusBg = new Color32(0x1B, 0x1E, 0x26, 0xC8);

        [MenuItem("Tools/Game Scene/게임 테스트 씬 만들기 (GameTest)")]
        public static void CreateFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null
                && !EditorUtility.DisplayDialog("게임 테스트 씬", TestScenePath + "이 이미 있습니다.\nGameScene에서 다시 복사할까요?", "다시 만들기", "취소"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            string error;
            string summary = Create(out error);
            EditorUtility.DisplayDialog("게임 테스트 씬", error ?? summary, "확인");
        }

        /// <summary>GameTest 씬을 만들고 연다. 실패하면 error에 이유를 넣고 null을 돌려준다.</summary>
        public static string Create(out string error)
        {
            error = null;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null)
            {
                error = GameScenePath + "이 없습니다.";
                return null;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null)
            {
                AssetDatabase.DeleteAsset(TestScenePath);
            }
            if (!AssetDatabase.CopyAsset(GameScenePath, TestScenePath))
            {
                error = "씬을 복사하지 못했습니다: " + TestScenePath;
                return null;
            }
            Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            var log = new StringBuilder("· " + GameScenePath + " → " + TestScenePath + " 복사\n");

            GameController game = Object.FindFirstObjectByType<GameController>(FindObjectsInactive.Include);
            GameScreen screen = Object.FindFirstObjectByType<GameScreen>(FindObjectsInactive.Include);
            if (game == null || screen == null)
            {
                error = "GameScene에 GameController·GameScreen이 없습니다. 먼저 Tools > Game Scene > 게임 화면 구성 (G)을 실행하세요.";
                return null;
            }

            SetBool(game, "useFakeServer", true);
            SetBool(game, "fakeBotsAct", true);
            var seed = new SerializedObject(game);
            seed.FindProperty("fakeSeed").intValue = -1; // 판마다 봇의 선택이 달라 매번 다른 흐름을 볼 수 있다
            seed.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("· GameController: 가짜 서버 켬 (봇 행동 켬, 판마다 다른 난수)");

            WaitingRoomController waiting = game.GetComponent<WaitingRoomController>();
            if (waiting != null)
            {
                waiting.enabled = false; // Start()가 돌지 않아 자동 시작·Room 씬 이동을 하지 않는다
                log.AppendLine("· WaitingRoomController 끔 (자동 시작·씬 이동 없음)");
            }

            GameChatController chat = Object.FindFirstObjectByType<GameChatController>(FindObjectsInactive.Include);
            if (chat != null)
            {
                chat.gameObject.SetActive(false);
                log.AppendLine("· " + chat.gameObject.name + " 끔 (서버 채팅 폴링 없음)");
            }

            GameTestRunner runner = BuildPanel(screen.transform, game);
            log.AppendLine("· GameTestPanel 추가 ([게임 시작] 버튼 + GameTestRunner)");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = runner.gameObject;
            log.AppendLine("· 저장 완료. Play → [게임 시작]");
            return log.ToString();
        }

        private static GameTestRunner BuildPanel(Transform canvas, GameController game)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            int layer = canvas.gameObject.layer;

            // 화면 전체를 덮지만 클릭은 막지 않는 루트 (버튼만 클릭을 받는다)
            RectTransform panel = NewRect("GameTestPanel", canvas, layer);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            panel.SetAsLastSibling(); // 다른 UI 위에 그린다

            // 상태 표시: 상단 바 바로 아래 띠
            RectTransform statusBg = NewRect("StatusBar", panel, layer);
            statusBg.anchorMin = new Vector2(0f, 1f);
            statusBg.anchorMax = new Vector2(1f, 1f);
            statusBg.pivot = new Vector2(0.5f, 1f);
            statusBg.anchoredPosition = new Vector2(0f, -130f);
            statusBg.sizeDelta = new Vector2(-40f, 70f);
            Image bg = statusBg.gameObject.AddComponent<Image>();
            bg.color = StatusBg;
            bg.raycastTarget = false;
            TextMeshProUGUI status = NewText("StatusText", statusBg, "[게임 시작]을 누르세요.", 30f, Color.white, font, layer);
            Stretch((RectTransform)status.transform, 16f);

            // 시작 버튼: 화면 가운데
            RectTransform buttonRt = NewRect("StartButton", panel, layer);
            buttonRt.anchorMin = buttonRt.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRt.pivot = new Vector2(0.5f, 0.5f);
            buttonRt.sizeDelta = new Vector2(520f, 150f);
            Image buttonImage = buttonRt.gameObject.AddComponent<Image>();
            buttonImage.color = AccentColor;
            Button button = buttonRt.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            TextMeshProUGUI label = NewText("Text", buttonRt, "게임 시작", 56f, DarkText, font, layer);
            label.fontStyle = FontStyles.Bold;
            Stretch((RectTransform)label.transform, 0f);

            GameTestRunner runner = panel.gameObject.AddComponent<GameTestRunner>();
            var so = new SerializedObject(runner);
            so.FindProperty("controller").objectReferenceValue = game;
            so.FindProperty("startButton").objectReferenceValue = button;
            so.FindProperty("startButtonText").objectReferenceValue = label;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();
            return runner;
        }

        private static RectTransform NewRect(string name, Transform parent, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TMP_FontAsset font, int layer)
        {
            RectTransform rt = NewRect(name, parent, layer);
            TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, 0f);
            rt.offsetMax = new Vector2(-padding, 0f);
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError("[GameTestSceneSetup] 필드 없음: " + target.GetType().Name + "." + field);
                return;
            }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
