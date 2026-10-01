package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.repository.ChatMessageRepository;
import com.WhoisntCitizen_server.common.exception.ForbiddenException;
import com.WhoisntCitizen_server.common.exception.NotFoundException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import org.springframework.stereotype.Service;

import java.util.ArrayList;
import java.util.List;

/**
 * 채팅 전송/조회.
 * 방과 참가자는 로비(lobby)가 관리하는 Room을 그대로 사용하고, 로그인 유저 식별도 로비와 같은 방식을 씁니다.
 *  - roomId   = 로비 방 id
 *  - memberId = JWT의 sub → member.User(프로필)로 바꿔 userId로 사용
 *  - 닉네임   = 로비 방 참가자(RoomPlayer)의 nickname
 *
 * 게임 중 채팅 규칙
 *  - 사망자 채팅: 진행 중인 게임에서 사망한 플레이어가 보낸 메시지는 DEAD로 저장하고,
 *    진영·직업과 관계없이 같은 게임에서 사망한 플레이어에게만 보여 줍니다. (살아 있는 플레이어·관전자에게는 숨김)
 *    게임이 끝나면(방이 대기 상태로 돌아가면) 그 게임의 사망자 채팅은 아무에게도 보이지 않습니다.
 */
@Service
public class ChatMessageService {

    public static final int DEFAULT_LIMIT = 50;
    public static final int MAX_LIMIT = 200;

    /** 숨긴 메시지 때문에 결과가 비지 않도록 더 읽어 올 때, 한 번 조회에서 읽는 최대 메시지 수 (방당 보관 개수와 같은 규모) */
    static final int MAX_SCAN = 1000;

    private final ChatMessageRepository repository;
    private final LobbyRoomRepository roomRepository;
    private final UserRepository userRepository;
    private final GameRepository gameRepository;

    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository,
                              GameRepository gameRepository) {
        this.repository = repository;
        this.roomRepository = roomRepository;
        this.userRepository = userRepository;
        this.gameRepository = gameRepository;
    }

    /**
     * 채팅 조회. 오래된 순(messageId 오름차순)으로 반환합니다.
     * - afterId가 없으면: 최신 limit개
     * - afterId가 있으면: 그 messageId 이후에 들어온 메시지 최대 limit개 (폴링용)
     * - 조회하는 사람(memberId)에게 보이지 않는 메시지(사망자 채팅 등)는 빼고, 그만큼 더 읽어서 limit개를 채웁니다.
     * 로비 방이 없으면 404
     */
    public List<ChatMessageSummary> getMessages(long roomId, Long memberId, Long afterId, Integer limit) {
        Room room = getRoom(roomId);
        int size = normalizeLimit(limit);
        Visibility visibility = new Visibility(room, memberId);

        List<ChatMessage> messages = (afterId != null)
                ? findVisibleAfter(roomId, afterId, size, visibility)
                : findVisibleLatest(roomId, size, visibility);
        return messages.stream().map(ChatMessageSummary::from).toList();
    }

    /**
     * 채팅 전송
     * 404: 로비 방이 존재하지 않음 / 403: 현재 채팅할 수 없는 플레이어(로비 방 참가자가 아님)
     * 400: 프로필(User)이 없는 계정 (로비와 동일)
     * 진행 중인 게임에서 사망한 플레이어가 보내면 사망자 채팅(DEAD)으로 저장합니다.
     */
    public ChatMessageResponse send(long roomId, Long memberId, String message) {
        Room room = getRoom(roomId);
        User user = userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));

        RoomPlayer player = room.getPlayers().stream()
                .filter(p -> user.getId().equals(p.getUserId()))
                .findFirst()
                .orElseThrow(() -> new ForbiddenException("현재 채팅할 수 없는 플레이어입니다. (방 참가자가 아님)"));

        Game game = activeGame(room);
        if (game != null && isDeadIn(game, player.getUserId())) {
            ChatMessage saved = repository.save(roomId, MessageType.DEAD,
                    player.getUserId(), player.getNickname(), message.trim(), game.getGameId());
            return ChatMessageResponse.from(saved);
        }

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

    // ---------- 조회: 보이는 메시지만 ----------

    /** afterId 이후 메시지를 앞에서부터 읽으며 보이는 것만 최대 size개 모읍니다. */
    private List<ChatMessage> findVisibleAfter(long roomId, long afterId, int size, Visibility visibility) {
        List<ChatMessage> result = new ArrayList<>();
        long cursor = afterId;
        int scanned = 0;
        while (scanned < MAX_SCAN) {
            List<ChatMessage> batch = repository.findAfter(roomId, cursor, size);
            for (ChatMessage m : batch) {
                if (visibility.canSee(m)) {
                    result.add(m);
                    if (result.size() >= size) return result;
                }
            }
            if (batch.size() < size) break; // 더 읽을 메시지 없음
            cursor = batch.get(batch.size() - 1).id();
            scanned += batch.size();
        }
        return result;
    }

    /** 최신 메시지에서 보이는 것만 최대 size개. 숨긴 메시지가 많으면 읽는 범위를 늘립니다. */
    private List<ChatMessage> findVisibleLatest(long roomId, int size, Visibility visibility) {
        int window = size;
        while (true) {
            List<ChatMessage> batch = repository.findLatest(roomId, window);
            List<ChatMessage> visible = batch.stream().filter(visibility::canSee).toList();
            if (visible.size() >= size || batch.size() < window || window >= MAX_SCAN) {
                return visible.subList(Math.max(0, visible.size() - size), visible.size());
            }
            window = Math.min(window * 2, MAX_SCAN);
        }
    }

    /**
     * 조회하는 사람 기준으로 메시지가 보이는지 판단합니다.
     * 일반·시스템 메시지는 모두에게 보입니다. 사망자 채팅은 그 게임이 진행 중이고 조회하는 사람이 그 게임에서 사망했을 때만 보입니다.
     * (조회하는 사람의 프로필·게임 상태는 사망자 채팅이 있을 때만 확인해 DB 조회를 줄입니다)
     */
    private final class Visibility {
        private final Room room;
        private final Long memberId;
        private boolean resolved;
        private String deadInGameId; // 조회하는 사람이 사망한 진행 중 게임 id (아니면 null)

        Visibility(Room room, Long memberId) {
            this.room = room;
            this.memberId = memberId;
        }

        boolean canSee(ChatMessage m) {
            if (m.type() != MessageType.DEAD) return true;
            String deadGame = deadInGameId();
            return deadGame != null && deadGame.equals(m.gameId());
        }

        private String deadInGameId() {
            if (!resolved) {
                resolved = true;
                deadInGameId = resolveDeadInGameId();
            }
            return deadInGameId;
        }

        private String resolveDeadInGameId() {
            if (memberId == null) return null;
            Game game = activeGame(room);
            if (game == null) return null;
            Long userId = userRepository.findByMemberId(memberId).map(User::getId).orElse(null);
            return userId != null && isDeadIn(game, userId) ? game.getGameId() : null;
        }
    }

    // ---------- 게임 상태 ----------

    /** 방에서 진행 중인 게임. 대기 중이거나 이미 끝난 게임이면 null */
    private Game activeGame(Room room) {
        if (!room.isInGame() || room.getGameId() == null) return null;
        Game game = gameRepository.findById(room.getGameId()).orElse(null);
        if (game == null) return null;
        synchronized (game) {
            return game.isEnded() ? null : game;
        }
    }

    /** 게임 참가자이고 사망했는지. (게임에 참가하지 않은 사람은 false) */
    private static boolean isDeadIn(Game game, Long userId) {
        synchronized (game) {
            for (GamePlayer p : game.getPlayers()) {
                if (p.getPlayerId().equals(userId)) return !p.isAlive();
            }
            return false;
        }
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
