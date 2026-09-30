package com.WhoisntCitizen_server.lobby.domain.room;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.util.ArrayList;
import java.util.List;

@Getter
@JsonIgnoreProperties(ignoreUnknown = true) // isFull()/isEmpty()/isInGame()가 JSON에 full/empty/inGame으로 저장되므로 읽을 때 무시
@NoArgsConstructor // Redis(JSON)에서 역직렬화할 때 필요
public class Room {
    private Long id;
    private String title;
    private Long hostUserId; // 방장을 위임 할 수도 있으므로 finalX
    private int maxPlayers;
    private List<RoomPlayer> players = new ArrayList<>();

    // 기본값 WAITING: status 필드가 없던 예전 Redis 데이터를 읽어도 대기 중으로 취급된다.
    private RoomStatus status = RoomStatus.WAITING;
    private String gameId; // 진행 중인 게임 id (대기 중이면 null)

    public Room(Long id, String title, Long hostUserId, int maxPlayers) {
        this.id = id;
        this.title = title;
        this.hostUserId = hostUserId;
        this.maxPlayers = maxPlayers;
        this.players = new ArrayList<>();
        this.status = RoomStatus.WAITING;
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

    // ---------- 게임 상태 ----------

    /** 게임 시작: 방을 IN_GAME으로 바꾸고 진행 중인 게임 id를 기록한다. */
    public void startGame(String gameId) {
        if (isInGame()) {
            throw new IllegalStateException("이미 게임이 진행 중인 방입니다.");
        }
        this.status = RoomStatus.IN_GAME;
        this.gameId = gameId;
    }

    /** 게임 종료: 대기 상태로 되돌리고 모든 참가자의 준비 상태를 초기화한다 (기존 방으로 복귀). */
    public void finishGame() {
        this.status = RoomStatus.WAITING;
        this.gameId = null;
        players.forEach(RoomPlayer::resetReady);
    }

    public boolean isInGame() {
        return status == RoomStatus.IN_GAME;
    }
}
