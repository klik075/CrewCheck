package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;

import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Redis 저장소와 같은 방식으로 저장하는 테스트용 저장소. Game 객체가 아니라 JSON 문자열을 보관한다.
 *
 *  save()    : Game → GameSnapshotCodec.encode → JSON 문자열 보관
 *  findById(): JSON 문자열 → GameSnapshotCodec.decode → 매번 새 Game
 *
 * InMemoryGameRepository는 같은 객체를 돌려주므로, 꺼낸 Game을 고치고 save()를 빼먹어도 티가 나지 않는다.
 * 이 저장소는 save() 시점의 JSON만 남기므로, save() 없이 바꾼 내용은 다음 findById()에서 사라진다.
 * → save 누락이 테스트 실패로 드러난다.
 *
 * RedisGameRepository(1-6)와 같은 코덱을 쓰므로, 저장 형식(스냅샷·매퍼·JSON)에서 빠지는 값이 있으면
 * Redis 없이도 이 저장소를 쓰는 테스트(GameSaveDisciplineTest 등)에서 먼저 드러난다.
 *
 * 진행 중인 게임 목록(findActiveIds)도 Redis와 같은 방식으로, JSON을 다시 읽지 않고 따로 둔 id 집합으로 관리한다.
 * (Redis: save할 때 끝나지 않은 게임이면 SADD, 끝났으면 SREM / delete할 때 SREM)
 */
public class CopyingGameRepository implements GameRepository {

    private final GameSnapshotCodec codec = new GameSnapshotCodec();
    private final Map<String, String> store = new ConcurrentHashMap<>();
    private final Set<String> activeIds = ConcurrentHashMap.newKeySet();
    private int saveCount;

    @Override
    public Game save(Game game) {
        store.put(game.getGameId(), codec.encode(game));
        if (game.isEnded()) {
            activeIds.remove(game.getGameId());
        } else {
            activeIds.add(game.getGameId());
        }
        saveCount++;
        return game;
    }

    @Override
    public Optional<Game> findById(String gameId) {
        return Optional.ofNullable(store.get(gameId)).map(codec::decode);
    }

    @Override
    public List<String> findActiveIds() {
        return List.copyOf(activeIds);
    }

    @Override
    public void delete(String gameId) {
        store.remove(gameId);
        activeIds.remove(gameId);
    }

    /** 지금까지 save()가 불린 횟수 */
    public int saveCount() {
        return saveCount;
    }

    /** 저장돼 있는 JSON 그대로 (저장 형식 확인용). 없으면 null */
    public String storedJson(String gameId) {
        return store.get(gameId);
    }
}
