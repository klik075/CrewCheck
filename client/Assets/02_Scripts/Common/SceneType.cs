namespace WhoisntCitizen.Common
{
    /// <summary>
    /// 게임에서 사용하는 씬 종류.
    /// 씬 이름 문자열을 코드 곳곳에 직접 쓰지 않도록, 씬 전환은 항상 이 enum으로 요청한다.
    /// 실제 씬 파일 이름과의 연결은 SceneLoader.SceneNames 한 곳에서만 관리한다.
    /// </summary>
    public enum SceneType
    {
        Title, // 로그인 / 회원가입
        Lobby, // 방 목록 / 방 생성
        Room,  // 대기실: 참가자 목록, 방장 게임 시작, 나가기 (RoomUIController)
        Game,  // 게임 진행 (GameScene, 다른 팀원 담당)
    }
}
