package com.WhoisntCitizen_server.game.repository.redis;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.GameRepositoryContractTest;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 1-7: RedisGameRepository를 실제 Redis로 확인하는 통합 테스트.
 *
 * 테스트가 시작될 때 Testcontainers가 Docker에 Redis 컨테이너를 띄우고, 끝나면 지운다.
 * Docker가 꺼져 있으면 이 클래스의 테스트는 실패가 아니라 "건너뜀(skipped)"으로 끝난다.
 * → Docker 없이도 ./gradlew test는 통과한다. Redis 쪽 코드를 고쳤다면 Docker를 켜고 돌려서 passed인지 확인한다.
 *
 * GameRepositoryContractTest를 상속하므로 메모리·JSON 저장소와 같은 동작을 실제 Redis에서도 확인한다.
 * 아래 테스트는 Redis 구현에만 있는 것(키 구조, TTL, 만료 시각 목록)을 확인한다.
 */
class RedisGameRepositoryTest extends GameRepositoryContractTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");
    private static final long ACTIVE_TTL = 21600;
    private static final long ENDED_TTL = 600;

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;
    private static GameRedisProperties props;

    private MutableClock clock;

    @BeforeAll
    static void startRedis() {
        // Docker가 없으면 여기서 멈추고 이 클래스의 테스트를 모두 건너뛴다
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 통합 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        props = new GameRedisProperties(redisContainer.getHost(), redisContainer.getMappedPort(6379), 0, "",
                ACTIVE_TTL, ENDED_TTL);
        gameRedis = new GameRedis(props);
    }

    @AfterAll
    static void stopRedis() {
        if (gameRedis != null) {
            gameRedis.destroy();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    /** 테스트마다 빈 Redis에서 시작한다. (부모의 @BeforeEach가 매 테스트 전에 부른다) */
    @Override
    protected GameRepository newRepository() {
        redis().getConnectionFactory().getConnection().serverCommands().flushDb();
        clock = new MutableClock(NOW);
        return new RedisGameRepository(gameRedis, new GameSnapshotCodec(), props, clock);
    }

    private static StringRedisTemplate redis() {
        return gameRedis.template();
    }

    private static Game startedGame() {
        Game game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원", TestRoles.SAILOR)), true);
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        return game;
    }

    // ---------- 키 구조 ----------

    @Test
    void 게임은_상태_키에_스냅샷_JSON으로_저장된다() {
        Game game = startedGame();
        repository.save(game);

        String json = redis().opsForValue().get(RedisGameRepository.stateKey(game.getGameId()));

        assertThat(json).contains("\"schemaVersion\":1", "\"gameId\":\"" + game.getGameId() + "\"");
    }

    @Test
    void 진행_중인_게임은_목록에_만료_시각과_함께_들어간다() {
        Game game = startedGame();
        repository.save(game);

        Double score = redis().opsForZSet().score(RedisGameRepository.ACTIVE_KEY, game.getGameId());

        assertThat(score).isEqualTo((double) NOW.plusSeconds(ACTIVE_TTL).toEpochMilli());
    }

    // ---------- TTL ----------

    @Test
    void 진행_중인_게임에는_진행_중_TTL이_걸린다() {
        Game game = startedGame();
        repository.save(game);

        Long ttl = redis().getExpire(RedisGameRepository.stateKey(game.getGameId()));

        assertThat(ttl).isBetween(ACTIVE_TTL - 5, ACTIVE_TTL);
    }

    @Test
    void 끝난_게임에는_짧은_TTL이_걸리고_목록에서_빠진다() {
        Game game = startedGame();
        repository.save(game);
        Game loaded = repository.findById(game.getGameId()).orElseThrow();
        loaded.end(Winner.CREW, List.of(2L, 3L));
        repository.save(loaded);

        Long ttl = redis().getExpire(RedisGameRepository.stateKey(game.getGameId()));

        assertThat(ttl).isBetween(ENDED_TTL - 5, ENDED_TTL);
        assertThat(redis().opsForZSet().score(RedisGameRepository.ACTIVE_KEY, game.getGameId())).isNull();
    }

    @Test
    void 저장할_때마다_만료_시각이_늘어난다() {
        Game game = startedGame();
        repository.save(game);

        clock.advance(Duration.ofHours(1));
        repository.save(game);

        Double score = redis().opsForZSet().score(RedisGameRepository.ACTIVE_KEY, game.getGameId());
        assertThat(score).isEqualTo((double) NOW.plus(Duration.ofHours(1)).plusSeconds(ACTIVE_TTL).toEpochMilli());
    }

    // ---------- 만료 시각이 지난 게임 ----------

    @Test
    void 만료_시각이_지난_게임은_목록에_나오지_않는다() {
        Game game = startedGame();
        repository.save(game);

        clock.advance(Duration.ofSeconds(ACTIVE_TTL + 1));   // 6시간 동안 저장되지 않은 게임

        assertThat(repository.findActiveIds()).doesNotContain(game.getGameId());
    }

    @Test
    void 만료_시각이_지난_id는_다른_게임을_저장할_때_청소된다() {
        Game abandoned = startedGame();
        repository.save(abandoned);

        clock.advance(Duration.ofSeconds(ACTIVE_TTL + 1));
        Game other = startedGame();
        repository.save(other);

        assertThat(redis().opsForZSet().score(RedisGameRepository.ACTIVE_KEY, abandoned.getGameId())).isNull();
        assertThat(repository.findActiveIds()).containsExactly(other.getGameId());
    }

    // ---------- 삭제 ----------

    @Test
    void 삭제하면_상태_키와_목록에서_모두_사라진다() {
        Game game = startedGame();
        repository.save(game);

        repository.delete(game.getGameId());

        assertThat(redis().hasKey(RedisGameRepository.stateKey(game.getGameId()))).isFalse();
        assertThat(redis().opsForZSet().score(RedisGameRepository.ACTIVE_KEY, game.getGameId())).isNull();
    }
}
