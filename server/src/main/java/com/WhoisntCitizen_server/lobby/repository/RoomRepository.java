package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import lombok.RequiredArgsConstructor;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Repository;

import java.util.Set;

@Repository
@RequiredArgsConstructor
public class RoomRepository {

    private final RedisTemplate<String, Room> redisTemplate;
    private final StringRedisTemplate stringRedisTemplate;

    private static final String PREFIX = "room:";
    private static final String ROOM_IDS_KEY = "rooms";

    public void save(Room room) {
        // 실제 방 저장
        redisTemplate.opsForValue().set(PREFIX + room.getId(), room);

        // 현재 존재하는 방 ID 저장
        stringRedisTemplate.opsForSet().add(ROOM_IDS_KEY, room.getId().toString());
    }

    public Room findById(Long roomId) {
        return redisTemplate.opsForValue().get(PREFIX + roomId);
    }

    public void delete(Long roomId) {
        // 룸 삭제
        redisTemplate.delete(PREFIX + roomId);

        // 방 ID 목록에서도 삭제
        stringRedisTemplate.opsForSet().remove(ROOM_IDS_KEY, roomId.toString());
    }

    public Set<String> findAllRoomIds() {
        return stringRedisTemplate.opsForSet().members(ROOM_IDS_KEY);
    }

    public Long generateRoomId() {
        return stringRedisTemplate.opsForValue().increment("room:id:sequence");
    }
}