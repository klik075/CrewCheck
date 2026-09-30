package com.WhoisntCitizen_server.lobby.domain.room;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.util.ArrayList;
import java.util.List;

@Getter
@JsonIgnoreProperties(ignoreUnknown = true) // isFull()/isEmpty()가 JSON에 full/empty로 저장되므로 읽을 때 무시
@NoArgsConstructor // Redis(JSON)에서 역직렬화할 때 필요
public class Room {
    private Long id;
    private String title;
    private Long hostUserId; // 방장을 위임 할 수도 있으므로 finalX
    private int maxPlayers;
    private List<RoomPlayer> players = new ArrayList<>();

    public Room(Long id, String title, Long hostUserId, int maxPlayers) {
        this.id = id;
        this.title = title;
        this.hostUserId = hostUserId;
        this.maxPlayers = maxPlayers;
        this.players = new ArrayList<>();
    }

    public void addPlayer(RoomPlayer player) {
        players.add(player);
    }

    public void removePlayer(Long userId) {
        players.removeIf(player -> player.getUserId().equals(userId));

        // 방장이 나가면 남은 사람 중 가장 먼저 들어온 사람에게 방장 위임
        if (userId.equals(hostUserId) && !players.isEmpty()) {
            hostUserId = players.get(0).getUserId();
        }
    }

    public boolean isEmpty() {
        return players.isEmpty();
    }

    public boolean isFull() {
        return players.size() >= maxPlayers;
    }

    public boolean containsPlayer(Long userId) {
        return players.stream().anyMatch(player -> player.getUserId().equals(userId));
    }
}
