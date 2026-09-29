package com.sparta.unityaitestproject_server.game.repository;

import com.sparta.unityaitestproject_server.game.entity.Game;

import java.util.Optional;

/**
 * 진행 중인 게임 저장소. 지금은 메모리 구현만 있고,
 * 서버를 여러 대로 늘릴 때 Redis 구현으로 교체한다 (게임 결과 기록은 별도 JPA Repository 권장).
 */
public interface GameRepository {
    Game save(Game game);
    Optional<Game> findById(String gameId);
    void delete(String gameId);
}
