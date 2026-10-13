using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    public enum ChatEntryKind
    {
        Player,  // 플레이어 채팅 (사망자 채팅·밤 해적 채팅 포함)
        System,  // 서버·게임 안내 ([시스템])
        Notice,  // 내 화면에만 보이는 안내·오류 ([안내], [알림])
        Whisper, // 귓속말 (서버 기능이 생기면 쓴다)
    }

    // 채팅 기록 한 줄. ChatLogView가 줄 모양(RowFactory)이 있으면 그 모양으로, 없으면 Rich 한 줄로 그린다.
    public sealed class ChatEntry
    {
        public ChatEntryKind Kind;
        public long SenderId = -1;   // 보낸 플레이어 (모르면 -1)
        public string Nickname;      // Player: 보낸 사람, System·Notice: 이름패에 쓸 말 ("시스템", "안내", "알림")
        public string Text;          // 리치 텍스트로 해석하지 않는 본문. 비어 있으면 Rich를 그대로 쓴다
        public Color? TextColor;     // 비우면 줄 모양의 기본색
        public bool Mine, Dead, Night;
        public DateTime? Time;       // 받은(보낸) 시각. 모르면 비운다 (예: 입장할 때 받아 온 이전 대화)
        public string Rich;          // 같은 줄을 리치 텍스트 한 줄로 (줄 모양이 없을 때)
    }

    public enum ChatTab
    {
        All,     // 모든 줄
        System,  // 시스템·안내·알림 줄
        General, // 플레이어 채팅만 (사망자 채팅·밤 해적 채팅 포함)
        Whisper, // 귓속말 줄
    }
}
