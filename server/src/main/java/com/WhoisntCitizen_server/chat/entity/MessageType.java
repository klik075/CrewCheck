package com.WhoisntCitizen_server.chat.entity;

/** 메시지 종류 */
public enum MessageType {
    /** 사용자가 보낸 일반 메시지 */
    USER,
    /** 서버가 만든 시스템 메시지 (입장 알림, 공지) */
    SYSTEM,
    /**
     * 게임 중 사망자가 보낸 메시지 (사망자 채팅).
     * 진영·직업과 관계없이 같은 게임에서 사망한 플레이어에게만 보입니다. (클라이언트는 회색으로 표시)
     */
    DEAD
}
