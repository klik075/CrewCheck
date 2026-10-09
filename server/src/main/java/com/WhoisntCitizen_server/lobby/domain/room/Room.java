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

    // ---------- 비밀방 ----------
    // 두 필드 모두 Redis(JSON)에 함께 저장된다.
    // 필드가 없던 예전 Redis 데이터를 읽으면 privateRoom=false, password=null → 공개방으로 취급된다.

    /** 비밀방 여부. true면 입장할 때 password가 일치해야 한다. */
    private boolean privateRoom;

    /**
     * 비밀방 비밀번호 (숫자 문자열, 4자리 이상). 공개방이면 null.
     *
     * ⚠ 현재는 평문으로 저장한다. (추후 암호화 예정)
     *   - 응답 DTO(RoomResponseDto 등)에 절대 넣지 않는다. 방 목록 API로 모든 유저에게 노출된다.
     *   - Room 객체를 통째로 로그에 찍거나 API 응답으로 그대로 내보내지 않는다.
     *   - 암호화로 바꿀 때는 이 필드(→ passwordHash)와 matchesPassword(),
     *     그리고 RoomService.createRoom에서 값을 넣는 부분만 바꾸면 된다.
     *     비밀번호 비교는 반드시 matchesPassword()를 통해서만 한다.
     */
    private String password;

    /** 공개방 생성 */
    public Room(Long id, String title, Long hostUserId, int maxPlayers) {
        this(id, title, hostUserId, maxPlayers, false, null);
    }

    /**
     * 공개방/비밀방 생성.
     * 비밀번호 형식 검증(숫자 4자리 이상)은 RoomService.createRoom에서 끝낸 뒤 호출한다.
     * 공개방이면 password가 넘어와도 저장하지 않는다. (공개방에 비밀번호가 남아 있으면 헷갈리기 때문)
     */
    public Room(Long id, String title, Long hostUserId, int maxPlayers, boolean privateRoom, String password) {
        this.id = id;
        this.title = title;
        this.hostUserId = hostUserId;
        this.maxPlayers = maxPlayers;
        this.players = new ArrayList<>();
        this.status = RoomStatus.WAITING;
        this.privateRoom = privateRoom;
        this.password = privateRoom ? password : null;
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

    /**
     * 입장하려는 사람이 보낸 비밀번호가 맞는지 확인한다.
     * 공개방이면 무엇을 보내든(보내지 않아도) 항상 true → 입장 로직에서 공개방/비밀방 분기가 필요 없다.
     * 비밀방이면 input이 저장된 비밀번호와 정확히 같아야 true. (null/빈 문자열이면 false)
     *
     * Jackson은 인자가 있는 메서드를 JSON 속성으로 보지 않으므로 Redis 저장 값에 섞이지 않는다.
     */
    public boolean matchesPassword(String input) {
        if (!privateRoom) return true;
        return input != null && input.equals(password);
    }

    // ---------- 준비 상태 ----------

    // 참가자의 준비 상태를 바꾼다. 방장은 준비할 필요가 없으므로 호출하지 않는다. (RoomService에서 막음)
    public void changeReady(Long userId, boolean ready) {
        for (RoomPlayer player : players) {
            if (player.getUserId().equals(userId)) {
                player.changeReady(ready);
                return;
            }
        }
        throw new IllegalStateException("해당 방에 참가 중이지 않습니다.");
    }

    // 방장을 뺀 모든 참가자가 준비했는지. 방장 혼자면 true (인원 검사는 따로 한다).
    public boolean allGuestsReady() {
        return players.stream()
                .filter(p -> !p.getUserId().equals(hostUserId)) // 방장 빼고
                .allMatch(RoomPlayer::isReady); // 전부 레디인가?
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
