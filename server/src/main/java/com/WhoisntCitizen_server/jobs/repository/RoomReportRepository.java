package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.RoomReportEntity;
import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;

public interface RoomReportRepository extends JpaRepository<RoomReportEntity, Long> {
    List<RoomReportEntity> findByRoomIdAndRecipientPlayerIdOrderByIdAsc(
        Long roomId, Long recipientPlayerId
    );
}
