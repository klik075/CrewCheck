package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.event.RoomStateChangedEvent;
import lombok.RequiredArgsConstructor;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Repository;

import java.util.Collection;
import java.util.List;
import java.util.Objects;
import java.util.Set;

@Repository
@RequiredArgsConstructor
public class LobbyRoomRepository {

    private final RedisTemplate<String, Room> redisTemplate;
    private final StringRedisTemplate stringRedisTemplate;
    // 방이 바뀔 때마다 RoomStateChangedEvent 발행 (실시간 모듈이 받아 room.changed를 보낸다)
    // 방 변경은 모두 save/delete를 거치므로 여기서 알리면 빠지는 곳이 없다.
    // 방 잠금 안에서 불리지만, 받는 쪽은 연결마다 버퍼에 넣고 바로 돌아오므로 잠금을 오래 쥐지 않는다.
    private final ApplicationEventPublisher eventPublisher;

    private static final String PREFIX = "room:";
    private static final String ROOM_IDS_KEY = "rooms";

    public void save(Room room) {
        // 실제 방 저장
        redisTemplate.opsForValue().set(PREFIX + room.getId(), room);

        // 현재 존재하는 방 ID 저장
        stringRedisTemplate.opsForSet().add(ROOM_IDS_KEY, room.getId().toString());

        eventPublisher.publishEvent(new RoomStateChangedEvent(room.getId(), false));
    }

    public Room findById(Long roomId) {
        return redisTemplate.opsForValue().get(PREFIX + roomId);
    }

    /**
     * 여러 방을 Redis 왕복 1번(MGET)으로 읽는다. 방 목록 조회용.
     *
     * 예전에는 방마다 findById(GET)를 불러 방 N개면 Redis를 N번 직렬 왕복했다. (N+1 문제)
     * 부하테스트(방 1000개, 동시 100명)에서 목록 조회 p95가 1.5초까지 늘어나 MGET 한 번으로 바꿨다.
     *
     * 그 사이 지워진 방(키 없음)은 결과에서 뺀다. 순서는 넘겨준 ID 순서를 따른다.
     */
    public List<Room> findAllByIds(Collection<Long> roomIds) {
        if (roomIds == null || roomIds.isEmpty()) return List.of();
        List<String> keys = roomIds.stream().map(id -> PREFIX + id).toList();
        List<Room> rooms = redisTemplate.opsForValue().multiGet(keys);
        if (rooms == null) return List.of(); // 파이프라인·트랜잭션 안에서는 null을 돌려준다
        return rooms.stream().filter(Objects::nonNull).toList();
    }

    public void delete(Long roomId) {
        // 룸 삭제
        redisTemplate.delete(PREFIX + roomId);

        // 방 ID 목록에서도 삭제
        stringRedisTemplate.opsForSet().remove(ROOM_IDS_KEY, roomId.toString());

        eventPublisher.publishEvent(new RoomStateChangedEvent(roomId, true));
    }

    public Set<String> findAllRoomIds() {
        return stringRedisTemplate.opsForSet().members(ROOM_IDS_KEY);
    }

    public Long generateRoomId() {
        return stringRedisTemplate.opsForValue().increment("room:id:sequence");
    }
}