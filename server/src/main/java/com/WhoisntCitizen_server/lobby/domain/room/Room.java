package com.WhoisntCitizen_server.lobby.domain.room;

import lombok.Getter;

import java.util.ArrayList;
import java.util.List;

@Getter
public class Room {
    private final Long id;
    private String title;
    private final Long hostUserId;
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
    }

    public boolean isFull() {
        return players.size() >= maxPlayers;
    }

    public boolean containsPlayer(Long userId) {
        return players.stream().anyMatch(player -> player.getUserId().equals(userId));
    }
}