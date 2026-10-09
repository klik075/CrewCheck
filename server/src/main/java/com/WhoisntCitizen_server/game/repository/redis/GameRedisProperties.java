package com.WhoisntCitizen_server.game.repository.redis;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * 게임 그룹 Redis 설정 (mafia.redis.game.*).
 *
 * Redis를 용도별로 묶는다: 게임 상태·게임 잠금(2단계)·게임 타이머(3단계)처럼 함께 바뀌어야 하는 키는 이 그룹에 둔다.
 * 지금은 다른 그룹(로비·채팅, spring.data.redis.*)과 같은 Redis를 바라본다.
 * 나중에 게임용 Redis를 따로 두려면 host·port만 바꾸면 되고 코드는 그대로다.
 *
 * TTL은 정리 작업이 돌지 못했을 때(서버가 죽는 등) 키가 영원히 남지 않게 하는 안전장치다.
 * 정상적인 삭제는 종료 후 보관 시간(mafia.phase.ended-retention-seconds)이 지나 GameTimer가 한다.
 */
@ConfigurationProperties(prefix = "mafia.redis.game")
public record GameRedisProperties(
        @DefaultValue("localhost") String host,
        @DefaultValue("6379") int port,
        @DefaultValue("0") int database,
        // 비어 있으면 비밀번호 없이 접속
        @DefaultValue("") String password,
        // 끝나지 않은 게임 키의 TTL. 저장할 때마다 다시 늘어난다. 한 판이 이보다 오래 갱신되지 않으면 버려진 게임으로 본다
        @DefaultValue("21600") long activeTtlSeconds,
        // 끝난 게임 키의 TTL. 종료 후 보관 시간(ended-retention-seconds)보다 길어야 한다
        @DefaultValue("600") long endedTtlSeconds
) {
}
