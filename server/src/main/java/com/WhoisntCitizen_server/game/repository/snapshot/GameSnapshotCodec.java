package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.game.entity.Game;
import tools.jackson.databind.json.JsonMapper;

/**
 * Game ↔ 저장 문자열(JSON) 변환. 게임 저장소가 실제로 저장하고 읽는 형식은 이 클래스가 정한다.
 *
 *  저장: Game → GameSnapshot.from → JSON 문자열
 *  조회: JSON 문자열 → GameSnapshot → toGame → 새 Game 객체
 *
 * Spring이 등록한 JsonMapper를 주입받지 않고 직접 만든다.
 * spring.jackson.* 설정(날짜 형식, null 처리 등)은 API 응답용이라 바뀔 수 있는데, 그때 저장 형식까지 같이 바뀌면
 * 이미 저장된 게임을 읽지 못하게 된다. 저장 형식은 GameSnapshot의 schemaVersion으로만 관리한다.
 *
 * RedisGameRepository(1-6)와 테스트용 CopyingGameRepository가 같은 코덱을 써서,
 * 테스트에서 통과한 저장·조회 경로가 Redis에서도 그대로 쓰이게 한다.
 */
public class GameSnapshotCodec {

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    /** 게임의 지금 상태를 저장할 JSON으로 만든다. */
    public String encode(Game game) {
        return jsonMapper.writeValueAsString(GameSnapshot.from(game));
    }

    /** 저장된 JSON에서 게임을 되살린다. 호출할 때마다 새 Game 객체를 만든다. */
    public Game decode(String json) {
        return jsonMapper.readValue(json, GameSnapshot.class).toGame();
    }
}
