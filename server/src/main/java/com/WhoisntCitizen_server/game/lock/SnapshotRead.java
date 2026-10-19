package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.game.repository.GameRepository;

import java.util.Objects;
import java.util.function.Supplier;

/**
 * 화면에 보여 주기만 하는 게임 읽기. 저장소가 복사본을 돌려주면 게임 잠금 없이 읽는다.
 *
 * 왜 필요한가
 *  - 게임 잠금은 같은 게임의 요청이 한 줄로 서는 곳이다. 상태 조회처럼 요청이 가장 많은 읽기까지 이 줄에 서면,
 *    투표가 몰리는 순간 조회도 함께 밀리고, 기다리는 동안 요청 스레드를 붙잡는다. (부하 테스트: 방 10개에서 Tomcat 스레드 200개 소진)
 *  - Redis 저장소는 GET 한 번으로 게임 전체(JSON 하나)를 읽는다. 저장은 키 하나를 통째로 바꾸므로,
 *    잠금 없이 읽어도 "저장이 끝난 어느 한 시점"의 온전한 게임을 받는다. 반쯤 바뀐 상태는 보이지 않는다.
 *
 * 쓰는 곳 / 쓰지 않는 곳
 *  - 쓴다: 읽은 값으로 응답만 만드는 곳 (상태 조회, 내 역할, 밤·처형·최종 결과)
 *  - 쓰지 않는다: 읽은 값으로 판단해서 저장하거나 다른 일을 시작하는 곳 (투표, 밤 행동, 페이즈 전환, 채팅 전송 규칙)
 *    잠금 없이 읽은 게임은 바로 다음 순간 오래된 상태일 수 있기 때문이다.
 *  - 받은 쪽은 응답의 phaseVersion으로 오래된 응답을 거른다. 그래서 수 ms 전 상태를 보여 줘도 화면이 뒤로 가지 않는다.
 *
 * 메모리 저장소(returnsSnapshots=false)는 같은 객체를 돌려주므로 지금처럼 잠금 안에서 읽는다.
 */
public final class SnapshotRead {

    private SnapshotRead() {
    }

    /** read는 저장소에서 게임을 읽어 응답을 만드는 일만 한다. (저장하지 않는다) */
    public static <T> T of(GameRepository repository, GameLock gameLock, String gameId, Supplier<T> read) {
        Objects.requireNonNull(read, "read");
        if (repository.returnsSnapshots()) {
            return read.get();
        }
        return gameLock.withLock(gameId, read);
    }
}
