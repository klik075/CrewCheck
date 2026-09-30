package com.WhoisntCitizen_server.jobs.dto;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.PirateAttackSelectionEntity;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomActionEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomPhase;
import com.WhoisntCitizen_server.jobs.domain.RoomReportEntity;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.PositiveOrZero;
import java.util.List;
import java.util.UUID;

/** HTTP JSON 계약. 필드 이름을 바꾸면 Unity RoleApiClient의 공개 필드도 함께 수정한다. */
public final class RoleApiDtos {
    private RoleApiDtos() {}

    public record RoleView(String code, String name, Faction faction) {
        public static RoleView from(RoleEntity role) {
            return new RoleView(role.getCode(), role.getName(), role.getFaction());
        }
    }

    public record RoleListResponse(List<RoleView> roles) {}
    // remainingUses=-1은 게임 전체 횟수 제한 없음. 라운드당 중복 제출은 별도로 제한한다.
    public record AbilityView(String actionCode, int remainingUses, RoomPhase phase) {}
    public record MyRoleResponse(
        // 공개 이름은 gamesId지만 현재 값은 내부 rooms.id다.
        Long gamesId, RoleView role, boolean alive,
        List<AbilityView> abilities, List<Long> allies
    ) {}

    public record ReportView(Long id, int roundNumber, String type, String message) {
        public static ReportView from(RoomReportEntity report) {
            return new ReportView(
                report.getId(), report.getRoundNumber(), report.getType(), report.getMessage()
            );
        }
    }

    public record ReportsResponse(List<ReportView> reports) {}

    /** 이번 밤의 공유 대상. 미선택 시 hasTarget=false이고 두 playerId는 0이므로 무시한다. */
    public record PirateAttackTargetResponse(
        Long gamesId, int nightNumber, boolean hasTarget,
        long targetPlayerId, long selectedByPlayerId
    ) {}

    /**
     * requestId는 재시도 시에도 같은 값을 사용한다.
     * playerId 0은 현재 Unity 로컬 프로토타입의 첫 플레이어 번호라 허용한다.
     */
    public record SubmitActionRequest(
        @NotNull UUID requestId,
        @NotBlank String actionCode,
        @NotNull @PositiveOrZero Long targetPlayerId
    ) {}

    public record ActionResponse(
        Long actionId, Long gamesId, String status,
        String actionCode, Long targetPlayerId
    ) {
        public static ActionResponse from(RoomActionEntity action) {
            return new ActionResponse(
                action.getId(), action.getRoomId(), action.getStatus(),
                action.getActionCode(), action.getTargetPlayerId()
            );
        }

        public static ActionResponse from(PirateAttackSelectionEntity selection) {
            return new ActionResponse(
                selection.getId(), selection.getRoomId(), "SELECTED",
                "SELECT_ATTACK_TARGET", selection.getTargetPlayerId()
            );
        }
    }
}
