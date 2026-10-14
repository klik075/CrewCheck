using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 설정 팝업의 "화면 크기" 드롭다운. TMP_Dropdown 오브젝트에 붙인다.
    ///
    /// - 활성화될 때마다 지금 모니터에서 가능한 프리셋으로 목록을 다시 채운다.
    /// - 항목을 고르면 바로 적용하고 저장한다. (ResolutionSettings)
    /// - 드롭다운의 기존 Options는 실행 시 덮어쓰므로 에디터에서 비워 둬도 된다.
    /// </summary>
    [RequireComponent(typeof(TMP_Dropdown))]
    [DisallowMultipleComponent]
    public class ResolutionOptionView : MonoBehaviour
    {
        private TMP_Dropdown dropdown;
        private readonly List<int> presetIndices = new List<int>(); // 드롭다운 항목 순서 → 프리셋 인덱스

        private void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
            dropdown.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnEnable()
        {
            Refresh();
            ResolutionSettings.Changed += OnSettingsChanged;
        }

        private void OnDisable()
        {
            ResolutionSettings.Changed -= OnSettingsChanged;
        }

        private void OnDestroy()
        {
            if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnValueChanged);
        }

        private void Refresh()
        {
            if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();

            presetIndices.Clear();
            presetIndices.AddRange(ResolutionSettings.GetAvailableIndices());

            var options = new List<TMP_Dropdown.OptionData>(presetIndices.Count);
            foreach (int i in presetIndices)
            {
                options.Add(new TMP_Dropdown.OptionData(ResolutionSettings.Presets[i].DisplayName));
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            SelectCurrent();
        }

        private void SelectCurrent()
        {
            int pos = presetIndices.IndexOf(ResolutionSettings.CurrentIndex);
            dropdown.SetValueWithoutNotify(Mathf.Max(0, pos));
            dropdown.RefreshShownValue();
        }

        private void OnValueChanged(int pos)
        {
            if (pos < 0 || pos >= presetIndices.Count) return;
            int index = presetIndices[pos];
            if (index == ResolutionSettings.CurrentIndex) return;
            ResolutionSettings.Apply(index);
        }

        private void OnSettingsChanged(int _)
        {
            SelectCurrent();
        }
    }
}
