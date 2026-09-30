package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.RoomPlayerEntity;
import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;

public interface RoomPlayerRepository extends JpaRepository<RoomPlayerEntity, Long> {
    Optional<RoomPlayerEntity> findByRoomIdAndUserId(Long roomId, Long userId);
    Optional<RoomPlayerEntity> findByRoomIdAndPlayerId(Long roomId, Long playerId);
    List<RoomPlayerEntity> findByRoomIdOrderByPlayerIdAsc(Long roomId);
}
