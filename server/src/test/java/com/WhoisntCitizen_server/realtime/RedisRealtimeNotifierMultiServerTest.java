package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import com.WhoisntCitizen_server.realtime.redis.RealtimeSignalCodec;
import com.WhoisntCitizen_server.realtime.redis.RealtimeSignalListener;
import com.WhoisntCitizen_server.realtime.redis.RedisRealtimeNotifier;
import io.lettuce.core.RedisClient;
import io.lettuce.core.RedisURI;
import io.lettuce.core.api.StatefulRedisConnection;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.connection.RedisStandaloneConfiguration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.listener.ChannelTopic;
import org.springframework.data.redis.listener.RedisMessageListenerContainer;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.function.BooleanSupplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 5단계: 서버 A에서 보낸 신호가 서버 B에 붙은 연결에 닿는지 확인하는 통합 테스트. Docker가 꺼져 있으면 건너뛴다.
 *
 * "서버"(Server)는 서버 한 대가 들고 있는 것(연결 목록, RedisRealtimeNotifier, 채널 구독)을 모은 것이다.
 * 같은 Redis를 바라보는 Server를 두 개 만들면 서버 두 대다. (RedisActivityMultiServerTest와 같은 방식)
 */
class RedisRealtimeNotifierMultiServerTest {

    private static final String CHANNEL = "realtime:test:signals";
    private static final Duration WAIT = Duration.ofSeconds(5);

    private static GenericContainer<?> redisContainer;
    private static LettuceConnectionFactory connectionFactory;
    private static StringRedisTemplate redis;
    /**
     * 구독 수(PUBSUB NUMSUB)를 묻는 전용 Lettuce 연결.
     * Spring의 RedisConnection.execute로 직접 보내면 응답을 바이트 배열 하나로만 읽어서,
     * [채널 이름, 숫자] 배열인 NUMSUB 응답을 읽지 못한다. (UnsupportedOperationException)
     */
    private static RedisClient probeClient;
    private static StatefulRedisConnection<String, String> probe;

    private final RealtimeMessageCodec messageCodec = new RealtimeMessageCodec();
    private Server serverA;
    private Server serverB;

    /** 서버 한 대 */
    private record Server(RealtimeSessionRegistry registry, RedisRealtimeNotifier notifier,
                          RedisMessageListenerContainer container) {

        FakeConnection connect(String id, long userId, Long roomId) {
            FakeConnection connection = new FakeConnection(id, userId);
            registry.register(connection);
            if (roomId != null) {
                registry.subscribe(id, roomId);
            }
            return connection;
        }

        void stop() {
            container.stop();
        }
    }

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 서버 여러 대 실시간 신호 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        connectionFactory = new LettuceConnectionFactory(
                new RedisStandaloneConfiguration(redisContainer.getHost(), redisContainer.getMappedPort(6379)));
        connectionFactory.afterPropertiesSet();
        redis = new StringRedisTemplate(connectionFactory);
        probeClient = RedisClient.create(RedisURI.create(redisContainer.getHost(), redisContainer.getMappedPort(6379)));
        probe = probeClient.connect();
    }

    @AfterAll
    static void stopRedis() {
        if (probe != null) {
            probe.close();
        }
        if (probeClient != null) {
            probeClient.shutdown();
        }
        if (connectionFactory != null) {
            connectionFactory.destroy();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    @BeforeEach
    void setUp() throws Exception {
        serverA = server("A");
        serverB = server("B");
    }

    @AfterEach
    void tearDown() {
        if (serverA != null) {
            serverA.stop();
        }
        if (serverB != null) {
            serverB.stop();
        }
        // 다음 테스트가 "구독이 등록됐다"를 앞 테스트의 구독으로 착각하지 않게, 구독이 다 풀릴 때까지 기다린다
        waitUntil(() -> subscriberCount() == 0);
    }

    private Server server(String serverId) throws Exception {
        RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
        RealtimeSignalCodec signalCodec = new RealtimeSignalCodec();
        RedisRealtimeNotifier notifier = new RedisRealtimeNotifier(new LocalRealtimeNotifier(registry, messageCodec),
                messageCodec, signalCodec, redis, CHANNEL, serverId);

        RedisMessageListenerContainer container = new RedisMessageListenerContainer();
        container.setConnectionFactory(connectionFactory);
        container.addMessageListener(new RealtimeSignalListener(notifier, signalCodec), ChannelTopic.of(CHANNEL));
        container.afterPropertiesSet();
        container.start();
        // 구독이 Redis에 등록될 때까지 기다린다. 구독 전에 발행한 메시지는 받지 못한다 (Pub/Sub은 저장하지 않음)
        waitUntil(() -> subscriberCount() >= expectedSubscribers(serverId));
        return new Server(registry, notifier, container);
    }

    private static int expectedSubscribers(String serverId) {
        return serverId.equals("A") ? 1 : 2;
    }

    /** 지금 이 채널을 구독 중인 연결 수 (PUBSUB NUMSUB) */
    private static long subscriberCount() {
        Map<String, Long> counts = probe.sync().pubsubNumsub(CHANNEL);
        return counts.getOrDefault(CHANNEL, 0L);
    }

    @Test
    void A에서_방에_보내면_B에_붙은_그_방_연결도_받는다() {
        FakeConnection chulsoo = serverA.connect("a1", 1, 3L);
        FakeConnection younghee = serverB.connect("b1", 2, 3L);
        FakeConnection otherRoom = serverB.connect("b2", 3, 4L);

        serverA.notifier().sendToRoom(3, new ServerMessage.RoomChanged(3));

        String expected = messageCodec.encode(new ServerMessage.RoomChanged(3));
        waitUntil(() -> younghee.sent.size() == 1);
        assertThat(younghee.sent).containsExactly(expected);
        assertThat(chulsoo.sent).containsExactly(expected); // 자기 연결에는 직접 보내고, 채널로 되돌아온 것은 버린다 (한 번만)
        assertThat(otherRoom.sent).isEmpty();
    }

    @Test
    void 방_안의_정해진_사용자에게만_간다() {
        FakeConnection pirateOnB = serverB.connect("b1", 1, 3L);
        FakeConnection sailorOnB = serverB.connect("b2", 2, 3L);

        serverA.notifier().sendToUsersInRoom(3, List.of(1L), new ServerMessage.ChatChanged(3, 10));

        waitUntil(() -> pirateOnB.sent.size() == 1);
        assertThat(sailorOnB.sent).isEmpty();
    }

    @Test
    void 사용자에게_보내면_다른_서버의_연결도_받는다() {
        FakeConnection onA = serverA.connect("a1", 1, null);
        FakeConnection onB = serverB.connect("b1", 1, null);

        serverA.notifier().sendToUser(1, new ServerMessage.RoomChanged(3));

        waitUntil(() -> onB.sent.size() == 1);
        assertThat(onA.sent).hasSize(1);
    }

    @Test
    void 중복_로그인_강제_종료는_다른_서버의_연결도_닫는다() {
        FakeConnection onB = serverB.connect("b1", 1, 3L);

        serverA.notifier().disconnectUser(1, 4409, "다른 곳에서 로그인");

        waitUntil(() -> onB.closedCode == 4409);
        assertThat(onB.open).isFalse();
    }

    @Test
    void 방에서_뺀_사용자는_다른_서버에서도_더_이상_신호를_받지_않는다() {
        FakeConnection onB = serverB.connect("b1", 1, 3L);

        serverA.notifier().removeUserFromRoom(3, 1);
        waitUntil(() -> serverB.registry().connectionsOfRoom(3).isEmpty());
        serverA.notifier().sendToRoom(3, new ServerMessage.RoomChanged(3));
        // 뒤에 보낸 신호까지 B가 처리했는지 확인하기 위해, 같은 방의 다른 사람을 하나 둔다
        FakeConnection witness = serverB.connect("b2", 2, 3L);
        serverA.notifier().sendToRoom(3, new ServerMessage.RoomChanged(3));
        waitUntil(() -> !witness.sent.isEmpty());

        assertThat(onB.sent).isEmpty();
    }

    @Test
    void 방을_지우면_다른_서버의_구독도_풀린다() {
        serverB.connect("b1", 1, 3L);
        serverB.connect("b2", 2, 3L);

        serverA.notifier().removeRoom(3);

        waitUntil(() -> serverB.registry().connectionsOfRoom(3).isEmpty());
    }

    private static void waitUntil(BooleanSupplier condition) {
        long deadline = System.nanoTime() + WAIT.toNanos();
        while (System.nanoTime() < deadline) {
            if (condition.getAsBoolean()) {
                return;
            }
            try {
                Thread.sleep(20);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException(e);
            }
        }
        throw new AssertionError(WAIT.toSeconds() + "초 안에 조건이 맞지 않았습니다");
    }
}
