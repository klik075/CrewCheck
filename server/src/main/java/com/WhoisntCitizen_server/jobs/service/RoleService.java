package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.PirateAttackSelectionEntity;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomActionEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomPhase;
import com.WhoisntCitizen_server.jobs.domain.RoomPlayerEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomReportEntity;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.AbilityView;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.ActionResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.MyRoleResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.PirateAttackTargetResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.ReportView;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.ReportsResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleListResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleView;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.SubmitActionRequest;
import com.WhoisntCitizen_server.jobs.repository.RoleRepository;
import com.WhoisntCitizen_server.jobs.repository.PirateAttackSelectionRepository;
import com.WhoisntCitizen_server.jobs.repository.RoomActionRepository;
import com.WhoisntCitizen_server.jobs.repository.RoomPlayerRepository;
import com.WhoisntCitizen_server.jobs.repository.RoomReportRepository;
import com.WhoisntCitizen_server.jobs.repository.RoomRepository;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.server.ResponseStatusException;

/**
 * 공개 직업 목록, 본인 정보, 행동 접수를 담당한다.
 * 이 서비스는 밤 행동을 기록만 하고 실제 효과는 NightResolutionService가 밤 종료 시 적용한다.
 * 해적 공격 대상은 여러 번 바꿀 수 있어 room_actions 대신 pirate_attack_selections에 기록한다.
 * 예외는 포수의 DAY_SHOOT로, 낮에 즉시 적용한다.
 */
@Service
public class RoleService {
    private final RoleRepository roles;
    private final RoomRepository rooms;
    private final RoomPlayerRepository players;
    private final RoomActionRepository actions;
    private final RoomReportRepository reports;
    private final PirateAttackSelectionRepository pirateSelections;
    private final RoomOutcomeService outcome;

    public RoleService(RoleRepository roles, RoomRepository rooms, RoomPlayerRepository players,
                       RoomActionRepository actions, RoomReportRepository reports,
                       PirateAttackSelectionRepository pirateSelections,
                       RoomOutcomeService outcome) {
        this.roles = roles;
        this.rooms = rooms;
        this.players = players;
        this.actions = actions;
        this.reports = reports;
        this.pirateSelections = pirateSelections;
        this.outcome = outcome;
    }

    @Transactional(readOnly = true)
    public RoleListResponse listRoles(Faction faction) {
        // enabled=false인 보류 직업은 기존 DB 참조를 위해 남겨 두되 새 게임 목록에는 보이지 않는다.
        List<RoleEntity> result = faction == null
            ? roles.findByEnabledTrueOrderByCodeAsc()
            : roles.findByFactionAndEnabledTrueOrderByCodeAsc(faction);
        return new RoleListResponse(result.stream().map(RoleView::from).toList());
    }

    @Transactional(readOnly = true)
    public MyRoleResponse getMyRole(Long roomsId, Long userId) {
        RoomEntity room = room(roomsId);
        RoomPlayerEntity actor = member(roomsId, userId);
        if (room.getPhase() == RoomPhase.SETUP || actor.getRoleCode() == null) {
            throw error(HttpStatus.CONFLICT, "Roles have not been assigned yet");
        }

        // 보이는 직업을 반환한다. 원숭이의 실제 CREW_MONKEY는 여기서 공개하지 않는다.
        RoleEntity shownRole = role(actor.getShownRoleCode());
        List<AbilityView> available = new ArrayList<>();
        if (shownRole.getActionCode() != null) {
            available.add(ability(roomsId, actor, ActionCode.valueOf(shownRole.getActionCode())));
        }
        List<Long> allies = List.of();
        if (outcome.factionOf(actor) == Faction.PIRATE) {
            // 해적끼리는 서로 알지만 공유 공격 대상 변경은 PIRATE_RAIDER만 할 수 있다.
            allies = players.findByRoomIdOrderByPlayerIdAsc(roomsId).stream()
                .filter(player -> !Objects.equals(player.getPlayerId(), actor.getPlayerId()))
                .filter(player -> outcome.factionOf(player) == Faction.PIRATE)
                .map(RoomPlayerEntity::getPlayerId).toList();
        }
        return new MyRoleResponse(roomsId, RoleView.from(shownRole), actor.isAlive(), available, allies);
    }

    @Transactional(readOnly = true)
    public ReportsResponse getMyReports(Long roomsId, Long userId) {
        room(roomsId);
        RoomPlayerEntity actor = member(roomsId, userId);
        // recipient_player_id로 조회해 다른 플레이어의 조사 결과가 섞이지 않게 한다.
        return new ReportsResponse(reports.findByRoomIdAndRecipientPlayerIdOrderByIdAsc(
            roomsId, actor.getPlayerId()).stream().map(ReportView::from).toList());
    }

    @Transactional(readOnly = true)
    public PirateAttackTargetResponse getPirateAttackTarget(Long roomsId, Long userId) {
        RoomEntity room = room(roomsId);
        RoomPlayerEntity actor = member(roomsId, userId);
        if (room.getPhase() != RoomPhase.NIGHT) {
            throw error(HttpStatus.CONFLICT, "Attack target is visible only at night");
        }
        if (!actor.isAlive() || actor.getRoleCode() == null
            || outcome.factionOf(actor) != Faction.PIRATE) {
            throw error(HttpStatus.FORBIDDEN, "Only living pirates can view the attack target");
        }
        PirateAttackSelectionEntity selected = pirateSelections
            .findTopByRoomIdAndNightNumberOrderByIdDesc(roomsId, room.getNightNumber())
            .orElse(null);
        return new PirateAttackTargetResponse(roomsId, room.getNightNumber(), selected != null,
            selected == null ? 0L : selected.getTargetPlayerId(),
            selected == null ? 0L : selected.getActorPlayerId());
    }

    @Transactional
    public ActionResponse submitAction(Long roomsId, Long userId, SubmitActionRequest request) {
        // 모든 행동과 단계 전환은 방 잠금을 먼저 잡아 같은 라운드의 동시 요청을 직렬화한다.
        RoomEntity room = rooms.lockById(roomsId)
            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
        RoomPlayerEntity actor = member(roomsId, userId);

        // 네트워크 재시도는 현재 단계가 변하거나 능력 횟수가 소진되어도 원래 결과를 돌려준다.
        RoomActionEntity previous = actions.findByRoomIdAndActorPlayerIdAndRequestId(
            roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
        if (previous != null) {
            if (!Objects.equals(previous.getActionCode(), request.actionCode())
                || !Objects.equals(previous.getTargetPlayerId(), request.targetPlayerId())) {
                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
            }
            return ActionResponse.from(previous);
        }
        PirateAttackSelectionEntity priorSelection = pirateSelections
            .findByRoomIdAndActorPlayerIdAndRequestId(
                roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
        if (priorSelection != null) {
            if (!ActionCode.SELECT_ATTACK_TARGET.name().equals(request.actionCode())
                || !Objects.equals(priorSelection.getTargetPlayerId(), request.targetPlayerId())) {
                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
            }
            return ActionResponse.from(priorSelection);
        }

        ActionCode code = parseAction(request.actionCode());
        // 순서: 현재 단계/생존 → 직업 권한 → 라운드 중복/게임 횟수 → 대상 규칙 → 저장.
        // 새 능력의 세부 제한(예: 연속 자기 보호)은 저장 전 이 검증 구간에 넣는다.
        if (room.getPhase() != code.phase()) {
            throw error(HttpStatus.CONFLICT, "Action is unavailable in this phase");
        }
        if (!actor.isAlive() || actor.getRoleCode() == null) {
            throw error(HttpStatus.FORBIDDEN, "Player cannot act");
        }
        RoleEntity shownRole = role(actor.getShownRoleCode());
        // 공유 공격 대상 변경은 해적 직업의 행동이다. 원숭이 위장 행동은 밤 판정에서 무효화된다.
        boolean pirateAttack = code == ActionCode.SELECT_ATTACK_TARGET;
        if (!code.name().equals(shownRole.getActionCode())) {
            throw error(HttpStatus.FORBIDDEN, "Action is not available to this role");
        }
        if (!pirateAttack && actions.findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
            roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(), code.name()
        ).isPresent()) {
            throw error(HttpStatus.CONFLICT, "Action already submitted this phase");
        }
        if (code.maxUses() >= 0 && actions.countByRoomIdAndActorPlayerIdAndActionCode(
            roomsId, actor.getPlayerId(), code.name()) >= code.maxUses()) {
            throw error(HttpStatus.FORBIDDEN, "No ability uses remain");
        }

        RoomPlayerEntity target = players.findByRoomIdAndPlayerId(roomsId, request.targetPlayerId())
            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Target player not found"));
        if (target.isAlive() != code.requiresLivingTarget()) {
            throw error(HttpStatus.BAD_REQUEST, "Invalid target life state");
        }
        if (!code.allowsSelfTarget() && Objects.equals(actor.getPlayerId(), target.getPlayerId())) {
            throw error(HttpStatus.BAD_REQUEST, "Cannot target yourself");
        }
        if (pirateAttack && outcome.factionOf(target) == Faction.PIRATE) {
            throw error(HttpStatus.BAD_REQUEST, "Pirates cannot attack a teammate");
        }
        if (code == ActionCode.PROTECT
            && Objects.equals(actor.getPlayerId(), target.getPlayerId())
            && room.getNightNumber() > 1) {
            RoomActionEntity last = actions
                .findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
                    roomsId, actor.getPlayerId(), RoomPhase.NIGHT,
                    room.getNightNumber() - 1, ActionCode.PROTECT.name())
                .orElse(null);
            if (last != null && Objects.equals(last.getTargetPlayerId(), actor.getPlayerId())) {
                // 지난 밤 행동이 차단됐더라도 '자기 보호 선택' 자체가 연속이었다면 금지한다.
                throw error(HttpStatus.CONFLICT, "Cannot protect yourself on consecutive nights");
            }
        }

        if (pirateAttack) {
            // 같은 밤에 여러 번 제출할 수 있다. 마지막으로 저장된 선택이 팀의 공유 대상이다.
            PirateAttackSelectionEntity selection = pirateSelections.saveAndFlush(
                new PirateAttackSelectionEntity(roomsId, room.getNightNumber(),
                    actor.getPlayerId(), request.requestId(), request.targetPlayerId()));
            return ActionResponse.from(selection);
        }

        RoomActionEntity action = actions.saveAndFlush(new RoomActionEntity(
            roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(),
            request.requestId(), code.name(), request.targetPlayerId()));
        if (code == ActionCode.DAY_SHOOT) {
            // 낮 사격은 타이머를 기다리지 않는다. 같은 트랜잭션에서 사망·공개 보고·승리를 갱신한다.
            target.kill();
            action.resolve();
            for (RoomPlayerEntity recipient : players.findByRoomIdOrderByPlayerIdAsc(roomsId)) {
                reports.save(new RoomReportEntity(roomsId, recipient.getPlayerId(), room.getNightNumber(),
                    "DAY_SHOOT", actor.getPlayerId() + "번 포수가 " + target.getPlayerId() + "번을 처형했습니다."));
            }
            outcome.check(room, players.findByRoomIdOrderByPlayerIdAsc(roomsId));
        }
        return ActionResponse.from(action);
    }

    private AbilityView ability(Long roomsId, RoomPlayerEntity actor, ActionCode code) {
        // 남은 횟수는 room_players 컬럼이 아니라 실제 제출 기록에서 계산한다.
        int remaining = code.maxUses() < 0 ? -1 : Math.max(0,
            code.maxUses() - (int) actions.countByRoomIdAndActorPlayerIdAndActionCode(
                roomsId, actor.getPlayerId(), code.name()));
        return new AbilityView(code.name(), remaining, code.phase());
    }

    private RoleEntity role(String code) {
        return roles.findById(code)
            .orElseThrow(() -> error(HttpStatus.INTERNAL_SERVER_ERROR, "Assigned role is missing"));
    }

    private RoomEntity room(Long roomsId) {
        return rooms.findById(roomsId)
            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
    }

    private RoomPlayerEntity member(Long roomsId, Long userId) {
        return players.findByRoomIdAndUserId(roomsId, userId)
            .orElseThrow(() -> error(HttpStatus.FORBIDDEN, "Not a room member"));
    }

    private static ActionCode parseAction(String code) {
        try {
            return ActionCode.valueOf(code);
        } catch (IllegalArgumentException e) {
            throw error(HttpStatus.BAD_REQUEST, "Unknown actionCode");
        }
    }

    private static ResponseStatusException error(HttpStatus status, String message) {
        return new ResponseStatusException(status, message);
    }
}
