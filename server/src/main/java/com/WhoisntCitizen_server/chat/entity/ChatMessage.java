package com.WhoisntCitizen_server.chat.entity;

import java.time.LocalDateTime;

/**
 * 채팅 메시지. Redis에 JSON 문자열로 저장됩니다.
 * nickname은 보낸 시점의 닉네임을 함께 저장합니다.
 * 시스템 메시지(입장/퇴장 알림, 공지)는 userId가 0(SYSTEM_USER_ID) 입니다.
 * gameId는 게임 중 일부에게만 보이는 메시지(사망자 채팅, 밤의 해적·세이렌 채팅)만 채워집니다. 누구에게 보일지 판단할 때만 쓰고 응답에는 넣지 않습니다.
 * pirateOnly: 밤에 해적이 보낸 메시지. 전체 채팅(type=USER)으로 오가지만 같은 게임의 해적에게만 보입니다. (응답에서는 nightChat)
 * sirenOnly: 밤에 세이렌이 보낸 메시지. 같은 게임의 세이렌 팀에게만 보입니다. 팀원은 읽기만 합니다. (응답에서는 sirenChat)
 * (gameId·pirateOnly·sirenOnly가 없던 예전 Redis 데이터는 null로 읽힙니다)
 */
public record ChatMessage(
        Long id,
        Long roomId,
        MessageType type,
        Long userId,
        String nickname,
        String message,
        LocalDateTime createdAt,
        String gameId,
        Boolean pirateOnly,
        Boolean sirenOnly
) {
    public static final int MAX_MESSAGE_LENGTH = 1000;

    /** 시스템 메시지의 nickname 값 */
    public static final String SYSTEM_NICKNAME = "SYSTEM";

    /** 시스템 메시지의 userId 값 (실제 유저 id는 1부터 시작하므로 겹치지 않음) */
    public static final long SYSTEM_USER_ID = 0L;

    public ChatMessage {
        if (type == null) type = MessageType.USER;
        // 시스템 메시지는 userId를 0으로 고정 (이전에 null로 저장된 Redis 데이터도 읽을 때 0으로 맞춤)
        if (type == MessageType.SYSTEM) userId = SYSTEM_USER_ID;
        if (pirateOnly == null) pirateOnly = Boolean.FALSE;
        if (sirenOnly == null) sirenOnly = Boolean.FALSE;
    }

    /** 세이렌 채팅이 아닌 메시지 */
    public ChatMessage(Long id, Long roomId, MessageType type, Long userId, String nickname, String message,
                       LocalDateTime createdAt, String gameId, Boolean pirateOnly) {
        this(id, roomId, type, userId, nickname, message, createdAt, gameId, pirateOnly, false);
    }

    /** 게임과 관계없는 메시지 (gameId = null) */
    public ChatMessage(Long id, Long roomId, MessageType type, Long userId, String nickname, String message,
                       LocalDateTime createdAt) {
        this(id, roomId, type, userId, nickname, message, createdAt, null, false, false);
    }

    /** 밤에 해적만 볼 수 있는 메시지인지 */
    public boolean visibleToPiratesOnly() {
        return Boolean.TRUE.equals(pirateOnly);
    }

    /** 밤에 세이렌 팀만 볼 수 있는 메시지인지 */
    public boolean visibleToSirenTeamOnly() {
        return Boolean.TRUE.equals(sirenOnly);
    }
}
