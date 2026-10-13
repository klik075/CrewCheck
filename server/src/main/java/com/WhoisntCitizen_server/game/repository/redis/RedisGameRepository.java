package com.WhoisntCitizen_server.game.repository.redis;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import org.springframework.dao.DataAccessException;
import org.springframework.data.redis.core.RedisOperations;
import org.springframework.data.redis.core.SessionCallback;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.time.Clock;
import java.time.Duration;
import java.util.List;
import java.util.Optional;
import java.util.Set;

/**
 * Redis에 게임 상태를 저장하는 GameRepository. 게임 그룹 Redis(GameRedis)를 쓴다.
 *
 * 키 구조 (gameId = abc 인 경우)
 *  - game:v1:state:abc          (String)     게임 스냅샷 JSON (GameSnapshotCodec). TTL: 진행 중 activeTtl, 끝나면 endedTtl
 *  - game:v1:active-expiry      (Sorted Set) member = 끝나지 않은 게임 id, score = 그 게임 상태 키가 만료되는 시각(epoch ms)
 *  키 이름의 v1은 키 구조 버전이다. JSON 안의 저장 형식 버전은 GameSnapshot.schemaVersion이 따로 관리한다.
 *
 * 진행 중 목록을 Set이 아니라 "만료 시각을 점수로 둔 Sorted Set"으로 두는 이유
 *  Redis TTL은 키 단위라 Set의 원소에는 걸 수 없다. Set이면 상태 키가 TTL로 사라져도 id가 목록에 영원히 남는다.
 *  상태 키와 같은 만료 시각을 점수로 함께 저장하면
 *   - 조회(findActiveIds)는 "만료 시각이 아직 안 지난 id"만 꺼내므로 사라진 게임이 나오지 않고
 *   - 만료된 id는 저장할 때마다 ZREMRANGEBYSCORE 한 번으로 지워져 쌓이지 않는다.
 *
 * 상태 키·목록·청소는 MULTI/EXEC로 함께 반영한다. 둘 중 하나만 바뀐 상태가 남지 않게 하기 위해서다.
 * 여러 서버가 같은 게임을 동시에 고치는 것은 막지 않는다. 그건 게임 잠금(GameLock, 2단계 분산 잠금)이 맡는다.
 *
 * 만료 시각(점수)은 게임 서버 시계, 상태 키 TTL은 Redis 시계로 센다. 시계가 몇 초 어긋나면 그 사이에
 * 목록에는 있는데 상태 키는 없는 순간이 생길 수 있다. 그래서 findActiveIds를 받은 쪽은 findById가 비었으면 건너뛴다.
 * (GameRepository.findActiveIds 설명 참고. 목록을 받은 직후 게임이 끝나거나 삭제되는 경우도 같은 규칙으로 처리된다)
 */
public class RedisGameRepository implements GameRepository {

    static final String KEY_PREFIX = "game:v1:";
    static final String ACTIVE_KEY = KEY_PREFIX + "active-expiry";

    private final StringRedisTemplate redis;
    private final GameSnapshotCodec codec;
    private final Clock clock;
    private final Duration activeTtl;
    private final Duration endedTtl;

    public RedisGameRepository(GameRedis gameRedis, GameSnapshotCodec codec, GameRedisProperties props, Clock clock) {
        this.redis = gameRedis.template();
        this.codec = codec;
        this.clock = clock;
        this.activeTtl = Duration.ofSeconds(props.activeTtlSeconds());
        this.endedTtl = Duration.ofSeconds(props.endedTtlSeconds());
    }

    static String stateKey(String gameId) {
        return KEY_PREFIX + "state:" + gameId;
    }

    @Override
    public Game save(Game game) {
        String gameId = game.getGameId();
        String json = codec.encode(game);
        boolean ended = game.isEnded();
        long now = clock.millis();
        long expiresAt = now + activeTtl.toMillis();
        redis.execute(new SessionCallback<List<Object>>() {
            @Override
            @SuppressWarnings("unchecked")
            public <K, V> List<Object> execute(RedisOperations<K, V> operations) throws DataAccessException {
                RedisOperations<String, String> ops = (RedisOperations<String, String>) operations;
                ops.multi();
                ops.opsForValue().set(stateKey(gameId), json, ended ? endedTtl : activeTtl);
                if (ended) {
                    ops.opsForZSet().remove(ACTIVE_KEY, gameId);
                } else {
                    ops.opsForZSet().add(ACTIVE_KEY, gameId, expiresAt); // 저장할 때마다 만료 시각도 같이 늘어난다
                }
                ops.opsForZSet().removeRangeByScore(ACTIVE_KEY, Double.NEGATIVE_INFINITY, now); // 만료된 id 청소
                return ops.exec();
            }
        });
        return game;
    }

    /**
     * 저장된 JSON에서 새 Game을 만든다. 호출할 때마다 다른 객체다.
     * JSON이 깨졌거나 모르는 저장 형식 버전이면 예외를 던진다. (조용히 없는 게임으로 취급하지 않는다)
     */
    @Override
    public Optional<Game> findById(String gameId) {
        String json = redis.opsForValue().get(stateKey(gameId));
        return Optional.ofNullable(json).map(codec::decode);
    }

    /** 만료 시각이 아직 지나지 않은 게임 id만 돌려준다. (만료된 id가 목록에 남아 있어도 나오지 않는다) */
    @Override
    public List<String> findActiveIds() {
        Set<String> ids = redis.opsForZSet().rangeByScore(ACTIVE_KEY, clock.millis(), Double.POSITIVE_INFINITY);
        return ids == null ? List.of() : List.copyOf(ids);
    }

    @Override
    public void delete(String gameId) {
        redis.execute(new SessionCallback<List<Object>>() {
            @Override
            @SuppressWarnings("unchecked")
            public <K, V> List<Object> execute(RedisOperations<K, V> operations) throws DataAccessException {
                RedisOperations<String, String> ops = (RedisOperations<String, String>) operations;
                ops.multi();
                ops.delete(stateKey(gameId));
                ops.opsForZSet().remove(ACTIVE_KEY, gameId);
                return ops.exec();
            }
        });
    }
}
