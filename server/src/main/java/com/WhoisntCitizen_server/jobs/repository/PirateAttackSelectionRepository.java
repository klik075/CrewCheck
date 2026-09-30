package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.PirateAttackSelectionEntity;
import java.util.Optional;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

public interface PirateAttackSelectionRepository
    extends JpaRepository<PirateAttackSelectionEntity, Long> {
    // 방 잠금 안에서 저장하므로 가장 큰 id가 그 밤의 마지막 선택이다.
    Optional<PirateAttackSelectionEntity> findTopByRoomIdAndNightNumberOrderByIdDesc(
        Long roomId, int nightNumber
    );

    Optional<PirateAttackSelectionEntity> findByRoomIdAndActorPlayerIdAndRequestId(
        Long roomId, Long actorPlayerId, UUID requestId
    );
}
