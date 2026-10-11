package com.WhoisntCitizen_server.lobby.lock;

import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.GameLockScope;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 2-8: 잠금 순서는 "방 잠금 → 게임 잠금"만 허용한다.
 * 반대 순서(게임 잠금을 쥔 채 방 잠금)가 섞이면, 한 스레드는 방 → 게임, 다른 스레드는 게임 → 방을 기다리며
 * 둘 다 영원히(대기 시간까지) 멈출 수 있다. 서버 여러 대에서는 Redis 잠금이라 같은 일이 서버 사이에서 생긴다.
 * 방 잠금이 잡히기 전에 검사해 바로 막는다. (Redis 방 잠금도 같은 검사를 한다: RedisRoomLock)
 */
class LockOrderTest {

    private final GameLock gameLock = new LocalGameLock();
    private final RoomLock roomLock = new LocalRoomLock();
    private final List<String> log = new ArrayList<>();

    @Test
    void 방_잠금_안에서_게임_잠금을_잡는_것은_허용한다() {
        // 게임 시작(RoomService.startGame → GameService.startGame), 고아 방 복구(keepsRoomInGame)가 이 순서다
        String result = roomLock.withLock(1L, () -> gameLock.withLock("g1", () -> "ok"));

        assertThat(result).isEqualTo("ok");
    }

    @Test
    void 게임_잠금_안에서_방_잠금을_잡으면_막고_작업은_실행하지_않는다() {
        assertThatThrownBy(() -> gameLock.runWithLock("g1", () ->
                roomLock.runWithLock(1L, () -> log.add("실행되면 안 됨"))))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("잠금 순서 위반")
                .hasMessageContaining("key=1");

        assertThat(log).isEmpty();
    }

    @Test
    void 방_잠금_안에서_게임_잠금을_잡고_그_안에서_다시_방_잠금을_잡아도_막는다() {
        assertThatThrownBy(() -> roomLock.runWithLock(1L, () -> gameLock.runWithLock("g1", () ->
                roomLock.runWithLock(1L, () -> log.add("실행되면 안 됨")))))
                .isInstanceOf(IllegalStateException.class);

        assertThat(log).isEmpty();
    }

    @Test
    void 게임_잠금이_풀린_뒤에_할_일로_넘기면_방_잠금을_잡을_수_있다() {
        gameLock.runWithLock("g1", () -> GameLockScope.afterUnlock(() ->
                roomLock.runWithLock(1L, () -> log.add("방 잠금 잡음"))));

        assertThat(log).containsExactly("방 잠금 잡음");
    }
}
