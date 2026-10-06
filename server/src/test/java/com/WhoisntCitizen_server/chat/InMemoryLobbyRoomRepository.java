package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;

import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;
import java.util.stream.Collectors;

/** 테스트용: Redis 없이 로비 방을 메모리에 저장하는 LobbyRoomRepository */
public class InMemoryLobbyRoomRepository extends LobbyRoomRepository {

    private final Map<Long, Room> rooms = new ConcurrentHashMap<>();
    private final AtomicLong seq = new AtomicLong();

    public InMemoryLobbyRoomRepository() {
        super(null, null);
    }

    @Override
    public void save(Room room) {
        rooms.put(room.getId(), room);
    }

    @Override
    public Room findById(Long roomId) {
        return rooms.get(roomId);
    }

    @Override
    public void delete(Long roomId) {
        rooms.remove(roomId);
    }

    @Override
    public Set<String> findAllRoomIds() {
        return rooms.keySet().stream().map(String::valueOf).collect(Collectors.toSet());
    }

    @Override
    public Long generateRoomId() {
        return seq.incrementAndGet();
    }
}
