package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.lock.GameLockScope;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 2-8: 게임 진행 중 나가는 채팅 안내(RoomNoticeEvent)와 로비·전적 이벤트가 게임 잠금이 풀린 뒤에 발행되는지 확인한다.
 * 채팅 저장(이후 WebSocket 전송)이 느려도 같은 게임의 다른 요청이 그만큼 기다리지 않게 하기 위해서다.
 * 순서도 예전과 같아야 한다. (안내 문장이 뒤섞이면 안 된다)
 */
class AnnounceAfterUnlockTest {

    private static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);

    /** 발행된 이벤트와, 발행될 때 그 스레드가 게임 잠금을 쥐고 있었는지 */
    private record Published(Object event, boolean insideGameLock) {
    }

    private final List<Published> published = new ArrayList<>();
    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private CopyingGameRepository repository;
    private GameFlowService flow;
    private String gameId;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new CopyingGameRepository();
        flow = TestGameFlows.create(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), scheduler, PROPS, clock,
                event -> published.add(new Published(event, GameLockScope.isHeld())));
        gameId = repository.save(new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true)).getGameId();
    }

    private List<String> notices() {
        return published.stream()
                .filter(p -> p.event() instanceof RoomNoticeEvent)
                .map(p -> ((RoomNoticeEvent) p.event()).message())
                .toList();
    }

    @Test
    void 페이즈_안내는_게임_잠금이_풀린_뒤에_발행된다() {
        flow.begin(gameId);

        assertThat(notices()).hasSize(1);
        assertThat(notices().get(0)).contains("1일차 밤");
        assertThat(published).allSatisfy(p -> assertThat(p.insideGameLock()).isFalse());
    }

    @Test
    void 한_번에_여러_안내가_나와도_순서가_그대로다() {
        flow.begin(gameId);
        published.clear();

        scheduler.advance(Duration.ofSeconds(30));   // 밤 시간 초과 → 밤 결과

        assertThat(notices()).hasSize(3);
        assertThat(notices().get(0)).isEqualTo("시간이 다 되어 다음 단계로 넘어갑니다.");
        assertThat(notices().get(1)).isEqualTo("날이 밝았습니다. 지난밤의 결과를 확인해 주세요.");
        assertThat(notices().get(2)).startsWith("지난밤");              // 밤 결과 (예전과 같은 순서)
        assertThat(published).allSatisfy(p -> assertThat(p.insideGameLock()).isFalse());
    }

    @Test
    void 게임_종료_이벤트도_잠금이_풀린_뒤_스케줄러에서_발행되고_승리_안내_뒤에_온다() {
        flow.begin(gameId);
        // 아무도 행동·투표하지 않아 정해진 일수 동안 사망자가 없으면 취소되는 경로로 종료를 만든다
        for (int day = 0; day < PROPS.maxDaysWithoutDeath() + 1 && !repository.findById(gameId).orElseThrow().isEnded(); day++) {
            scheduler.advance(Duration.ofSeconds(30 + 5 + 60 + 30 + 5));
        }
        scheduler.runDue();
        assertThat(repository.findById(gameId).orElseThrow().isEnded()).isTrue();

        int cancelNotice = -1;
        int endedEvent = -1;
        for (int i = 0; i < published.size(); i++) {
            Object event = published.get(i).event();
            if (event instanceof RoomNoticeEvent notice && notice.message().contains("게임이 취소되었습니다")) {
                cancelNotice = i;
            }
            if (event instanceof GameEndedEvent) {
                endedEvent = i;
            }
        }
        assertThat(cancelNotice).isNotNegative();
        assertThat(endedEvent).isGreaterThan(cancelNotice);
        assertThat(published).allSatisfy(p -> assertThat(p.insideGameLock()).isFalse());
    }
}
