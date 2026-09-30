package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.JoinRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.entity.User;
import com.WhoisntCitizen_server.lobby.repository.RoomRepository;
import com.WhoisntCitizen_server.lobby.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Objects;
import java.util.Set;

@Service
@RequiredArgsConstructor
public class RoomService {

    private final RoomRepository roomRepository;
    private final UserRepository userRepository;

    // 방 생성
    @Transactional(readOnly = true)
    public RoomResponseDto createRoom(CreateRoomRequestDto request) {
        User user = userRepository
                .findById(request.getUserId())
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));

        // 새로운 룸 생성
        Long roomId = roomRepository.generateRoomId();
        Room room = new Room(roomId, request.getTitle(), user.getId(), request.getMaxPlayers());

        // 방을 만든 사람은 자동으로 해당 방에 입장
        RoomPlayer host = new RoomPlayer(user.getId(), user.getNickname(), false);
        room.addPlayer(host);

        roomRepository.save(room);
        return RoomResponseDto.from(room);
    }

    // 방 참가
    public RoomResponseDto joinRoom(Long roomId, JoinRoomRequestDto request) {

        Room room = roomRepository.findById(roomId);

        if (room == null) throw new IllegalArgumentException("존재하지 않는 방입니다.");
        if (room.isFull()) throw new IllegalStateException("방이 가득 찼습니다.");
        if (room.containsPlayer(request.getUserId())) throw new IllegalStateException("이미 참가 중입니다.");

        User user = userRepository
                .findById(request.getUserId())
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));

        RoomPlayer player = new RoomPlayer(user.getId(), user.getNickname(), false);
        room.addPlayer(player);

        roomRepository.save(room);
        return RoomResponseDto.from(room);
    }

    // 방 나가기
    public void leaveRoom(Long roomId, Long userId) {

        Room room = roomRepository.findById(roomId);

        if (room == null) throw new IllegalArgumentException("존재하지 않는 방입니다.");
        if (!room.containsPlayer(userId)) throw new IllegalStateException("해당 방에 참가 중이지 않습니다.");

        room.removePlayer(userId);
        roomRepository.save(room);
    }

    // 현재 룸 참가자 조회
    public List<RoomPlayerResponseDto> getPlayers(Long roomId) {
        Room room = roomRepository.findById(roomId);

        if (room == null) throw new IllegalArgumentException("존재하지 않는 방입니다.");

        return room.getPlayers()
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

}
