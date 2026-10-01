package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.repository.ChatMessageRepository;
import com.WhoisntCitizen_server.common.exception.ForbiddenException;
import com.WhoisntCitizen_server.common.exception.NotFoundException;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import org.springframework.stereotype.Service;

import java.util.List;

/**
 * 채팅 전송/조회.
 * 방과 참가자는 로비(lobby)가 관리하는 Room을 그대로 사용하고, 로그인 유저 식별도 로비와 같은 방식을 씁니다.
 *  - roomId   = 로비 방 id
 *  - memberId = JWT의 sub → member.User(프로필)로 바꿔 userId로 사용
 *  - 닉네임   = 로비 방 참가자(RoomPlayer)의 nickname
 */
@Service
public class ChatMessageService {

    public static final int DEFAULT_LIMIT = 50;
    public static final int MAX_LIMIT = 200;

    private final ChatMessageRepository repository;
    private final LobbyRoomRepository roomRepository;
    private final UserRepository userRepository;

    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository) {
        this.repository = repository;
        this.roomRepository = roomRepository;
        this.userRepository = userRepository;
    }

    /**
     * 채팅 조회. 오래된 순(messageId 오름차순)으로 반환합니다.
     * - afterId가 없으면: 최신 limit개
     * - afterId가 있으면: 그 messageId 이후에 들어온 메시지 최대 limit개 (폴링용)
     * 로비 방이 없으면 404
     */
    public List<ChatMessageSummary> getMessages(long roomId, Long afterId, Integer limit) {
        getRoom(roomId);
        int size = normalizeLimit(limit);

        List<ChatMessage> messages = (afterId != null)
                ? repository.findAfter(roomId, afterId, size)
                : repository.findLatest(roomId, size);
        return messages.stream().map(ChatMessageSummary::from).toList();
    }

    /**
     * 채팅 전송
     * 404: 로비 방이 존재하지 않음 / 403: 현재 채팅할 수 없는 플레이어(로비 방 참가자가 아님)
     * 400: 프로필(User)이 없는 계정 (로비와 동일)
     */
    public ChatMessageResponse send(long roomId, Long memberId, String message) {
        Room room = getRoom(roomId);
        User user = userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));

        RoomPlayer player = room.getPlayers().stream()
                .filter(p -> user.getId().equals(p.getUserId()))
                .findFirst()
                .orElseThrow(() -> new ForbiddenException("현재 채팅할 수 없는 플레이어입니다. (방 참가자가 아님)"));

        ChatMessage saved = repository.save(roomId, MessageType.USER,
                player.getUserId(), player.getNickname(), message.trim());
        return ChatMessageResponse.from(saved);
    }

    /** 공지(시스템 메시지) 전송. 게임 페이즈 알림 등 서버 코드에서도 직접 호출할 수 있습니다. */
    public ChatMessageResponse sendSystem(long roomId, String message) {
        getRoom(roomId);
        return ChatMessageResponse.from(saveSystem(roomId, message));
    }

    /** 방 존재 여부를 확인하지 않고 시스템 메시지를 저장합니다. (로비 이벤트 처리용) */
    ChatMessage saveSystem(long roomId, String message) {
        return repository.save(roomId, MessageType.SYSTEM,
                ChatMessage.SYSTEM_USER_ID, ChatMessage.SYSTEM_NICKNAME, message.trim());
    }

    private Room getRoom(long roomId) {
        Room room = roomRepository.findById(roomId);
        if (room == null) {
            throw new NotFoundException("방이 존재하지 않습니다. (roomId=" + roomId + ")");
        }
        return room;
    }

    private static int normalizeLimit(Integer limit) {
        if (limit == null) return DEFAULT_LIMIT;
        if (limit < 1) throw new IllegalArgumentException("limit은 1 이상이어야 합니다.");
        return Math.min(limit, MAX_LIMIT);
    }
}
