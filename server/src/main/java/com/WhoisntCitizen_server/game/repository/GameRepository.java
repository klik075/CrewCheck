package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.entity.Game;

import java.util.List;
import java.util.Optional;

/**
 * 진행 중인 게임 저장소. 지금은 메모리 구현만 있고,
 * 서버를 여러 대로 늘릴 때 Redis 구현으로 교체한다 (게임 결과 기록은 별도 JPA Repository 권장).
 */
public interface GameRepository {
    Game save(Game game);
    Optional<Game> findById(String gameId);

    /**
     * 끝나지 않은 게임(시작 전·진행 중)의 id 목록. 순서는 정하지 않는다.
     * 끝난 게임(phase = ENDED, 삭제 전 보관 중)과 삭제한 게임은 들어가지 않는다.
     *
     * 게임 전체가 아니라 id만 돌려준다. Redis에서는 게임마다 JSON을 읽고 변환하는 비용이 크고,
     * 받은 쪽은 어차피 게임 잠금 안에서 findById로 다시 읽어야 하기 때문이다. (잠금 밖에서 읽은 게임은 오래된 복사본)
     * 목록을 받은 사이에 게임이 끝나거나 삭제될 수 있으므로, 받은 쪽은 findById가 비었거나 끝난 게임이면 건너뛴다.
     */
    List<String> findActiveIds();

    void delete(String gameId);

    /**
     * findById가 저장된 상태의 "복사본"을 돌려주는지.
     *  - true (Redis): 읽을 때마다 JSON에서 새 Game을 만든다. 다른 스레드가 같은 게임을 바꿔도 받은 객체는 바뀌지 않는다.
     *    그래서 화면에 보여 주기만 하는 읽기는 게임 잠금 없이 해도 반쯤 바뀐 상태를 보지 않는다. (SnapshotRead)
     *  - false (메모리): 저장소가 들고 있는 바로 그 객체를 돌려준다. 잠금 없이 읽으면 다른 스레드가 바꾸는 도중의 상태를 볼 수 있다.
     * 모르면 false(안전한 쪽)로 둔다.
     */
    default boolean returnsSnapshots() {
        return false;
    }
}
