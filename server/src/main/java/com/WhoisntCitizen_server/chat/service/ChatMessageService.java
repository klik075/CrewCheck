package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.event.ChatMessageSavedEvent;
import com.WhoisntCitizen_server.chat.filter.ProfanityFilter;
import com.WhoisntCitizen_server.chat.repository.ChatMessageRepository;
import com.WhoisntCitizen_server.chat.spam.ChatSpamGuard;
import com.WhoisntCitizen_server.common.exception.ForbiddenException;
import com.WhoisntCitizen_server.common.exception.NotFoundException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.function.Function;
import java.util.function.Predicate;

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
 *    해적 여부는 Game.knownPirateAllies와 같은 규칙입니다: 해적·요리사는 처음부터, 앵무새는 접선한 뒤부터 해적으로 취급하고
 *    앵무새는 접선한 뒤에 오간 밤 채팅만 읽을 수 있습니다. (isPirate()를 쓰면 접선 전 앵무새가 드러남)
 *  - 세이렌 팀 채팅: 밤에 살아 있는 세이렌이 입력한 채팅(sirenOnly)은 세이렌 팀에게만 보입니다. 유혹당한 팀원은 밤에 입력할 수 없고
 *    읽기만 하며(해적이 아니므로 403), 유혹당한 뒤에 오간 메시지만 볼 수 있습니다. 사망한 뒤에도 읽을 수 있습니다.
 *  - 사망자 규칙이 먼저입니다: 사망자는 밤에도 사망자 채팅을 할 수 있고, 사망한 해적이 밤에 보내면 사망자 채팅이 됩니다.
 *
 * 욕설 검열: 사용자가 보낸 메시지는 저장하기 전에 ProfanityFilter로 욕설 부분을 순화 단어(땡큐, 좋아요…)로 바꿉니다. (사망자·해적·세이렌 채팅 포함)
 * 저장소(Redis)와 다른 사람의 화면에는 바뀐 문장만 남습니다. 시스템 메시지는 서버가 만든 문장이라 검사하지 않습니다.
 *
 * 도배 방지: 같은 내용을 연속으로 3번째 보낼 때부터 응답에 경고 문구(spamWarning)를 넣고, 10번째에는 그 메시지를 저장하지 않고
 * 3분간 채팅을 금지합니다(429 CHAT_MUTED). 금지 중에 보낸 메시지도 모두 429입니다. (ChatSpamGuard, 사망자·해적·세이렌 채팅 포함)
 */
@Service
public class ChatMessageService {

    public static final int DEFAULT_LIMIT = 50;
    public static final int MAX_LIMIT = 200;

    /** 숨긴 메시지 때문에 결과가 비지 않도록 더 읽어 올 때, 한 번 조회에서 읽는 최대 메시지 수 (방당 보관 개수와 같은 규모) */
    static final int MAX_SCAN = 1000;

    /** 밤에 해적·세이렌이 아닌 생존자가 채팅을 보냈을 때 (403) */
    public static final String NIGHT_BLOCKED_MESSAGE = "밤에는 해적과 세이렌만 채팅할 수 있습니다.";

    /** 게임 중 보낸 메시지에 적용되는 규칙 */
    private enum Rule {
        /** 모두에게 보이는 채팅 */
        PUBLIC,
        /** 사망자 채팅 (사망자에게만) */
        DEAD,
        /** 밤에 해적이 보낸 채팅 (해적에게만) */
        NIGHT_PIRATE,
        /** 밤에 세이렌이 보낸 채팅 (세이렌 팀에게만) */
        NIGHT_SIREN,
        /** 밤이라 보낼 수 없음 (해적·세이렌이 아닌 생존자) */
        NIGHT_BLOCKED
    }

    private final ChatMessageRepository repository;
    private final LobbyRoomRepository roomRepository;
    private final UserRepository userRepository;
    private final GameRepository gameRepository;
    // 게임 상태를 읽을 때 게임 진행 쪽과 같은 잠금을 쓴다. (진행 중에 바뀌는 사망·접선·페이즈를 일관되게 읽기 위해)
    private final GameLock gameLock;
    // 메시지를 저장할 때마다 ChatMessageSavedEvent 발행 (실시간 모듈이 받아 볼 수 있는 사람에게 chat.changed를 보낸다)
    private final ApplicationEventPublisher eventPublisher;
    // 사용자 메시지의 욕설을 저장 전에 순화 단어로 바꾼다 (ProfanityFilterConfig가 만든 빈)
    private final ProfanityFilter profanityFilter;
    // 같은 내용 반복(도배)을 경고하고, 계속하면 채팅을 금지한다 (ChatSpamConfig가 만든 빈)
    private final ChatSpamGuard spamGuard;

    @Autowired
    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository,
                              GameRepository gameRepository,
                              GameLock gameLock,
                              ApplicationEventPublisher eventPublisher,
                              ProfanityFilter profanityFilter,
                              ChatSpamGuard spamGuard) {
        this.repository = repository;
        this.roomRepository = roomRepository;
        this.userRepository = userRepository;
        this.gameRepository = gameRepository;
        this.gameLock = gameLock;
        this.eventPublisher = eventPublisher;
        this.profanityFilter = profanityFilter == null ? ProfanityFilter.disabled() : profanityFilter;
        this.spamGuard = spamGuard == null ? ChatSpamGuard.disabled() : spamGuard;
    }

    /** 도배 방지 없이 만드는 생성자 (도배 방지와 관계없는 테스트용) */
    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository,
                              GameRepository gameRepository,
                              GameLock gameLock,
                              ApplicationEventPublisher eventPublisher,
                              ProfanityFilter profanityFilter) {
        this(repository, roomRepository, userRepository, gameRepository, gameLock, eventPublisher, profanityFilter,
                ChatSpamGuard.disabled());
    }

    /** 욕설 검열 없이 만드는 생성자 (검열과 관계없는 테스트용) */
    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository,
                              GameRepository gameRepository,
                              GameLock gameLock,
                              ApplicationEventPublisher eventPublisher) {
        this(repository, roomRepository, userRepository, gameRepository, gameLock, eventPublisher,
                ProfanityFilter.disabled());
    }

    /** 이벤트를 발행하지 않는 생성자 (실시간 알림이 필요 없는 테스트용) */
    public ChatMessageService(ChatMessageRepository repository,
                              LobbyRoomRepository roomRepository,
                              UserRepository userRepository,
                              GameRepository gameRepository,
                              GameLock gameLock) {
        this(repository, roomRepository, userRepository, gameRepository, gameLock, event -> { });
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
     * 403: 밤에는 해적과 세이렌만 채팅할 수 있음 (사망자는 밤에도 사망자 채팅 가능)
     * 게임 중이면 보낸 사람의 상태에 따라 정해집니다: 사망자 → DEAD, 밤의 해적 → 해적에게만 보이는 전체 채팅,
     * 밤의 세이렌 → 세이렌 팀에게만 보이는 채팅, 그 밖 → 전체 채팅
     * 욕설은 순화 단어로 바꿔서 저장하고, 응답에도 바뀐 문장이 들어갑니다. (보낸 사람도 바뀐 결과를 봅니다)
     * 도배: 같은 내용 3번째부터 응답의 spamWarning에 경고 문구 / 10번째와 금지 중에는 저장하지 않고 ChatMutedException(429)
     */
    public ChatMessageResponse send(long roomId, Long memberId, String message) {
        Room room = getRoom(roomId);
        User user = userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));

        RoomPlayer player = room.getPlayers().stream()
                .filter(p -> user.getId().equals(p.getUserId()))
                .findFirst()
                .orElseThrow(() -> new ForbiddenException("현재 채팅할 수 없는 플레이어입니다. (방 참가자가 아님)"));

        // 게임 잠금 안에서 게임을 읽고 규칙까지 정한다. 메시지 저장은 잠금 밖에서 한다
        // 볼 수 있는 사람(audience)도 같은 잠금 안에서 정한다. (실시간 알림을 볼 수 있는 사람에게만 보내기 위해)
        SendRule decided = withActiveGame(room, game -> {
            if (game == null) return new SendRule(Rule.PUBLIC, null, null);
            Rule decidedRule = ruleOf(game, player.getUserId());
            return new SendRule(decidedRule, game.getGameId(), audienceOf(game, decidedRule));
        });
        Rule rule = decided.rule();
        if (rule == Rule.NIGHT_BLOCKED) {
            throw new ForbiddenException(NIGHT_BLOCKED_MESSAGE);
        }

        MessageType type = rule == Rule.DEAD ? MessageType.DEAD : MessageType.USER;
        String gameId = rule == Rule.PUBLIC ? null : decided.gameId();

        // 도배 확인 (채팅 금지면 ChatMutedException → 429, 메시지는 저장하지 않는다). 비교는 욕설을 바꾸기 전 원문으로 한다
        String spamWarning = spamGuard.check(player.getUserId(), message);

        // 욕설 필터링
        String text = profanityFilter.mask(message.trim());
        ChatMessage saved = repository.save(roomId, type, player.getUserId(), player.getNickname(), text,
                gameId, rule == Rule.NIGHT_PIRATE, rule == Rule.NIGHT_SIREN);
        publishSaved(saved, decided.audience());
        return ChatMessageResponse.from(saved, spamWarning);
    }

    /** 공지(시스템 메시지) 전송. 게임 페이즈 알림 등 서버 코드에서도 직접 호출할 수 있습니다. */
    public ChatMessageResponse sendSystem(long roomId, String message) {
        getRoom(roomId);
        return ChatMessageResponse.from(saveSystem(roomId, message));
    }

    /** 방 존재 여부를 확인하지 않고 시스템 메시지를 저장합니다. (로비 이벤트 처리용) */
    ChatMessage saveSystem(long roomId, String message) {
        ChatMessage saved = repository.save(roomId, MessageType.SYSTEM,
                ChatMessage.SYSTEM_USER_ID, ChatMessage.SYSTEM_NICKNAME, message.trim());
        publishSaved(saved, null);
        return saved;
    }

    /** 삭제된 방의 메시지를 지웁니다. (로비 이벤트 처리용) */
    void deleteRoomMessages(long roomId) {
        repository.deleteRoom(roomId);
    }

    /** 게임 시작 표시를 남깁니다. 이후 메시지는 게임이 끝나면 지워집니다. (로비 이벤트 처리용) */
    void markGameStart(long roomId) {
        repository.markGameStart(roomId);
    }

    /**
     * 게임 중에 오간 메시지(게임 시작 표시 이후)를 모두 지웁니다. (로비 이벤트 처리용)
     * 대기실로 돌아온 방의 채팅에는 게임 전 대기실 대화와 그 뒤의 메시지만 남습니다.
     * @return 지운 메시지 수
     */
    long clearGameMessages(long roomId) {
        return repository.deleteSinceGameStart(roomId);
    }

    /** 같은 게임의 해적(접선한 앵무새 포함)에게만 보이는 시스템 메시지를 저장합니다. (해적 안내 이벤트 처리용) */
    ChatMessage saveSystemForPirates(long roomId, String gameId, String message) {
        ChatMessage saved = repository.save(roomId, MessageType.SYSTEM,
                ChatMessage.SYSTEM_USER_ID, ChatMessage.SYSTEM_NICKNAME, message.trim(), gameId, true);
        // 볼 수 있는 해적을 게임 잠금 안에서 정한다. 게임이 없거나 끝났으면 아무에게도 보이지 않는다
        Set<Long> pirates = gameLock.withLock(gameId, () -> gameRepository.findById(gameId)
                .filter(game -> !game.isEnded())
                .map(game -> audienceOf(game, Rule.NIGHT_PIRATE))
                .orElse(Set.of()));
        publishSaved(saved, pirates);
        return saved;
    }

    /** 저장 알림. audience == null이면 모두에게 보이는 메시지 */
    private void publishSaved(ChatMessage saved, Set<Long> audience) {
        if (saved == null || saved.id() == null || saved.roomId() == null) return;
        eventPublisher.publishEvent(new ChatMessageSavedEvent(saved.roomId(), saved.id(), audience));
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
     *  - 밤에 세이렌이 보낸 채팅: 같은 진행 중 게임의 세이렌 팀이고, 팀이 된 뒤에 보낸 메시지 (세이렌은 처음부터, 팀원은 유혹당한 시각부터)
     * (조회하는 사람의 프로필·게임 상태는 숨김 대상 메시지가 있을 때만 확인해 DB 조회를 줄입니다)
     */
    private final class Visibility {
        private final Room room;
        private final Long memberId;
        private boolean resolved;
        private String gameId;        // 조회하는 사람이 참가 중인 진행 중 게임 id (아니면 null)
        private Instant diedAt;       // 그 게임에서 사망한 시각 (살아 있으면 null)
        private Instant pirateSince;  // 밤 채팅을 읽을 수 있게 된 시각 (해적이 아니면 null)
        private Instant sirenSince;   // 세이렌 팀 채팅을 읽을 수 있게 된 시각 (세이렌 팀이 아니면 null)

        Visibility(Room room, Long memberId) {
            this.room = room;
            this.memberId = memberId;
        }

        boolean canSee(ChatMessage m) {
            boolean dead = m.type() == MessageType.DEAD;
            boolean pirateOnly = m.visibleToPiratesOnly();
            boolean sirenOnly = m.visibleToSirenTeamOnly();
            if (!dead && !pirateOnly && !sirenOnly) return true;
            resolve();
            if (gameId == null || !gameId.equals(m.gameId()) || m.createdAt() == null) return false;
            Instant since = dead ? diedAt : pirateOnly ? pirateSince : sirenSince;
            return since != null && !toInstant(m).isBefore(since);
        }

        private void resolve() {
            if (resolved) return;
            resolved = true;
            if (memberId == null) return;
            if (!room.isInGame() || room.getGameId() == null) return; // 게임 중이 아니면 프로필 조회도 하지 않음
            Long userId = userRepository.findByMemberId(memberId).map(User::getId).orElse(null); // DB 조회는 잠금 밖에서
            if (userId == null) return;
            withActiveGame(room, game -> {
                if (game == null) return null;
                GamePlayer me = findPlayer(game, userId);
                if (me == null) return null; // 게임 참가자가 아님 (관전자 등)
                gameId = game.getGameId();
                if (!me.isAlive()) diedAt = me.getDiedAt() != null ? me.getDiedAt() : Instant.EPOCH;
                pirateSince = pirateChatSince(me);
                sirenSince = sirenChatSince(me);
                return null;
            });
        }
    }

    /** 메시지 createdAt(서버 기본 시간대의 LocalDateTime)을 사망·접선 시각과 비교할 수 있게 Instant로 바꿉니다. */
    private static Instant toInstant(ChatMessage m) {
        return m.createdAt().atZone(ZoneId.systemDefault()).toInstant();
    }

    // ---------- 게임 상태 ----------

    /**
     * 방에서 진행 중인 게임을 게임 잠금 안에서 읽어 action에 넘깁니다. 대기 중이거나 이미 끝난 게임이면 null을 넘깁니다.
     *
     * 게임은 반드시 잠금을 잡은 뒤 저장소에서 읽습니다. Redis 저장소는 읽을 때마다 JSON에서 새 복사본을 만들기 때문에,
     * 잠금 밖에서 읽은 게임은 그사이 밤이 시작되거나 플레이어가 죽어도 알 수 없습니다.
     * 게임 진행(페이즈 전환, 사망 처리)도 같은 잠금을 쓰므로, action이 판단하는 동안에는 게임 상태가 바뀌지 않습니다.
     * action 안에서 판단까지 끝내고 결과 값만 밖으로 가지고 나갑니다. (Game 객체를 잠금 밖으로 가지고 나가지 않습니다)
     */
    private <T> T withActiveGame(Room room, Function<Game, T> action) {
        String gameId = room.getGameId();
        if (!room.isInGame() || gameId == null) return action.apply(null);
        return gameLock.withLock(gameId, () -> action.apply(
                gameRepository.findById(gameId).filter(game -> !game.isEnded()).orElse(null)));
    }

    /**
     * 전송 규칙, 그 규칙을 정한 게임 id, 볼 수 있는 사용자(모두에게 보이면 null).
     * 게임 잠금 안에서 정해 잠금 밖으로 가지고 나옵니다.
     */
    private record SendRule(Rule rule, String gameId, Set<Long> audience) {
    }

    /**
     * 지금 보낸 메시지를 볼 수 있는 사용자. 모두에게 보이면 null. (Visibility.canSee와 같은 규칙을 "보내는 순간" 기준으로 적용)
     *  - 사망자 채팅: 그 게임의 사망자 (사망한 뒤에 보낸 메시지이므로 지금 사망자는 모두 볼 수 있다)
     *  - 밤 해적 채팅·해적 안내: 지금 해적으로 취급되는 사람 (해적·요리사, 접선한 앵무새. 사망한 해적 포함)
     *  - 세이렌 채팅: 지금 세이렌 팀인 사람 (세이렌, 유혹당한 팀원. 사망한 팀원 포함)
     * withActiveGame 안(게임 잠금 안)에서만 부릅니다.
     */
    private static Set<Long> audienceOf(Game game, Rule rule) {
        Predicate<GamePlayer> canRead = switch (rule) {
            case DEAD -> p -> !p.isAlive();
            case NIGHT_PIRATE -> p -> pirateChatSince(p) != null;
            case NIGHT_SIREN -> p -> sirenChatSince(p) != null;
            case PUBLIC, NIGHT_BLOCKED -> null;
        };
        if (canRead == null) return null;
        Set<Long> ids = new LinkedHashSet<>();
        for (GamePlayer p : game.getPlayers()) {
            if (canRead.test(p)) ids.add(p.getPlayerId());
        }
        return Collections.unmodifiableSet(ids);
    }

    /**
     * 게임 중 보낸 메시지의 규칙
     *  - 사망자 → DEAD (밤에도 가능)
     *  - 밤: 해적 → NIGHT_PIRATE, 세이렌 → NIGHT_SIREN, 그 밖(유혹당한 팀원, 게임 참가자가 아닌 사람 포함) → NIGHT_BLOCKED
     *  - 그 밖 → PUBLIC
     * withActiveGame 안(게임 잠금 안)에서만 부릅니다.
     */
    private static Rule ruleOf(Game game, Long userId) {
        GamePlayer p = findPlayer(game, userId);
        if (p != null && !p.isAlive()) return Rule.DEAD;
        if (game.getPhase() == GamePhase.NIGHT) {
            if (p != null && pirateChatSince(p) != null) return Rule.NIGHT_PIRATE;
            if (p != null && p.isSiren()) return Rule.NIGHT_SIREN;
            return Rule.NIGHT_BLOCKED;
        }
        return Rule.PUBLIC;
    }

    /**
     * 세이렌 팀 채팅을 읽을 수 있게 된 시각. 세이렌 팀이 아니면 null.
     * 세이렌은 게임 시작부터(EPOCH), 유혹당한 팀원은 유혹당한 시각부터. (Game.knownSirenTeam과 같은 규칙)
     */
    private static Instant sirenChatSince(GamePlayer p) {
        if (p.getTeam() != Team.SIREN) return null;
        return p.isSiren() || p.getSeducedAt() == null ? Instant.EPOCH : p.getSeducedAt();
    }

    /**
     * 밤 채팅을 읽을 수 있게 된 시각(해적으로 취급되기 시작한 시각). 해적이 아니면 null.
     * 해적·요리사는 게임 시작부터(EPOCH), 앵무새는 해적과 접선한 시각부터. (Game.knownPirateAllies와 같은 규칙)
     */
    private static Instant pirateChatSince(GamePlayer p) {
        if (!p.isActivePirate()) return null;
        return p.isParrot() ? p.getContactedAt() : Instant.EPOCH;
    }

    /** 게임 참가자. 없으면 null (호출하는 쪽에서 게임 잠금(GameLock)을 잡은 상태) */
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
