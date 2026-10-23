using System;
using UnityEngine;

// 게스트 UUID를 PlayerPrefs에 영구 보관한다.
// SystemInfo.deviceUniqueIdentifier는 플랫폼별로 비어 있거나(WebGL 등) 바뀔 수 있고 개인정보 이슈도 있어서,
// 처음 한 번 Guid를 만들어 저장해 쓴다. 앱 데이터를 지우면 새 게스트가 된다.
//
// 같은 PC에서 클라이언트를 여러 개 띄우면 PlayerPrefs(레지스트리)를 모두 같이 쓰므로 같은 UUID를 읽어 한 게스트 계정을 공유하게 된다.
// (Guid는 매번 무작위라 시드 문제가 아니라 저장소 공유 문제다.) 그래서 인스턴스마다 저장 키를 나눌 수 있게 한다.
//   - 빌드: 실행 인자 -guestSlot <이름>  예) Game.exe -guestSlot 2   (인자가 없으면 기존 키 그대로)
//   - 에디터: ParrelSync 복제 프로젝트(폴더 이름에 "_clone")는 자동으로 복제본 폴더 이름을 키에 붙인다.
public static class GuestAccountManager
{
    private const string BasePrefsKey = "guest_uuid";
    private const string SlotArgument = "-guestSlot";

    // 인스턴스를 구분하는 이름. 비어 있으면 기존 키(guest_uuid)를 그대로 쓴다.
    private static string PrefsKey => BasePrefsKey + InstanceSuffix;

    /// <summary>
    /// PlayerPrefs 키 뒤에 붙이는 인스턴스 구분 접미사. 구분할 필요가 없으면 빈 문자열.
    /// 리프레시 토큰처럼 계정별로 저장하는 값도 같은 규칙을 써야 같은 PC의 클라이언트끼리 덮어쓰지 않는다.
    /// </summary>
    public static string InstanceSuffix
    {
        get
        {
            string slot = ReadSlot();
            return string.IsNullOrEmpty(slot) ? string.Empty : "_" + slot;
        }
    }

    private static string ReadSlot()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == SlotArgument) return args[i + 1];
        }

#if UNITY_EDITOR
        // <프로젝트>/Assets → 프로젝트 폴더 이름. ParrelSync 복제본은 "..._clone_0" 같은 이름이다.
        string projectFolder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Application.dataPath));
        if (!string.IsNullOrEmpty(projectFolder) && projectFolder.Contains("_clone")) return projectFolder;
#endif
        return null;
    }

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
