package com.WhoisntCitizen_server.game.lock;

import java.util.function.Supplier;

/**
 * 게임 한 판(gameId) 단위의 잠금. 같은 게임의 제출·투표·타이머·조회가 동시에 상태를 바꾸지 않게 한다.
 * 다른 게임끼리는 서로 기다리지 않는다.
 *
 * 사용 규칙
 *  - 잠금 안에서 저장소에서 게임을 불러오고(load), 바꿨으면 저장(save)한 뒤 잠금을 푼다.
 *    (게임을 Redis에 두게 되면 잠금 밖에서 불러온 객체는 이미 낡은 값일 수 있다)
 *  - 재진입이 된다. 잠금 안에서 같은 gameId로 다시 잠가도 막히지 않는다.
 *  - 잠금 순서는 "방 잠금 → 게임 잠금"만 허용한다. 게임 잠금 안에서 방 잠금을 잡지 않는다.
 */
public interface GameLock {

    /** gameId 잠금을 잡고 action을 실행한 뒤 결과를 돌려준다. */
    <T> T withLock(String gameId, Supplier<T> action);

    /** 반환값이 없는 작업용. */
    default void runWithLock(String gameId, Runnable action) {
        withLock(gameId, () -> {
            action.run();
            return null;
        });
    }
}
