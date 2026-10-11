package com.WhoisntCitizen_server.game.lock;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * 지금 이 스레드가 게임 잠금을 쥐고 있는지 기억하고, "잠금이 풀린 뒤에 할 일"을 모아 두었다가 실행한다.
 * (DB 트랜잭션의 "커밋 후에 실행"과 같은 생각)
 *
 * 왜 필요한가
 *  - 게임 잠금은 같은 게임의 모든 요청·타이머가 줄 서는 곳이다. 잠금 안에서 느린 일(채팅 저장, 이후 WebSocket 전송)을 하면
 *    그 시간만큼 다른 요청이 기다리고, Redis가 느려지면 잠금 대기 시간(10초)을 넘겨 LockTimeoutException이 난다.
 *  - 그래서 상태 변경·저장만 잠금 안에서 하고, 안내 메시지 같은 "알리는 일"은 afterUnlock()으로 넘겨 잠금이 풀린 뒤 실행한다.
 *
 * 동작
 *  - afterUnlock(action): 잠금 안이면 모아 두었다가 가장 바깥 잠금이 풀린 직후 같은 스cl레드에서 넣은 순서대로 실행한다.
 *    잠금 밖이면 바로 실행한다. 같은 스레드에서 바로 이어서 실행하므로 API 응답 전에 끝나고, 순서도 바뀌지 않는다.
 *  - 작업이 예외로 끝나도 모아 둔 일은 실행한다. (예전처럼 바로 실행하던 것과 결과가 같도록)
 *  - 모아 둔 일 하나가 실패해도 나머지는 계속 실행하고 로그만 남긴다. (알리는 일이 게임 진행을 막지 않게)
 *  - isHeld(): 방 잠금이 "게임 잠금 안에서 방 잠금"(잠금 순서 위반)을 막을 때 쓴다. (requireNotHeld)
 *
 * enter()/exit()는 게임 잠금 구현(LocalGameLock, RedisGameLock)만 부른다. 잠금을 잡은 직후 enter, 푼 직후 exit.
 * 스레드마다 따로 기억하므로(ThreadLocal) 다른 스레드에는 영향이 없다.
 */
public final class GameLockScope {

    private static final Logger log = LoggerFactory.getLogger(GameLockScope.class);

    private static final ThreadLocal<Scope> CURRENT = new ThreadLocal<>();

    private GameLockScope() {
    }

    /** 이 스레드가 게임 잠금을 하나라도 쥐고 있으면 true */
    public static boolean isHeld() {
        Scope scope = CURRENT.get();
        return scope != null && scope.depth > 0;
    }

    /** 게임 잠금 안이면 가장 바깥 잠금이 풀린 뒤에, 밖이면 바로 실행한다. */
    public static void afterUnlock(Runnable action) {
        Objects.requireNonNull(action, "action");
        Scope scope = CURRENT.get();
        if (scope == null || scope.depth == 0) {
            runSafely(action);
            return;
        }
        scope.afterUnlock.add(action);
    }

    /**
     * 게임 잠금을 쥔 채로 다른 잠금(방 잠금)을 잡으려 하면 막는다.
     * 잠금 순서는 "방 잠금 → 게임 잠금"만 허용한다. 반대 순서가 섞이면 두 스레드(또는 두 서버)가 서로를 기다리며 멈춘다.
     */
    public static void requireNotHeld(String lockName, Object key) {
        if (isHeld()) {
            throw new IllegalStateException("잠금 순서 위반: 게임 잠금을 쥔 채로 " + lockName + " 잠금(key=" + key
                    + ")을 잡으려 했습니다. \"" + lockName + " 잠금 → 게임 잠금\" 순서만 허용합니다. "
                    + "게임 잠금 안의 일은 GameLockScope.afterUnlock이나 DeferredEventPublisher로 잠금 밖으로 넘기세요.");
        }
    }

    /** 게임 잠금 구현용: 잠금을 잡은 직후 부른다. (재진입이면 깊이만 늘어난다) */
    public static void enter() {
        Scope scope = CURRENT.get();
        if (scope == null) {
            scope = new Scope();
            CURRENT.set(scope);
        }
        scope.depth++;
    }

    /** 게임 잠금 구현용: 잠금을 푼 직후 부른다. 가장 바깥 잠금이 풀렸으면 모아 둔 일을 실행한다. */
    public static void exit() {
        Scope scope = CURRENT.get();
        if (scope == null) {
            return;
        }
        scope.depth--;
        if (scope.depth > 0) {
            return;
        }
        // 먼저 비운다. 모아 둔 일이 다시 게임 잠금을 잡으면 새 범위로 시작한다.
        CURRENT.remove();
        for (Runnable action : scope.afterUnlock) {
            runSafely(action);
        }
    }

    private static void runSafely(Runnable action) {
        try {
            action.run();
        } catch (RuntimeException e) {
            log.warn("게임 잠금이 풀린 뒤 실행할 일이 실패했습니다: {}", e.getMessage(), e);
        }
    }

    private static final class Scope {
        private int depth;
        private final List<Runnable> afterUnlock = new ArrayList<>();
    }
}
