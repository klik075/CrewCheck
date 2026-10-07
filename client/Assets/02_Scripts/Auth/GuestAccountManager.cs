using System;
using UnityEngine;

// 게스트 UUID를 PlayerPrefs에 영구 보관한다.
// SystemInfo.deviceUniqueIdentifier는 플랫폼별로 비어 있거나(WebGL 등) 바뀔 수 있고 개인정보 이슈도 있어서,
// 처음 한 번 Guid를 만들어 저장해 쓴다. 앱 데이터를 지우면 새 게스트가 된다.
public static class GuestAccountManager
{
    private const string PrefsKey = "guest_uuid";

    public static string GetOrCreateUuid()
    {
        string uuid = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(uuid))
        {
            uuid = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(PrefsKey, uuid);
            PlayerPrefs.Save();
        }
        return uuid;
    }

    /// <summary>저장된 UUID 삭제 (테스트용). 다음 게스트 로그인 때 새 계정이 만들어진다.</summary>
    public static void Reset()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }
}
