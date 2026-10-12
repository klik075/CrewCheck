package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.event.RoomStateChangedEvent;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.SetOperations;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.ValueOperations;

import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/**
 * 방을 저장·삭제하면 RoomStateChangedEvent가 발행되는지 확인한다. (Redis는 가짜로 바꾼다)
 */
class LobbyRoomRepositoryEventTest {

    private final List<Object> events = new ArrayList<>();
    private LobbyRoomRepository repository;

    @BeforeEach
    @SuppressWarnings("unchecked")
    void setUp() {
        RedisTemplate<String, Room> redisTemplate = mock(RedisTemplate.class);
        StringRedisTemplate stringRedisTemplate = mock(StringRedisTemplate.class);
        when(redisTemplate.opsForValue()).thenReturn(mock(ValueOperations.class));
        when(stringRedisTemplate.opsForSet()).thenReturn(mock(SetOperations.class));
        repository = new LobbyRoomRepository(redisTemplate, stringRedisTemplate, events::add);
    }

    @Test
    void 방을_저장하면_변경_이벤트를_발행한다() {
        repository.save(new Room(3L, "방", 15L, 6));

        assertThat(events).containsExactly(new RoomStateChangedEvent(3L, false));
    }

    @Test
    void 방을_삭제하면_삭제_이벤트를_발행한다() {
        repository.delete(3L);

        assertThat(events).containsExactly(new RoomStateChangedEvent(3L, true));
    }
}
