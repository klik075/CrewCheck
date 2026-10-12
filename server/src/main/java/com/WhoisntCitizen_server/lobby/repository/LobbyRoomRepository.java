package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.event.RoomStateChangedEvent;
import lombok.RequiredArgsConstructor;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Repository;

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