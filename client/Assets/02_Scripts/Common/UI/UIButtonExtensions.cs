using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드로 버튼을 생성할 때 사용. Button + UIButtonBase(클릭 효과음)를 함께 붙여준다.
/// </summary>
public static class UIButtonExtensions
{
    public static Button AddClickSoundButton(this GameObject go)
    {
        Button button = go.GetComponent<Button>();
        if (button == null) button = go.AddComponent<Button>();
        if (go.GetComponent<UIButtonBase>() == null) go.AddComponent<UIButtonBase>();
        return button;
    }
}
