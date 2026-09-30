package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

import java.util.List;
import java.util.Objects;
import java.util.Set;

@Service
@RequiredArgsConstructor
public class RoomService {

    private static final int MIN_PLAYERS = 4;  // 게임 시작 조건(StartGameRequest)과 동일
    private static final int MAX_PLAYERS = 12;

    private final LobbyRoomRepository roomRepository;
    private final UserRepository userRepository;
    private final RoomLockManager roomLockManager;

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

        User user = findUser(memberId);

        // 새로운 룸 생성
        Long roomId = roomRepository.generateRoomId();
        Room room = new Room(roomId, request.getTitle(), user.getId(), request.getMaxPlayers());

        // 방을 만든 사람은 자동으로 해당 방에 입장
        room.addPlayer(new RoomPlayer(user.getId(), user.getNickname(), false));

        roomRepository.save(room);
        return RoomResponseDto.from(room);
    }

    // 방 참가
    public RoomResponseDto joinRoom(Long roomId, Long memberId) {
        // DB 조회(User)는 잠금 밖에서 먼저 해서 잠금을 쥐고 있는 시간을 줄인다.
        User user = findUser(memberId);

        // 읽기 → 검사 → 추가 → 저장을 같은 방 잠금 안에서 처리 (동시 입장 시 덮어쓰기 방지)
        return roomLockManager.withLock(roomId, () -> {
            Room room = findRoom(roomId);

            // 게임은 시작할 때 참가자 명단을 고정하므로, 진행 중인 방에는 새로 들어올 수 없다.
            if (room.isInGame()) throw new IllegalStateException("게임이 진행 중인 방입니다.");
            if (room.containsPlayer(user.getId())) throw new IllegalStateException("이미 참가 중입니다.");
            if (room.isFull()) throw new IllegalStateException("방이 가득 찼습니다.");

            room.addPlayer(new RoomPlayer(user.getId(), user.getNickname(), false));

            roomRepository.save(room);
            return RoomResponseDto.from(room);
        });
    }

    // 방 나가기
    public void leaveRoom(Long roomId, Long memberId) {
        Long userId = findUser(memberId).getId();

        roomLockManager.withLock(roomId, () -> {
            Room room = findRoom(roomId);

            if (!room.containsPlayer(userId)) throw new IllegalStateException("해당 방에 참가 중이지 않습니다.");
            // 게임 중에 나가면 방에서는 빠지지만 게임에는 살아 있는 플레이어로 남아 진행이 꼬인다.
            // 게임 중 퇴장(사망 처리 등) 규칙이 정해지기 전까지는 게임이 끝난 뒤에만 나갈 수 있다.
            if (room.isInGame()) throw new IllegalStateException("게임 중에는 방을 나갈 수 없습니다.");

            room.removePlayer(userId); // 방장이면 다음 사람에게 위임

            // 아무도 없으면 방 삭제
            if (room.isEmpty()) {
                roomRepository.delete(roomId);
                return;
            }
            roomRepository.save(room);
        });
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
                .map(RoomResponseDto::from)// Room -> RoomResponseDto 변환
                .toList();
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
