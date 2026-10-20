package com.WhoisntCitizen_server.lobby.presence;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.lobby.config.LobbyProperties;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.lobby.service.RoomService;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Service;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * 대기실 연결 끊김 처리 (고아룸 정리).
 * [서버 여러 대] 주기 작업 — 서버마다 돈다. 두 번 돌아도 안전: 잠금 없이 미리 확인하고, 뺄 때는 방 잠금 안에서 최신 기록으로 다시 판단한다.
 *   (아래 "서버 여러 대일 때" 참고)
 *
 * 클라이언트는 대기실(Room 씬)에서 1초마다(웹소켓으로 방을 구독 중이면 15초마다) 방 조회(GET /api/v1/rooms/{roomId})를 보낸다.
 * 이 활동이 idleTimeoutSeconds(기본 45초) 동안 끊긴 참가자는 앱 종료·강제 종료·네트워크 끊김·토큰 만료 등으로 보고 방에서 뺀다.
 *  - 방장이면 남은 사람 중 가장 먼저 들어온 사람에게 위임된다. (Room.removePlayer)
 *  - 남은 사람이 있으면 퇴장 알림(RoomPlayerLeftEvent), 아무도 없으면 방 삭제(RoomDeletedEvent → 방 채팅·접속 기록 삭제)
 * 게임 중 연결 끊김은 게임 쪽 InactivePlayerMonitor가 맡는다. 이 클래스는 대기 중(WAITING)인 방만 정리한다.
 *
 * ---------------------------------------------------------------------------
 * 기록 (RoomPresenceTracker, Redis)
 * ---------------------------------------------------------------------------
 *   - 방 조회: RoomController.getRoom → touch
 *   - 방 생성·입장: RoomPlayerJoinedEvent → touch (Room 씬을 불러오는 동안 미접속으로 판정되지 않도록)
 *   - 게임 중인 방: 검사할 때마다 참가자 전원을 "지금 + 결과 조회 시간(endedRetentionSeconds)"으로 기록한다.
 *     게임이 끝나 결과 화면에 있는 동안은 방이 아니라 게임 상태를 조회하므로, 그 시간만큼 유예를 주기 위해서다.
 *     결과 화면에서 앱을 끄면 결과 조회 시간 + idleTimeout 뒤에 빠진다.
 *   - 기록이 없는 참가자(서버가 오래 꺼져 키가 만료된 경우 등): 바로 빼지 않고 검사 시점을 첫 기록으로 넣는다.
 *
 * ---------------------------------------------------------------------------
 * 검사 (checkAll, idleCheckSeconds마다)
 * ---------------------------------------------------------------------------
 *   서버가 켜진 뒤 idleTimeoutSeconds만큼 기다렸다가 시작한다.
 *   기록은 Redis에 남아 있으므로, 서버가 몇 분 꺼져 있었다면 모든 기록이 오래된 시각이다.
 *   바로 검사하면 다시 연결하려던 사람까지 전부 빠지므로, 그 사이 살아 있는 클라이언트가 조회해서 기록을 갱신할 시간을 준다.
 *
 *   방마다 (rooms 세트의 모든 방)
 *     1. 방을 읽는다. 게임이 사라진 IN_GAME 방이면 WAITING으로 복구한다. (서버 재시작 등. RoomService.loadRoomRecovered)
 *        방이 없으면 그 방의 기록 키를 지운다.
 *     2. 방에 없는 사람의 기록을 지운다. (나가기·추방된 사람)
 *     3. 게임 중이면 전원을 "지금 + 결과 조회 시간"으로 기록하고 끝.
 *     4. 기록이 없는 사람에게 지금 시각을 넣는다.
 *     5. 마지막 활동이 idleTimeout보다 오래된 사람이 있으면 RoomService.removeIdlePlayers로 뺀다.
 *        빼기 직전 방 잠금 안에서 기록을 다시 읽어 판단한다. (잠금을 기다리는 사이 들어온 조회를 반영)
 *
 * ---------------------------------------------------------------------------
 * 서버 여러 대일 때
 * ---------------------------------------------------------------------------
 *   - 기록이 Redis에 있어 모든 서버가 같은 기록을 본다.
 *   - 모든 서버가 같은 검사를 돌지만, 빼는 일은 방 잠금 안에서 최신 기록으로 다시 판단하므로
 *     실제로 빼고 이벤트를 발행하는 것은 한 서버뿐이다. (나머지는 이미 빠진 상태를 보고 아무것도 하지 않는다)
 *   - 서버 A가 쓴 시각을 서버 B의 시계로 비교한다. 서버 시계가 NTP로 맞춰져 있다고 가정한다. (1초 안팎 차이는 45초 기준에서 문제없음)
 */
@Service
public class RoomPresenceService {

    private static final Logger log = LoggerFactory.getLogger(RoomPresenceService.class);

    private final RoomPresenceTracker tracker;
    private final LobbyRoomRepository roomRepository;
    private final RoomService roomService;
    private final TaskScheduler scheduler;
    private final LobbyProperties lobbyProps;
    private final GamePhaseProperties phaseProps;
    private final Clock clock;

    public RoomPresenceService(RoomPresenceTracker tracker,
                               LobbyRoomRepository roomRepository,
                               RoomService roomService,
                               @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                               LobbyProperties lobbyProps,
                               GamePhaseProperties phaseProps,
                               Clock clock) {
        this.tracker = tracker;
        this.roomRepository = roomRepository;
        this.roomService = roomService;
        this.scheduler = scheduler;
        this.lobbyProps = lobbyProps;
        this.phaseProps = phaseProps;
        this.clock = clock;
    }

    // ------------------------------------------------------------------
    // 시작
    // ------------------------------------------------------------------

    /** 서버가 요청을 받을 준비가 되면 검사를 예약한다. 첫 검사는 idleTimeout 뒤 (재시작 직후 유예, 클래스 주석 참고) */
    @EventListener(ApplicationReadyEvent.class)
    public void start() {
        if (!lobbyProps.idleCheckEnabled()) {
            log.info("대기실 연결 끊김 처리 꺼짐 (mafia.lobby.idle-timeout-seconds={})", lobbyProps.idleTimeoutSeconds());
            return;
        }
        Instant firstCheck = clock.instant().plusSeconds(lobbyProps.idleTimeoutSeconds());
        scheduler.scheduleWithFixedDelay(this::checkAll, firstCheck, Duration.ofSeconds(lobbyProps.idleCheckSeconds()));
        log.info("대기실 연결 끊김 처리 시작: {}초 뒤부터 {}초마다 검사, {}초 동안 활동이 없으면 방에서 제외",
                lobbyProps.idleTimeoutSeconds(), lobbyProps.idleCheckSeconds(), lobbyProps.idleTimeoutSeconds());
    }

    // ------------------------------------------------------------------
    // 기록
    // ------------------------------------------------------------------

    /**
     * 방 조회 기록. RoomController.getRoom이 조회보다 먼저 호출한다.
     * 참가자인지는 확인하지 않는다. 참가자가 아닌 사람의 기록은 다음 검사(2단계)에서 지워진다.
     * 기록에 실패해도(Redis 일시 오류 등) 방 조회 자체는 계속되도록 예외를 삼킨다.
     */
    public void touch(Long roomId, Long userId) {
        if (!lobbyProps.idleCheckEnabled()) return;
        try {
            tracker.touch(roomId, userId, clock.instant());
        } catch (RuntimeException e) {
            log.warn("방 {}: 대기실 접속 기록 실패 (userId={})", roomId, userId, e);
        }
    }

    /** 방 생성·입장 직후 기록. (Room 씬을 불러오는 동안 미접속으로 판정되지 않도록) */
    @EventListener
    public void onPlayerJoined(RoomPlayerJoinedEvent event) {
        touch(event.roomId(), event.userId());
    }

    /** 방이 삭제되면(나가기·퇴출·취소된 게임 등 모든 경로) 그 방의 기록 키도 지운다. 놓쳐도 TTL로 결국 사라진다. */
    @EventListener
    public void onRoomDeleted(RoomDeletedEvent event) {
        try {
            tracker.delete(event.roomId());
        } catch (RuntimeException e) {
            log.warn("방 {}: 대기실 접속 기록 삭제 실패 (TTL로 정리됨)", event.roomId(), e);
        }
    }

    // ------------------------------------------------------------------
    // 검사
    // ------------------------------------------------------------------

    /**
     * 모든 방 검사. 반복 작업에서 예외가 밖으로 나가면 이후 검사가 멈추므로 방마다 예외를 잡는다.
     * (InactivePlayerMonitor.checkAll과 같은 방식)
     */
    public void checkAll() {
        if (!lobbyProps.idleCheckEnabled()) return; // 꺼져 있으면 기록도 없으므로 검사하지 않는다

        Set<String> ids;
        try {
            ids = roomRepository.findAllRoomIds();
        } catch (RuntimeException e) {
            log.error("대기실 연결 끊김 검사 실패: 방 목록을 읽지 못함", e);
            return;
        }
        if (ids == null) return;

        for (String id : ids) {
            Long roomId = parseRoomId(id);
            if (roomId == null) continue;
            try {
                check(roomId);
            } catch (LockTimeoutException e) {
                // 방(또는 게임)이 바빠 이번 검사를 건너뜀. 다음 검사(idleCheckSeconds 뒤)에서 다시 확인한다
                log.debug("방 {}: 잠금이 바빠 이번 대기실 검사를 건너뜁니다", roomId);
            } catch (RuntimeException e) {
                log.error("방 {}: 대기실 연결 끊김 검사 실패", roomId, e);
            }
        }
    }

    /** 방 하나 검사. 단계는 클래스 주석의 "검사" 참고 */
    private void check(Long roomId) {
        // 1. 방 읽기 (게임이 사라진 IN_GAME 방은 여기서 WAITING으로 복구된다)
        Room room = roomService.loadRoomRecovered(roomId);
        if (room == null) {
            tracker.delete(roomId);
            return;
        }

        Instant now = clock.instant();
        List<Long> userIds = room.getPlayers().stream().map(RoomPlayer::getUserId).toList();
        Map<Long, Instant> lastSeen = tracker.lastSeen(roomId);

        // 2. 방에 없는 사람의 기록 정리
        List<Long> leftUsers = lastSeen.keySet().stream().filter(id -> !userIds.contains(id)).toList();
        tracker.remove(roomId, leftUsers);

        // 3. 게임 중: 빼지 않고 결과 화면 유예만 미리 넣어 둔다 (게임 중 연결 끊김은 게임 쪽 담당)
        if (room.isInGame()) {
            tracker.markAll(roomId, userIds, now.plusSeconds(Math.max(0, phaseProps.endedRetentionSeconds())));
            return;
        }

        // 4. 기록이 없는 사람은 지금부터 센다
        List<Long> missing = userIds.stream().filter(id -> !lastSeen.containsKey(id)).toList();
        tracker.initMissing(roomId, missing, now);

        // 5. 오래된 사람이 있을 때만 방 잠금을 잡고 뺀다 (대부분의 방은 여기서 끝나 잠금을 잡지 않는다)
        Instant cutoff = now.minusSeconds(lobbyProps.idleTimeoutSeconds());
        if (idleUserIds(lastSeen, cutoff).isEmpty()) return;

        // 잠금 안에서 기록을 다시 읽어 판단한다 (잠금을 기다리는 사이 들어온 조회를 반영)
        List<Long> removed = roomService.removeIdlePlayers(roomId, () -> idleUserIds(tracker.lastSeen(roomId), cutoff));
        if (!removed.isEmpty()) {
            tracker.remove(roomId, removed); // 방이 삭제됐다면 onRoomDeleted가 키를 통째로 지운다
            log.info("방 {}: 대기실 활동이 {}초 넘게 없어 {} 제외", roomId, lobbyProps.idleTimeoutSeconds(), removed);
        }
    }

    /** 마지막 활동이 cutoff보다 이전인 사람. 기록이 없는 사람은 포함하지 않는다. (딱 cutoff인 사람도 아직 포함하지 않음) */
    private static Set<Long> idleUserIds(Map<Long, Instant> lastSeen, Instant cutoff) {
        return lastSeen.entrySet().stream()
                .filter(e -> e.getValue().isBefore(cutoff))
                .map(Map.Entry::getKey)
                .collect(Collectors.toSet());
    }

    private static Long parseRoomId(String id) {
        try {
            return Long.valueOf(id);
        } catch (NumberFormatException e) {
            return null;
        }
    }
}
