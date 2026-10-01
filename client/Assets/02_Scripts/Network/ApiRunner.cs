using System.Collections;
using UnityEngine;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// ApiClient(static 클래스)가 코루틴을 돌리기 위해 쓰는 숨은 MonoBehaviour.
    ///
    /// static 클래스는 StartCoroutine을 호출할 수 없어서, 처음 요청할 때 이 오브젝트를 하나 만들고
    /// 씬이 바뀌어도 유지(DontDestroyOnLoad)한다. 직접 씬에 배치하거나 호출할 필요는 없다.
    /// </summary>
    public class ApiRunner : MonoBehaviour
    {
        private static ApiRunner instance;

        // 도메인 리로드를 끈 상태로 플레이해도 이전 플레이의 값이 남지 않도록 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        /// <summary>코루틴을 실행한다. 러너가 없으면 만든다.</summary>
        public static void Run(IEnumerator routine)
        {
            if (instance == null)
            {
                var go = new GameObject("[ApiRunner]");
                instance = go.AddComponent<ApiRunner>();
                DontDestroyOnLoad(go);
            }
            instance.StartCoroutine(routine);
        }

        private void Awake()
        {
            // 혹시 두 개가 생기면 나중 것을 제거한다.
            if (instance != null && instance != this) Destroy(gameObject);
        }
    }
}
