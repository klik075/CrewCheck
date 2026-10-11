package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.repository.ChangeEventGameRepository;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 4-4: 실제 Spring 설정에서 "게임 저장 → 잠금 해제 → 이벤트 → 그 방 연결에 game.changed"가 이어지는지 확인한다.
 * WebSocket 대신 가짜 연결을 연결 목록에 직접 넣는다. (WebSocket 전송은 4-2·4-3 테스트에서 확인했다)
 */
@SpringBootTest
class RealtimeGameChangeWiringTest {

    @Autowired
    GameRepository gameRepository;

    @Autowired
    GameLock gameLock;

    @Autowired
    RealtimeSessionRegistry registry;

    @Autowired
    RealtimeMessageCodec codec;

    @AfterEach
    void cleanUp() {
        registry.unregister("fake-room-3");
    }

    private static Game game(String roomId) {
        return new Game(roomId, List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR)), true);
    }

    @Test
    void 게임_저장소는_변경을_알리는_저장소로_감싸져_있다() {
        assertThat(gameRepository).isInstanceOf(ChangeEventGameRepository.class);
    }

    @Test
    void 잠금_안에서_게임을_저장하면_잠금이_풀린_뒤_그_방_연결이_game_changed를_받는다() {
        FakeConnection connection = new FakeConnection("fake-room-3", 15);
        registry.register(connection);
        registry.subscribe("fake-room-3", 3);
        Game game = game("3");

        gameLock.runWithLock(game.getGameId(), () -> {
            gameRepository.save(game);
            assertThat(connection.sent).as("잠금 안에서는 아직 보내지 않는다").isEmpty();
        });

        assertThat(connection.sent).containsExactly(
                codec.encode(new ServerMessage.GameChanged(game.getGameId(), game.getPhaseVersion())));
    }
}
