package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.RoomEntity;
import jakarta.persistence.LockModeType;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface RoomRepository extends JpaRepository<RoomEntity, Long> {
    // 행동 제출과 향후 페이즈 전환이 동일한 잠금을 잡아야 상태 변경과 제출이 충돌하지 않는다.
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select r from RoomEntity r where r.id = :roomsId")
    Optional<RoomEntity> lockById(@Param("roomsId") Long roomsId);
}
