package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.ValueOperations;

import java.util.Arrays;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

/**
 * 방 목록용 findAllByIds가 방마다 GET하지 않고 MGET 한 번으로 읽는지 확인한다. (Redis는 가짜로 바꾼다)
 * 배경: 방 목록 N+1 (loadtest/README.md 시나리오 B)
 */
class LobbyRoomRepositoryFindAllTest {

    private ValueOperations<String, Room> values;
    private RedisTemplate<String, Room> redisTemplate;
    private LobbyRoomRepository repository;

    @BeforeEach
    @SuppressWarnings("unchecked")
    void setUp() {
        redisTemplate = mock(RedisTemplate.class);
        values = mock(ValueOperations.class);
        when(redisTemplate.opsForValue()).thenReturn(values);
        repository = new LobbyRoomRepository(redisTemplate, mock(StringRedisTemplate.class), event -> { });
    }

    @Test
    void 여러_방을_MGET_한_번으로_읽고_GET은_부르지_않는다() {
        Room a = new Room(1L, "가", 10L, 8);
        Room b = new Room(2L, "나", 20L, 8);
        when(values.multiGet(List.of("room:1", "room:2"))).thenReturn(List.of(a, b));

        List<Room> rooms = repository.findAllByIds(List.of(1L, 2L));

        assertThat(rooms).containsExactly(a, b);
        verify(values, times(1)).multiGet(List.of("room:1", "room:2"));
        verify(values, never()).get(anyString());
    }

    @Test
    void 그_사이_지워진_방은_결과에서_뺀다() {
        Room a = new Room(1L, "가", 10L, 8);
        // MGET은 없는 키 자리에 null을 돌려준다
        when(values.multiGet(List.of("room:1", "room:2"))).thenReturn(Arrays.asList(a, null));

        assertThat(repository.findAllByIds(List.of(1L, 2L))).containsExactly(a);
    }

    @Test
    void 방_ID가_없으면_Redis를_부르지_않는다() {
        assertThat(repository.findAllByIds(List.of())).isEmpty();
        verifyNoInteractions(values);
    }
}
