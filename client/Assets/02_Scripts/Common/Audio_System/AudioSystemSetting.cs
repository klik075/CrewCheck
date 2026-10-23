using UnityEngine;

public static class CreateAssetMenuPathLib
{
    public const string SystemScriptableObjectCreatePath = "System/";
}

[CreateAssetMenu(menuName = CreateAssetMenuPathLib.SystemScriptableObjectCreatePath + "AudioSetting")]
public class AudioSystemSetting : ScriptableObject
{
    public GameObject AudioSourcePlayObject;
}