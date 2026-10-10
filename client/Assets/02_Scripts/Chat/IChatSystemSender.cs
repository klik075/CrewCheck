using System;

namespace WhoisntCitizen.Chat
{
    /// <summary>
    /// 시스템 메시지(공지)를 보낼 수 있는 채팅 컨트롤러.
    /// 에디터 창(Tools > Chat > System Message Console)이 GameScene(GameChatController) 등
    /// 이 인터페이스를 구현한 채팅 컨트롤러를 같은 방식으로 찾아 쓰기 위한 인터페이스입니다.
    /// </summary>
    public interface IChatSystemSender
    {
        /// 로그인 + 방 참가가 끝나 채팅할 수 있는 상태인지
        bool IsReady { get; }

        /// 채팅 중인 로비 방 id
        long RoomId { get; }

        /// <param name="onDone">(성공 여부, 오류 문구)</param>
        void SendSystemMessage(string text, Action<bool, string> onDone = null);
    }
}
