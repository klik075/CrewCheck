package com.WhoisntCitizen_server.lobby.lock;

import java.util.function.Supplier;

/**
 * 방(roomId) 단위의 잠금.
 * Redis에서 방을 읽고 → Java에서 수정하고 → 다시 저장하는 작업(입장, 나가기, 게임 시작/종료, 복구)이
 * 같은 방에 동시에 들어오면 한쪽의 변경이 덮어써질 수 있다(lost update).
 * 같은 roomId에 대한 작업은 한 번에 하나씩만 실행되게 한다. 다른 방끼리는 서로 기다리지 않는다.
 *
 * 사용 규칙
 *  - 잠금 안에서 저장소에서 방을 다시 읽고(load), 바꿨으면 저장(save)한 뒤 잠금을 푼다.
 *    (방은 Redis에 있으므로 잠금 밖에서 읽은 Room은 이미 낡은 값일 수 있다)
 *  - 재진입이 된다. 잠금 안에서 같은 roomId로 다시 잠가도 막히지 않는다.
 *  - 잠금 순서는 "방 잠금 → 게임 잠금(GameLock)"만 허용한다. 게임 잠금 안에서 방 잠금을 잡지 않는다.
 *
 * 구현: 지금은 서버 1대 기준인 LocalRoomLock. 서버를 여러 대로 늘리면 Redis 분산 락 구현으로 바꾼다.
 */
public interface RoomLock {

    /** roomId 잠금을 잡고 action을 실행한 뒤 결과를 돌려준다. */
    <T> T withLock(Long roomId, Supplier<T> action);

    /** 반환값이 없는 작업용. */
    default void runWithLock(Long roomId, Runnable action) {
        withLock(roomId, () -> {
            action.run();
            return null;
        });
    }
}
