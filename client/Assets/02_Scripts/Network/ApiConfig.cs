namespace WhoisntCitizen.Network
{
    /// <summary>
    /// 서버 접속 설정을 한곳에 모아 둔 클래스.
    /// 서버 주소가 바뀌면(예: 배포 서버) BaseUrl만 고치면 모든 API 호출에 반영된다.
    /// </summary>
    public static class ApiConfig
    {
        /// <summary>기본 서버 주소 (로컬 서버, 포트 8080). 상수라 [SerializeField] 기본값으로도 쓸 수 있다.</summary>
        public const string DefaultBaseUrl = "http://3.38.117.166:8080";

        /// <summary>실제로 요청을 보낼 서버 주소 (끝에 / 없이). 배포 서버로 바꿀 때 이 값을 바꾼다.</summary>
        public static string BaseUrl = DefaultBaseUrl;

        /// <summary>요청 하나당 최대 대기 시간(초). 넘으면 연결 실패로 처리한다.</summary>
        public static int TimeoutSeconds = 10;

        /// <summary>true면 모든 요청/응답을 Console에 출력한다. (디버깅용)</summary>
        public static bool LogRequests = true;
    }
}
