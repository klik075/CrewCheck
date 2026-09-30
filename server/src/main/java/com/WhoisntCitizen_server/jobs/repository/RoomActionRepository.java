package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.RoomActionEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomPhase;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

public interface RoomActionRepository extends JpaRepository<RoomActionEntity, Long> {
    // 동일 requestId의 재시도인지 확인한다.
    Optional<RoomActionEntity> findByRoomIdAndActorPlayerIdAndRequestId(
        Long roomId, Long actorPlayerId, UUID requestId
    );

    // 일반 능력은 같은 단계·라운드에 한 번만 제출한다. 해적의 공유 대상 변경은 별도 테이블에 저장한다.
    Optional<RoomActionEntity> findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
        Long roomId, Long actorPlayerId, RoomPhase phase, int roundNumber, String actionCode
    );

    long countByRoomIdAndActorPlayerIdAndActionCode(
        Long roomId, Long actorPlayerId, String actionCode
    );

    List<RoomActionEntity> findByRoomIdAndPhaseAndRoundNumberOrderByIdAsc(
        Long roomId, RoomPhase phase, int roundNumber
    );
}
