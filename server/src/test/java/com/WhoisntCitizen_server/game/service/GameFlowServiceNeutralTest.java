package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/** 제3 세력이 있는 게임 진행: 인어 처형 승리, 크라켄 발동 안내, 유령 선장 습격 안내. */
class GameFlowServiceNeutralTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition KRAKEN =
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK);
    private static final RoleDefinition GHOST =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition MERMAID =
            new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-07T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private InMemoryGameRepository repository;
    private final List<Object> events = new ArrayList<>();
    private GameFlowService flow;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new InMemoryGameRepository();
        flow = TestGameFlows.create(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), scheduler, PROPS, clock, events::add);
    }

    /** roles 순서대로 playerId 1, 2, 3... (닉네임 p1, p2, ...)로 게임을 시작해 첫 밤으로 들어간다. */
    private Game start(RoleDefinition... roles) {
        List<GamePlayer> players = new ArrayList<>();
        for (int i = 0; i < roles.length; i++) {
            players.add(new GamePlayer((long) (i + 1), "p" + (i + 1), roles[i]));
        }
        Game game = new Game("1", players, true);
        repository.save(game);
        flow.begin(game.getGameId());
        return game;
    }

    /** 페이즈 타이머를 seconds초씩 차례로 흘려보낸다. */
    private void pass(int... seconds) {
        for (int s : seconds) {
            scheduler.advance(Duration.ofSeconds(s));
        }
    }

    private List<String> notices() {
        return events.stream().filter(RoomNoticeEvent.class::isInstance)
                .map(e -> ((RoomNoticeEvent) e).message()).toList();
    }

    @Test
    void 인어가_처형되면_게임이_끝나고_인어만_승리로_기록된다() {
        Game game = start(RAIDER, MERMAID, SAILOR, SAILOR, SAILOR);
        pass(30, 5, 60); // 밤 → 결과 → 낮 → 투표
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        game.recordVote(1L, 2L);
        game.recordVote(3L, 2L);
        game.recordVote(4L, 2L);

        flow.resolveVote(game);
        scheduler.runDue();

        assertThat(game.isEnded()).isTrue();
        assertThat(game.getWinner()).isEqualTo(Winner.MERMAID);
        assertThat(game.getWinnerIds()).containsExactly(2L);
        assertThat(notices()).contains("처형된 사람은 인어였습니다. 인어가 승리했습니다!");
        GameEndedEvent ended = events.stream().filter(GameEndedEvent.class::isInstance)
                .map(GameEndedEvent.class::cast).findFirst().orElseThrow();
        assertThat(ended.outcomes()).filteredOn(GameEndedEvent.PlayerOutcome::win)
                .extracting(GameEndedEvent.PlayerOutcome::userId).containsExactly(2L);
    }

    @Test
    void 크라켄이_발동한_밤은_사망자마다_원인을_알리고_게임이_이어진다() {
        Game game = start(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L); // 첫 밤: 3번에 표식
        flow.resolveNight(game);
        pass(5, 60, 30, 5); // 결과 → 낮 → 투표(아무도 안 함) → 처형 → 둘째 밤
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);

        game.recordNightAction(2L, ActionCode.KRAKEN_STRIKE, null, clock.instant());
        game.recordNightAction(1L, 4L);
        flow.resolveNight(game);

        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(game.getLastNightResult().deaths()).containsExactly(
                new NightResult.Death(4L, DeathCause.ATTACK), new NightResult.Death(3L, DeathCause.KRAKEN));
        assertThat(notices()).contains(
                "지난밤 p4님이 해적의 습격을 받아 사망했습니다. 지난밤 p3님이 크라켄에게 끌려가 사망했습니다.");
    }

    @Test
    void 유령_선장이_습격당한_밤은_아무_일도_없던_것처럼_알린다() {
        Game game = start(RAIDER, GHOST, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(1L, 2L);

        flow.resolveNight(game);

        assertThat(game.getPlayer(2L).isAlive()).isTrue();
        assertThat(notices()).contains("지난밤은 아무 일도 일어나지 않았습니다.");
    }
}
