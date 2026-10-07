package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.common.exception.RoomPasswordException;
import com.WhoisntCitizen_server.game.dto.GameParticipant;
import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.game.service.RoleAssigner;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomDetailResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.stereotype.Service;

import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.regex.Pattern;

@Slf4j
@Service
@RequiredArgsConstructor
public class RoomService {

    // 인원 규칙은 게임 쪽(RoleAssigner) 값을 그대로 사용한다. 규칙을 바꿀 때 한 곳만 고치면 된다.
    private static final int MIN_PLAYERS = RoleAssigner.MIN_PLAYERS;
    private static final int MAX_PLAYERS = RoleAssigner.MAX_PLAYERS;

    // 비밀방 비밀번호 규칙: 숫자(0~9)만, 최소 4자리, 최대 길이 제한 없음.
    // 아라비아 숫자(0~9)만 허용한다는 의도가 바로 보이도록 [0-9]로 적는다.
    static final int MIN_PASSWORD_LENGTH = 4;
    private static final Pattern PASSWORD_PATTERN = Pattern.compile("[0-9]{" + MIN_PASSWORD_LENGTH + ",}");

    private final LobbyRoomRepository roomRepository;
    private final UserRepository userRepository;
    private final RoomLockManager roomLockManager;
    private final GameService gameService;
    // 입장/퇴장 이벤트 발행 (채팅의 ChatLobbyEventListener가 받아 "OOO님이 입장했습니다." 시스템 메시지를 남긴다)
    private final ApplicationEventPublisher eventPublisher;

    /*
     * memberId = JWT의 sub (로그인 계정 id)
     * 룸 안에서는 User(프로필)의 id를 userId로 사용한다.
     */

    // 방 생성
    public RoomResponseDto createRoom(Long memberId, CreateRoomRequestDto request) {
        if (request.getTitle() == null || request.getTitle().isBlank()) {
            throw new IllegalArgumentException("방 제목을 입력해주세요.");
        }
        if (request.getMaxPlayers() < MIN_PLAYERS || request.getMaxPlayers() > MAX_PLAYERS) {
            throw new IllegalArgumentException("최대 인원은 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다.");
        }

        // 비밀방이면 비밀번호 형식 검사 (숫자만, 4자리 이상). 공개방이면 비밀번호는 보내도 무시한다.
        boolean privateRoom = request.isPrivateRoom();
        if (privateRoom) validatePassword(request.getPassword());

        User user = findUser(memberId);

        // 새로운 룸 생성 (공개방이면 Room 생성자가 password를 null로 저장한다)
        Long roomId = roomRepository.generateRoomId();
        Room room = new Room(roomId, request.getTitle(), user.getId(), request.getMaxPlayers(),
                privateRoom, request.getPassword());

        // 방을 만든 사람은 자동으로 해당 방에 입장 (비밀방이어도 방장은 비밀번호를 입력하지 않는다)
        room.addPlayer(new RoomPlayer(user.getId(), user.getNickname(), false));

        roomRepository.save(room);
        eventPublisher.publishEvent(new RoomPlayerJoinedEvent(roomId, user.getId(), user.getNickname()));
        return RoomResponseDto.from(room);
    }

    /**
     * 방 참가. 공개방/비밀방 모두 이 메서드 하나로 처리한다.
     *
     * 검사 순서 (방 잠금 안에서)
     *   1. 게임 중         → 409
     *   2. 이미 참가 중     → 409  (비밀번호보다 먼저 검사: 이미 들어와 있는 사람은 비밀번호 없이도 "이미 참가 중"을 받아야 Unity의 CheckAlreadyJoined가 대기실로 돌려보낼 수 있다)
     *   3. 비밀번호 불일치   → 403 WRONG_ROOM_PASSWORD  (공개방은 matchesPassword가 항상 true라 통과)
     *   4. 정원 초과       → 409  (비밀번호보다 뒤: 비밀번호를 모르는 사람에게 방 인원 상태를 먼저 알려주지 않는다)
     *
     * 틀린 횟수 제한은 없다. 평문 비교라 금방 끝나므로 잠금 안에서 해도 다른 요청을 오래 막지 않는다.
     *
     * @param password 비밀방 비밀번호. 공개방이면 null이어도 된다. (무시됨)
     */
    public RoomResponseDto joinRoom(Long roomId, Long memberId, String password) {
        // DB 조회(User)는 잠금 밖에서 먼저 해서 잠금을 쥐고 있는 시간을 줄인다.
        User user = findUser(memberId);

        // 읽기 → 검사 → 추가 → 저장을 같은 방 잠금 안에서 처리 (동시 입장 시 덮어쓰기 방지)
        RoomResponseDto joined = roomLockManager.withLock(roomId, () -> {
            Room room = findRoom(roomId);
            recoverIfOrphaned(room);

            // 게임은 시작할 때 참가자 명단을 고정하므로, 진행 중인 방에는 새로 들어올 수 없다.
            if (room.isInGame()) throw new IllegalStateException("게임이 진행 중인 방입니다.");
            if (room.containsPlayer(user.getId())) throw new IllegalStateException("이미 참가 중입니다.");
            if (!room.matchesPassword(password)) throw new RoomPasswordException();
            if (room.isFull()) throw new IllegalStateException("방이 가득 찼습니다.");

            room.addPlayer(new RoomPlayer(user.getId(), user.getNickname(), false));

            roomRepository.save(room);
            return RoomResponseDto.from(room);
        });

        // 이벤트는 잠금 밖에서 발행한다. (채팅 저장 때문에 방 잠금을 오래 쥐지 않도록)
        eventPublisher.publishEvent(new RoomPlayerJoinedEvent(roomId, user.getId(), user.getNickname()));
        return joined;
    }

    // 방 나가기
    public void leaveRoom(Long roomId, Long memberId) {
        User user = findUser(memberId);
        Long userId = user.getId();

        // 반환값: 방이 남아 있으면 true, 마지막 사람이 나가 방이 삭제되면 false
        boolean roomRemains = roomLockManager.withLock(roomId, () -> {
            Room room = findRoom(roomId);
            recoverIfOrphaned(room);

            if (!room.containsPlayer(userId)) throw new IllegalStateException("해당 방에 참가 중이지 않습니다.");
            // 게임 중에 나가면 방에서는 빠지지만 게임에는 살아 있는 플레이어로 남아 진행이 꼬인다.
            // 그래서 게임이 끝난 뒤에만 나갈 수 있다. 게임 중 앱을 끄는 등 연결이 끊기면
            // 게임 쪽(InactivePlayerMonitor)이 사망 처리한 뒤 removeDepartedPlayers로 방에서 뺀다.
            if (room.isInGame()) throw new IllegalStateException("게임 중에는 방을 나갈 수 없습니다.");

            room.removePlayer(userId); // 방장이면 다음 사람에게 위임

            // 아무도 없으면 방 삭제
            if (room.isEmpty()) {
                roomRepository.delete(roomId);
                return false;
            }
            roomRepository.save(room);
            return true;
        });

        // 방이 삭제된 경우에는 볼 사람이 없으므로 퇴장 알림을 남기지 않고, 방 채팅을 지우도록 알린다.
        if (roomRemains) {
            eventPublisher.publishEvent(new RoomPlayerLeftEvent(roomId, userId, user.getNickname()));
        } else {
            eventPublisher.publishEvent(new RoomDeletedEvent(roomId));
        }
    }

    /**
     * 게임 중 연결이 끊겨 게임에서 내보낸 플레이어를 방에서 뺀다. (게임 쪽 사망 처리는 이미 끝난 상태)
     * RoomGameListener가 PlayersDepartedEvent를 받아 호출한다. 방장이면 다음 사람에게 위임된다.
     * 게임 종료 이벤트와 처리 순서가 바뀌어 방이 이미 대기 상태로 돌아갔어도 그대로 뺀다.
     * 그사이 같은 방에서 다른 게임이 시작됐으면 무시한다.
     */
    public void removeDepartedPlayers(Long roomId, String gameId, List<Long> userIds) {
        boolean deleted = roomLockManager.withLock(roomId, () -> {
            Room room = roomRepository.findById(roomId);
            if (room == null) return false;
            if (room.isInGame() && !Objects.equals(room.getGameId(), gameId)) return false;

            userIds.forEach(room::removePlayer);
            if (room.isEmpty()) {
                roomRepository.delete(roomId);
                return true;
            }
            roomRepository.save(room);
            return false;
        });
        if (deleted) {
            eventPublisher.publishEvent(new RoomDeletedEvent(roomId));
        }
    }

    /**
     * 취소된 게임의 방 삭제. 결과 조회 시간이 지나 게임을 메모리에서 지울 때
     * RoomGameListener가 CancelledGameExpiredEvent를 받아 호출한다.
     * 방이 이미 없거나 다른 게임으로 넘어간 경우는 무시한다.
     *
     * @return 삭제했으면 true
     */
    public boolean deleteRoomOfCancelledGame(Long roomId, String gameId) {
        boolean deleted = roomLockManager.withLock(roomId, () -> {
            Room room = roomRepository.findById(roomId);
            if (room == null || !Objects.equals(room.getGameId(), gameId)) return false;
            roomRepository.delete(roomId);
            return true;
        });
        if (deleted) {
            eventPublisher.publishEvent(new RoomDeletedEvent(roomId));
        }
        return deleted;
    }

    // 게임 시작 (방장만)
    public StartGameResponse startGame(Long roomId, Long memberId) {
        Long userId = findUser(memberId).getId();

        // 입장/나가기와 같은 방 잠금 안에서 처리: 시작 도중 누가 들어오거나 나가지 못한다.
        return roomLockManager.withLock(roomId, () -> {
            Room room = findRoom(roomId);
            recoverIfOrphaned(room);

            if (!userId.equals(room.getHostUserId())) throw new IllegalStateException("방장만 게임을 시작할 수 있습니다.");
            if (room.isInGame()) throw new IllegalStateException("이미 게임이 진행 중인 방입니다.");
            // 사용자에게 빠르게 알려주기 위한 검사. 최종 인원 검사는 게임 쪽(RoleAssigner)이 한 번 더 한다.
            if (room.getPlayers().size() < MIN_PLAYERS) {
                throw new IllegalStateException("게임을 시작하려면 최소 " + MIN_PLAYERS + "명이 필요합니다.");
            }

            // Room 참가자(userId = User.id)를 그대로 게임 참가자(playerId)로 넘긴다.
            List<GameParticipant> participants = room.getPlayers().stream()
                    .map(p -> new GameParticipant(p.getUserId(), p.getNickname()))
                    .toList();

            // 게임을 먼저 만들고 성공했을 때만 방 상태를 바꾼다. 게임 생성이 실패하면 방은 WAITING 그대로다.
            StartGameResponse started = gameService.startGame(String.valueOf(roomId), participants);

            room.startGame(started.gameId());
            roomRepository.save(room);
            return started;
        });
    }

    /**
     * 게임 종료 후 방 복귀: IN_GAME → WAITING, gameId 제거, 준비 상태 초기화.
     * RoomGameListener가 GameEndedEvent를 받아 호출한다.
     *
     * @return 실제로 복귀 처리했으면 true, 무시했으면 false
     *         (방이 이미 없음 / 이미 WAITING / 다른 게임의 이벤트인 경우 무시)
     */
    public boolean returnToWaiting(Long roomId, String gameId) {
        return roomLockManager.withLock(roomId, () -> {
            Room room = roomRepository.findById(roomId);
            if (room == null) return false;                          // 게임 중 방이 사라진 경우
            if (!room.isInGame()) return false;                      // 이미 복귀됨
            if (!Objects.equals(room.getGameId(), gameId)) return false; // 다른(이전) 게임의 늦은 이벤트

            room.finishGame();
            roomRepository.save(room);
            return true;
        });
    }

    // 방 단건 조회 (대기 화면 polling용)
    public RoomDetailResponseDto getRoom(Long roomId) {
        // 읽기만 하는 요청이라 평소에는 잠금 없이 조회한다. (여러 참가자가 1초마다 polling해도 서로 기다리지 않음)
        // 서버 재시작 등으로 게임이 사라진 방일 때만 잠금을 잡고 WAITING으로 복구한다.
        Room room = recoverInListIfOrphaned(findRoom(roomId));
        if (room == null) throw new IllegalArgumentException("존재하지 않는 방입니다."); // 복구 중 방이 삭제된 경우
        return RoomDetailResponseDto.from(room);
    }

    // 현재 룸 참가자 조회
    public List<RoomPlayerResponseDto> getPlayers(Long roomId) {
        return findRoom(roomId).getPlayers()
                .stream()
                .map(RoomPlayerResponseDto::from)
                .toList();
    }

    // 현재 룸 조회
    public List<RoomResponseDto> getRooms() {
        Set<String> roomIds = roomRepository.findAllRoomIds();

        if (roomIds == null || roomIds.isEmpty()) return List.of();

        return roomIds.stream()
                .map(Long::valueOf) //String을 Long으로 바꿈
                .map(roomRepository::findById)// 각 id를 room으로 바꿈 Stream<String>에서 -> Stream<Room>이 됨
                .filter(Objects::nonNull)
                .map(this::recoverInListIfOrphaned) // 서버 재시작 등으로 게임이 사라진 방은 WAITING으로 복구해서 보여준다
                .filter(Objects::nonNull)           // 복구 중 방이 삭제된 경우 제외
                .map(RoomResponseDto::from)// Room -> RoomResponseDto 변환
                .toList();
    }

    /**
     * IN_GAME인데 그 게임이 메모리에 없거나 이미 끝난 방을 WAITING으로 되돌린다.
     * - 서버 재시작: 진행 중인 게임(메모리)은 사라지지만 방(Redis)은 IN_GAME으로 남는다.
     * - 종료 이벤트 처리 실패: 게임은 끝났는데 방 복귀가 안 된 경우.
     * 취소된 게임의 방은 곧 삭제되므로 게임이 메모리에 남아 있는 동안은 되돌리지 않는다. (GameService.keepsRoomInGame)
     * 반드시 해당 방의 잠금(roomLockManager) 안에서 호출한다.
     *
     * @return 복구했으면 true
     */
    private boolean recoverIfOrphaned(Room room) {
        if (!room.isInGame() || gameService.keepsRoomInGame(room.getGameId())) {
            return false;
        }
        log.warn("방 {}: 진행 중인 게임({})을 찾을 수 없어 대기 상태로 복구", room.getId(), room.getGameId());
        room.finishGame();
        roomRepository.save(room);
        eventPublisher.publishEvent(RoomNoticeEvent.of(room.getId(), "진행 중이던 게임을 찾을 수 없어 대기실로 돌아왔습니다."));
        return true;
    }

    /** 조회용(방 목록, 방 단건): 복구가 필요한 방만 잠금을 잡고 다시 읽어서 복구한다. */
    private Room recoverInListIfOrphaned(Room room) {
        if (!room.isInGame() || gameService.keepsRoomInGame(room.getGameId())) {
            return room; // 대부분의 방은 잠금 없이 그대로 반환
        }
        return roomLockManager.withLock(room.getId(), () -> {
            Room latest = roomRepository.findById(room.getId()); // 잠금 안에서 최신 상태로 다시 확인
            if (latest != null) {
                recoverIfOrphaned(latest);
            }
            return latest;
        });
    }

    /**
     * 비밀방 비밀번호 형식 검사: 숫자만, 최소 4자리. 실패하면 400 BAD_REQUEST.
     * Unity CreateRoomPopup도 같은 규칙으로 먼저 검사하지만, 최종 검증은 서버가 한다.
     */
    private static void validatePassword(String password) {
        if (password == null || !PASSWORD_PATTERN.matcher(password).matches()) {
            throw new IllegalArgumentException("비밀번호는 숫자 " + MIN_PASSWORD_LENGTH + "자리 이상이어야 합니다.");
        }
    }

    private Room findRoom(Long roomId) {
        Room room = roomRepository.findById(roomId);
        if (room == null) throw new IllegalArgumentException("존재하지 않는 방입니다.");
        return room;
    }

    private User findUser(Long memberId) {
        return userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));
    }
}
