package com.WhoisntCitizen_server.chat.entity;

/** 메시지 종류 */
public enum MessageType {
    /** 사용자가 보낸 일반 메시지 */
    USER,
    /** 서버가 만든 시스템 메시지 (입장 알림, 공지) */
    SYSTEM
}
