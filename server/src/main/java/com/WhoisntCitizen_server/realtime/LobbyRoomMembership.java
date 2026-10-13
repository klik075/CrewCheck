package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;

import java.util.Objects;

/**
 * 로비 저장소(Redis room:{id})에서 방을 읽어 참가자인지 확인한다.
 * 읽기만 하므로 방 잠금은 잡지 않는다. (구독 직후 나가는 경우는 4-5에서 퇴장 때 구독을 풀어 처리한다)
 * 게임 중 사망자도 방에 남아 있으므로 구독할 수 있다. 연결이 끊겨 게임에서 내보내진 사람은 방에서 빠져 구독할 수 없다.
 */
public class LobbyRoomMembership implements RoomMembership {

    private final LobbyRoomRepository roomRepository;

    public LobbyRoomMembership(LobbyRoomRepository roomRepository) {
        this.roomRepository = Objects.requireNonNull(roomRepository, "roomRepository");
    }

    @Override
    public boolean isMember(long roomId, long userId) {
        Room room = roomRepository.findById(roomId);
        return room != null && room.containsPlayer(userId);
    }
}
