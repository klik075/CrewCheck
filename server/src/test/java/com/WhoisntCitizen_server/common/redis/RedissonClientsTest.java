package com.WhoisntCitizen_server.common.redis;

import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.redisson.api.RedissonClient;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * RedissonClients로 만든 연결이 실제 Redis에 붙는지 확인한다.
 * Docker가 꺼져 있으면 건너뛴다. (RedisGameRepositoryTest와 같은 방식)
 */
class RedissonClientsTest {

    private static GenericContainer<?> redis;

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redisson 연결 테스트를 건너뜁니다");
        redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redis.start();
    }

    @AfterAll
    static void stopRedis() {
        if (redis != null) {
            redis.stop();
        }
    }

    private static RedissonClient connect(int database) {
        return RedissonClients.create(redis.getHost(), redis.getMappedPort(6379), database, "");
    }

    @Test
    void 만든_연결로_값을_쓰고_읽을_수_있다() {
        RedissonClient client = connect(0);
        try {
            client.getBucket("redisson-test:hello").set("world");

            assertThat(client.<String>getBucket("redisson-test:hello").get()).isEqualTo("world");
        } finally {
            client.shutdown();
        }
    }

    @Test
    void 두_연결이_같은_Redis를_보면_같은_값을_본다() {
        // 지금 게임 그룹과 로비 그룹은 같은 Redis를 바라본다. 연결이 둘이어도 같은 키를 공유한다
        RedissonClient game = connect(0);
        RedissonClient lobby = connect(0);
        try {
            game.getBucket("redisson-test:shared").set("from-game");

            assertThat(lobby.<String>getBucket("redisson-test:shared").get()).isEqualTo("from-game");
        } finally {
            game.shutdown();
            lobby.shutdown();
        }
    }

    @Test
    void database가_다르면_키를_공유하지_않는다() {
        RedissonClient db0 = connect(0);
        RedissonClient db1 = connect(1);
        try {
            db0.getBucket("redisson-test:db").set("zero");

            assertThat(db1.<String>getBucket("redisson-test:db").get()).isNull();
        } finally {
            db0.shutdown();
            db1.shutdown();
        }
    }
}
