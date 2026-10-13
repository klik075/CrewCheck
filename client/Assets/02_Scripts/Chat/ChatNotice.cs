using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhoisntCitizen.Chat
{
    /// <summary>
    /// 내 화면에만 보이는 안내 문장(로그인, 방 입장·퇴장, 입력 오류, 연결 상태 등)을
    /// 채팅창과 Unity 콘솔에 동시에 출력합니다.
    ///  - 서버에 저장되지 않으며 다른 플레이어에게는 보이지 않습니다. (방 전체 안내는 서버 시스템 메시지)
    ///  - 채팅창이 없는 씬(로비 등)에서 보낸 안내는 보관해 두었다가 채팅창(GameChatController)이 열리면 표시합니다.
    /// 사용: ChatNotice.Post("'{방 제목}' 방에 입장했습니다.");
    /// </summary>
    public static class ChatNotice
    {
        /// 채팅창이 열리기 전까지 보관하는 최대 개수 (넘으면 오래된 것부터 버림)
        public const int MaxPending = 20;

        /// 채팅창이 구독합니다. 구독자가 없으면 Pending에 보관합니다.
        public static event Action<string> Posted;

        static readonly Queue<string> Pending = new Queue<string>();

        /// <param name="keepUntilChatOpens">
        /// 채팅창이 없을 때 보관했다가 나중에 보여 줄지. 로그인·입장처럼 상태가 바뀐 안내는 true,
        /// 입력 오류처럼 그 화면에서만 의미 있는 안내는 false (콘솔에만 남음).
        /// </param>
        public static void Post(string text, bool keepUntilChatOpens = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("<color=#9FE8A0>[Chat][안내]</color> " + text);

            var handler = Posted;
            if (handler != null)
            {
                handler(text);
                return;
            }
            if (!keepUntilChatOpens) return;
            Pending.Enqueue(text);
            while (Pending.Count > MaxPending) Pending.Dequeue();
        }

        /// 보관 중인 안내를 꺼냅니다. (채팅창이 처음 열릴 때 호출)
        public static List<string> TakePending()
        {
            var list = new List<string>(Pending);
            Pending.Clear();
            return list;
        }

        // Enter Play Mode 설정에서 도메인 리로드를 끈 경우에도 이전 플레이의 구독/보관 내용이 남지 않도록
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Posted = null;
            Pending.Clear();
        }
    }
}
