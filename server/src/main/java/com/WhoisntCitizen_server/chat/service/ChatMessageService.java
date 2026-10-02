package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.repository.ChatMessageRepository;
import com.WhoisntCitizen_server.common.exception.ForbiddenException;
import com.WhoisntCitizen_server.common.exception.NotFoundException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.List;

/**
 * 채팅 전송/조회.
 * 방과 참가자는 로비(lobby)가 관리하는 Room을 그대로 사용하고, 로그인 유저 식별도 로비와 같은 방식을 씁니다.
 *  - roomId   = 로비 방 id
 *  - memberId = JWT의 sub → member.User(프로필)로 바꿔 userId로 사용
 *  - 닉네임   = 로비 방 참가자(RoomPlayer)의 nickname
 *
 * 게임 중 채팅 규칙 (게임이 끝나 방이 대기 상태로 돌아가면 그 게임의 사망자·해적 채팅은 아무에게도 보이지 않습니다)
 *  - 사망자 채팅(DEAD): 진행 중인 게임에서 사망한 플레이어가 보낸 메시지.
 *    진영·직업과 관계없이 같은 게임의 사망자에게만 보이고, 사망자는 자신이 사망한 뒤에 오간 대화만 볼 수 있습니다.
 *  - 밤 채팅 규칙: 밤(NIGHT)에는 살아 있는 해적만 전체 채팅에 입력할 수 있고(그 밖의 생존자는 403),
 *    밤에 입력한 채팅(pirateOnly)은 해적에게만 보입니다. 해적은 낮이든 사망한 뒤든 언제든 읽을 수 있습니다.
 *    해적 여부는 Game.knownPirateAllies와 같은 규칙입니다: 해적은 처음부터, 앵무새는 접선한 뒤부터 해적으로 취급하고
 *    앵무새는 접선한 뒤에 오간 밤 채팅만 읽을 수 있습니다. (isPirate()를 쓰면 접선 전 앵무새가 드러남)
 *  - 사망자 규칙이 먼저입니다: 사망자는 밤에도 사망자 채팅을 할 수 있고, 사망한 해적이 밤에 보내면 사망자 채팅이 됩니다.
 */
@Service
public class ChatMessageService {

    public static final int DEFAULT_LIMIT = 50;
    public static final int MAX_LIMIT = 200;

    /** 숨긴 메시지 때문에 결과가 비지 않도록 더 읽어 올 때, 한 번 조회에서 읽는 최대 메시지 수 (방당 보관 개수와 같은 규모) */
    static final int MAX_SCAN = 1000;

    /** 밤에 해적이 아닌 생존자가 채팅을 보냈을 때 (403) */
    public static final String NIGHT_BLOCKED_MESSAGE = "밤에는 해적만 채팅할 수 있습니다.";

    /** 게임 중 보낸 메시지에 적용되는 규칙 */
    private enum Rule {
        /** 모두에게 보이는 채팅 */
        PUBLIC,
        /** 사망자 채팅 (사망자에게만) */
        DEAD,
        /** 밤에 해적이 보낸 채팅 (해적에게만) */
        NIGHT_PIRATE,
        /** 밤이라 보낼 수 없음 (해적이 아닌 생존자) */
        NIGHT_BLOCKED
    }

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
     * - 조회하는 사람(memberId)에게 보이지 않는 메시지(사망자·해적 채팅)는 빼고, 그만큼 더 읽어서 limit개를 채웁니다.
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
     * 403: 밤에는 해적만 채팅할 수 있음 (사망자는 밤에도 사망자 채팅 가능)
     * 게임 중이면 보낸 사람의 상태에 따라 정해집니다: 사망자 → DEAD, 밤의 해적 → 해적에게만 보이는 전체 채팅, 그 밖 → 전체 채팅
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
        Rule rule = game == null ? Rule.PUBLIC : ruleOf(game, player.getUserId());
        if (rule == Rule.NIGHT_BLOCKED) {
            throw new ForbiddenException(NIGHT_BLOCKED_MESSAGE);
        }

        MessageType type = rule == Rule.DEAD ? MessageType.DEAD : MessageType.USER;
        String gameId = rule == Rule.PUBLIC ? null : game.getGameId();
        ChatMessage saved = repository.save(roomId, type,
                player.getUserId(), player.getNickname(), message.trim(), gameId, rule == Rule.NIGHT_PIRATE);
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

    /** 같은 게임의 해적(접선한 앵무새 포함)에게만 보이는 시스템 메시지를 저장합니다. (해적 안내 이벤트 처리용) */
    ChatMessage saveSystemForPirates(long roomId, String gameId, String message) {
        return repository.save(roomId, MessageType.SYSTEM,
                ChatMessage.SYSTEM_USER_ID, ChatMessage.SYSTEM_NICKNAME, message.trim(), gameId, true);
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
     *  - 일반·시스템 메시지: 모두에게 보임
     *  - 사망자 채팅: 같은 진행 중 게임에서 사망했고, 사망한 뒤에 보낸 메시지
     *  - 밤에 해적이 보낸 채팅: 같은 진행 중 게임의 해적이고, 해적으로 취급된 뒤에 보낸 메시지 (해적은 처음부터, 앵무새는 접선 시각부터)
     * (조회하는 사람의 프로필·게임 상태는 숨김 대상 메시지가 있을 때만 확인해 DB 조회를 줄입니다)
     */
    private final class Visibility {
        private final Room room;
        private final Long memberId;
        private boolean resolved;
        private String gameId;        // 조회하는 사람이 참가 중인 진행 중 게임 id (아니면 null)
        private Instant diedAt;       // 그 게임에서 사망한 시각 (살아 있으면 null)
        private Instant pirateSince;  // 밤 채팅을 읽을 수 있게 된 시각 (해적이 아니면 null)

        Visibility(Room room, Long memberId) {
            this.room = room;
            this.memberId = memberId;
        }

        boolean canSee(ChatMessage m) {
            boolean dead = m.type() == MessageType.DEAD;
            boolean pirateOnly = m.visibleToPiratesOnly();
            if (!dead && !pirateOnly) return true;
            resolve();
            if (gameId == null || !gameId.equals(m.gameId()) || m.createdAt() == null) return false;
            Instant since = dead ? diedAt : pirateSince;
            return since != null && !toInstant(m).isBefore(since);
        }

        private void resolve() {
            if (resolved) return;
            resolved = true;
            if (memberId == null) return;
            Game game = activeGame(room);
            if (game == null) return;
            Long userId = userRepository.findByMemberId(memberId).map(User::getId).orElse(null);
            if (userId == null) return;
            synchronized (game) {
                GamePlayer me = findPlayer(game, userId);
                if (me == null) return; // 게임 참가자가 아님 (관전자 등)
                gameId = game.getGameId();
                if (!me.isAlive()) diedAt = me.getDiedAt() != null ? me.getDiedAt() : Instant.EPOCH;
                pirateSince = pirateChatSince(me);
            }
        }
    }

    /** 메시지 createdAt(서버 기본 시간대의 LocalDateTime)을 사망·접선 시각과 비교할 수 있게 Instant로 바꿉니다. */
    private static Instant toInstant(ChatMessage m) {
        return m.createdAt().atZone(ZoneId.systemDefault()).toInstant();
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

    /**
     * 게임 중 보낸 메시지의 규칙
     *  - 사망자 → DEAD (밤에도 가능)
     *  - 밤: 해적 → NIGHT_PIRATE, 그 밖(게임 참가자가 아닌 사람 포함) → NIGHT_BLOCKED
     *  - 그 밖 → PUBLIC
     */
    private static Rule ruleOf(Game game, Long userId) {
        synchronized (game) {
            GamePlayer p = findPlayer(game, userId);
            if (p != null && !p.isAlive()) return Rule.DEAD;
            if (game.getPhase() == GamePhase.NIGHT) {
                return p != null && pirateChatSince(p) != null ? Rule.NIGHT_PIRATE : Rule.NIGHT_BLOCKED;
            }
            return Rule.PUBLIC;
        }
    }

    /**
     * 밤 채팅을 읽을 수 있게 된 시각(해적으로 취급되기 시작한 시각). 해적이 아니면 null.
     * 해적은 게임 시작부터(EPOCH), 앵무새는 해적과 접선한 시각부터. (Game.knownPirateAllies와 같은 규칙)
     */
    private static Instant pirateChatSince(GamePlayer p) {
        if (p.isRaider()) return Instant.EPOCH;
        if (p.isParrot() && p.isContacted()) return p.getContactedAt();
        return null;
    }

    /** 게임 참가자. 없으면 null (호출하는 쪽에서 game을 잠근 상태) */
    private static GamePlayer findPlayer(Game game, Long userId) {
        for (GamePlayer p : game.getPlayers()) {
            if (p.getPlayerId().equals(userId)) return p;
        }
        return null;
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
