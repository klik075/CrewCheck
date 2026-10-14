using System.Collections.Generic;
using UnityEngine;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 윈도우 창 크기(해상도) 프리셋 관리.
    ///
    /// - 프리셋은 전부 9:16 세로 비율이다. (Canvas Scaler 기준 1080x1920과 같은 비율)
    /// - 선택값은 PlayerPrefs "display_resolution"에 프리셋 인덱스로 저장한다.
    /// - 게임 시작 시(첫 씬 로드 전) 저장값을 자동 적용한다.
    ///   Unity가 레지스트리에 기억해 둔 이전 창 크기를 이 값으로 덮어쓴다.
    /// - 모니터 작업 영역(작업 표시줄 제외)보다 큰 프리셋은 선택할 수 없다.
    /// - 에디터에서는 창 크기를 바꾸지 않는다. (저장만 됨)
    /// </summary>
    public static class ResolutionSettings
    {
        public struct Preset
        {
            public readonly int Width;
            public readonly int Height;
            public readonly string Label;

            public Preset(int width, int height, string label)
            {
                Width = width;
                Height = height;
                Label = label;
            }

            public string DisplayName => $"{Label} ({Width}x{Height})";
        }

        public static readonly Preset[] Presets =
        {
            new Preset(360, 640, "작게"),
            new Preset(450, 800, "보통"),
            new Preset(540, 960, "크게"),
            new Preset(720, 1280, "아주 크게"),
        };

        public const int DefaultIndex = 1; // 450x800
        private const string PrefsKey = "display_resolution";

        // 창 테두리, 타이틀바 높이 여유분
        private const int WindowFrameMargin = 60;

        /// <summary>현재 선택된 프리셋 인덱스 (저장값, 사용 불가하면 보정된 값).</summary>
        public static int CurrentIndex { get; private set; } = DefaultIndex;

        public static event System.Action<int> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySavedOnStartup()
        {
            int saved = PlayerPrefs.GetInt(PrefsKey, DefaultIndex);
            Apply(saved, save: false);
        }

        /// <summary>지금 모니터에서 선택할 수 있는 프리셋 인덱스 목록. 최소 1개(가장 작은 것)는 항상 포함.</summary>
        public static List<int> GetAvailableIndices()
        {
            var result = new List<int>();
            Vector2Int area = GetWorkAreaSize();

            for (int i = 0; i < Presets.Length; i++)
            {
                bool fits = Presets[i].Width <= area.x && Presets[i].Height + WindowFrameMargin <= area.y;
                if (fits || i == 0) result.Add(i);
            }
            return result;
        }

        /// <summary>프리셋을 적용한다. 사용 불가한 인덱스면 가능한 것 중 가장 가까운(작은) 것으로 보정.</summary>
        public static void Apply(int index, bool save = true)
        {
            index = ClampToAvailable(index);
            CurrentIndex = index;

            if (save)
            {
                PlayerPrefs.SetInt(PrefsKey, index);
                PlayerPrefs.Save();
            }

            Preset p = Presets[index];
#if UNITY_STANDALONE && !UNITY_EDITOR
            Screen.SetResolution(p.Width, p.Height, FullScreenMode.Windowed);
#else
            Debug.Log($"[ResolutionSettings] 에디터에서는 창 크기를 바꾸지 않습니다. 선택: {p.DisplayName}");
#endif
            Changed?.Invoke(index);
        }

        private static int ClampToAvailable(int index)
        {
            List<int> available = GetAvailableIndices();
            index = Mathf.Clamp(index, 0, Presets.Length - 1);

            for (int i = available.Count - 1; i >= 0; i--)
            {
                if (available[i] <= index) return available[i];
            }
            return available[0];
        }

        private static Vector2Int GetWorkAreaSize()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            RectInt work = Screen.mainWindowDisplayInfo.workArea;
            if (work.width > 0 && work.height > 0) return new Vector2Int(work.width, work.height);
            return new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
#else
            return new Vector2Int(int.MaxValue, int.MaxValue); // 에디터에서는 전부 표시
#endif
        }
    }
}
