package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
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

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

import com.WhoisntCitizen_server.lobby.repository.RoomRepository;
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
    // ───────────────────────── 1. 공개 직업 목록 ─────────────────────────
    // readOnly = true: 읽기만 하는 트랜잭션. 변경 감지를 생략해 조금 더 가볍다.
    @Transactional(readOnly = true)
    public RoleListResponse listRoles(Faction faction) {
        // enabled=false인 보류 직업은 기존 DB 참조를 위해 남겨 두되 새 게임 목록에는 보이지 않는다.
        // 삼항 연산자: faction이 없으면 전체, 있으면 그 진영만
        List<RoleEntity> result = faction == null
                ? roles.findByEnabledTrueOrderByCodeAsc()
                : roles.findByFactionAndEnabledTrueOrderByCodeAsc(faction);
        // 엔티티 리스트 → RoleView 리스트로 변환해 응답 record에 담는다.
        return new RoleListResponse(result.stream().map(RoleView::from).toList());
    }

    // ───────────────────────── 2. 내 직업 조회 ─────────────────────────
    @Transactional(readOnly = true)
    public MyRoleResponse getMyRole(Long roomsId, Long userId) {
        RoomEntity room = room(roomsId);            // 방이 없으면 404
        RoomPlayerEntity actor = member(roomsId, userId); // 이 방 참가자가 아니면 403
        // 직업 배정 전이면 409 Conflict("지금 상태에서는 할 수 없음")
        if (room.getPhase() == RoomPhase.SETUP || actor.getRoleCode() == null) {
            throw error(HttpStatus.CONFLICT, "Roles have not been assigned yet");
        }

        // 보이는 직업을 반환한다. 원숭이의 실제 CREW_MONKEY는 여기서 공개하지 않는다.
        RoleEntity shownRole = role(actor.getShownRoleCode());
        // 쓸 수 있는 능력 목록(최대 1개). ArrayList로 만들어 조건부로 추가한다.
        List<AbilityView> available = new ArrayList<>();
        if (shownRole.getActionCode() != null) {
            // 문자열 "PROTECT" → ActionCode.PROTECT 로 바꿔 남은 횟수까지 계산
            available.add(ability(roomsId, actor, ActionCode.valueOf(shownRole.getActionCode())));
        }
        // 동료 목록. 기본은 빈 불변 리스트.
        List<Long> allies = List.of();
        if (outcome.factionOf(actor) == Faction.PIRATE) {
            // 해적끼리는 서로 알지만 공유 공격 대상 변경은 PIRATE_RAIDER만 할 수 있다.
            allies = players.findByRoomIdOrderByPlayerIdAsc(roomsId).stream()
                    // 나 자신은 빼고
                    .filter(player -> !Objects.equals(player.getPlayerId(), actor.getPlayerId()))
                    // 해적 진영만 남기고 (죽은 동료도 포함된다)
                    .filter(player -> outcome.factionOf(player) == Faction.PIRATE)
                    // playerId만 뽑아 리스트로
                    .map(RoomPlayerEntity::getPlayerId).toList();
        }
        // 응답 조립. 람다 안에서 쓰는 actor는 "사실상 final"이어야 해서 재할당하지 않는다.
        return new MyRoleResponse(roomsId, RoleView.from(shownRole), actor.isAlive(), available, allies);
    }

    // ───────────────────────── 3. 내 보고서 조회 ─────────────────────────
    @Transactional(readOnly = true)
    public ReportsResponse getMyReports(Long roomsId, Long userId) {
        room(roomsId); // 방 존재 확인만(반환값은 안 씀). 없으면 404.
        RoomPlayerEntity actor = member(roomsId, userId); // 참가자 확인. 아니면 403.
        // recipient_player_id로 조회해 다른 플레이어의 조사 결과가 섞이지 않게 한다.
        return new ReportsResponse(reports.findByRoomIdAndRecipientPlayerIdOrderByIdAsc(
                roomsId, actor.getPlayerId()).stream().map(ReportView::from).toList());
    }

    // ───────────────────────── 4. 해적 공유 공격 대상 조회 ─────────────────────────
    @Transactional(readOnly = true)
    public PirateAttackTargetResponse getPirateAttackTarget(Long roomsId, Long userId) {
        RoomEntity room = room(roomsId);
        RoomPlayerEntity actor = member(roomsId, userId);
        // 밤이 아니면 볼 수 없다(409)
        if (room.getPhase() != RoomPhase.NIGHT) {
            throw error(HttpStatus.CONFLICT, "Attack target is visible only at night");
        }
        // 살아 있는 해적 진영만(403). 앵무새도 해적 진영이라 조회는 가능하다.
        // || 는 앞 조건이 참이면 뒤를 계산하지 않는다 → roleCode가 null이면 factionOf를 부르지 않아 안전.
        if (!actor.isAlive() || actor.getRoleCode() == null
                || outcome.factionOf(actor) != Faction.PIRATE) {
            throw error(HttpStatus.FORBIDDEN, "Only living pirates can view the attack target");
        }
        // 이번 밤의 가장 최근 선택 한 줄. 없으면 null.
        PirateAttackSelectionEntity selected = pirateSelections
                .findTopByRoomIdAndNightNumberOrderByIdDesc(roomsId, room.getNightNumber())
                .orElse(null);
        // 선택이 없으면 hasTarget=false, 두 번호는 0으로 채운다.
        return new PirateAttackTargetResponse(roomsId, room.getNightNumber(), selected != null,
                selected == null ? 0L : selected.getTargetPlayerId(),
                selected == null ? 0L : selected.getActorPlayerId());
    }

    // ───────────────────────── 5. 행동 제출 (가장 중요) ─────────────────────────
    // readOnly가 아닌 일반 트랜잭션: 저장과 사망 처리가 있다. 예외가 나면 이 메서드의 모든 변경이 취소된다.
    @Transactional
    public ActionResponse submitAction(Long roomsId, Long userId, SubmitActionRequest request) {
        // 모든 행동과 단계 전환은 방 잠금을 먼저 잡아 같은 라운드의 동시 요청을 직렬화한다.
        // (직렬화 = 동시에 와도 한 줄로 세워 하나씩 처리)
        RoomEntity room = rooms.lockById(roomsId)
                .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
        RoomPlayerEntity actor = member(roomsId, userId); // 참가자가 아니면 403

        // ── (1) 재시도 판별 ──
        // 네트워크 재시도는 현재 단계가 변하거나 능력 횟수가 소진되어도 원래 결과를 돌려준다.
        // 그래서 단계/횟수 검사보다 **먼저** 확인한다.
        RoomActionEntity previous = actions.findByRoomIdAndActorPlayerIdAndRequestId(
                roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
        if (previous != null) {
            // 같은 requestId인데 내용이 다르면 클라이언트 버그 → 409
            if (!Objects.equals(previous.getActionCode(), request.actionCode())
                    || !Objects.equals(previous.getTargetPlayerId(), request.targetPlayerId())) {
                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
            }
            // 같은 내용이면 새로 저장하지 않고 예전 결과를 그대로 돌려준다(멱등성).
            return ActionResponse.from(previous);
        }
        // 해적 선택 테이블에서도 같은 requestId를 찾아본다(행동이 두 테이블에 나뉘어 저장되므로).
        PirateAttackSelectionEntity priorSelection = pirateSelections
                .findByRoomIdAndActorPlayerIdAndRequestId(
                        roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
        if (priorSelection != null) {
            // 내용이 다르면 409
            if (!ActionCode.SELECT_ATTACK_TARGET.name().equals(request.actionCode())
                    || !Objects.equals(priorSelection.getTargetPlayerId(), request.targetPlayerId())) {
                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
            }
            // 같으면 예전 결과 반환. 새 줄을 쌓지 않으므로 "늦게 온 재시도"가 최신 대상을 되돌리지 않는다.
            return ActionResponse.from(priorSelection);
        }

        // ── (2) 새 요청 검증 ──
        ActionCode code = parseAction(request.actionCode()); // 모르는 코드면 400
        // 순서: 현재 단계/생존 → 직업 권한 → 라운드 중복/게임 횟수 → 대상 규칙 → 저장.
        // 새 능력의 세부 제한(예: 연속 자기 보호)은 저장 전 이 검증 구간에 넣는다.
        // (a) 단계: 밤 능력을 낮에 내면 409
        if (room.getPhase() != code.phase()) {
            throw error(HttpStatus.CONFLICT, "Action is unavailable in this phase");
        }
        // (b) 생존·배정: 죽었거나 직업이 없으면 403
        if (!actor.isAlive() || actor.getRoleCode() == null) {
            throw error(HttpStatus.FORBIDDEN, "Player cannot act");
        }
        // (c) 권한: **보이는 직업** 기준. 원숭이는 위장 직업의 능력을 "낼 수는" 있다(효과는 밤 판정에서 무효).
        RoleEntity shownRole = role(actor.getShownRoleCode());
        // 공유 공격 대상 변경은 해적 직업의 행동이다. 원숭이 위장 행동은 밤 판정에서 무효화된다.
        boolean pirateAttack = code == ActionCode.SELECT_ATTACK_TARGET;
        // 내 직업의 능력 코드와 다르면 403 (예: 앵무새가 SELECT_ATTACK_TARGET을 보내면 여기서 막힌다)
        if (!code.name().equals(shownRole.getActionCode())) {
            throw error(HttpStatus.FORBIDDEN, "Action is not available to this role");
        }
        // (d) 이번 단계·라운드 중복: 해적 대상 선택은 여러 번 허용하므로 검사에서 뺀다.
        if (!pirateAttack && actions.findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
                roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(), code.name()
        ).isPresent()) {
            throw error(HttpStatus.CONFLICT, "Action already submitted this phase");
        }
        // (e) 게임 전체 횟수: maxUses가 0 이상(제한 있음)일 때만, 지금까지 쓴 횟수와 비교
        if (code.maxUses() >= 0 && actions.countByRoomIdAndActorPlayerIdAndActionCode(
                roomsId, actor.getPlayerId(), code.name()) >= code.maxUses()) {
            throw error(HttpStatus.FORBIDDEN, "No ability uses remain");
        }

        // (f) 대상 규칙
        // 대상이 이 방에 없으면 404
        RoomPlayerEntity target = players.findByRoomIdAndPlayerId(roomsId, request.targetPlayerId())
                .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Target player not found"));
        // 산 대상이 필요한데 죽었거나, 죽은 대상이 필요한데(주정뱅이) 살아 있으면 400
        if (target.isAlive() != code.requiresLivingTarget()) {
            throw error(HttpStatus.BAD_REQUEST, "Invalid target life state");
        }
        // 자기 자신 선택이 금지된 능력인데 자신을 골랐으면 400
        if (!code.allowsSelfTarget() && Objects.equals(actor.getPlayerId(), target.getPlayerId())) {
            throw error(HttpStatus.BAD_REQUEST, "Cannot target yourself");
        }
        // 해적은 동료 해적을 공격 대상으로 고를 수 없다(400)
        if (pirateAttack && outcome.factionOf(target) == Faction.PIRATE) {
            throw error(HttpStatus.BAD_REQUEST, "Pirates cannot attack a teammate");
        }
        // 선의 전용 규칙: 이틀 연속 자기 자신 보호 금지
        if (code == ActionCode.PROTECT
                && Objects.equals(actor.getPlayerId(), target.getPlayerId()) // 이번에 자기 자신을 골랐고
                && room.getNightNumber() > 1) {                              // 첫 밤이 아니면(지난 밤이 존재)
            // 지난 밤(라운드 - 1)의 내 PROTECT 기록을 찾는다
            RoomActionEntity last = actions
                    .findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
                            roomsId, actor.getPlayerId(), RoomPhase.NIGHT,
                            room.getNightNumber() - 1, ActionCode.PROTECT.name())
                    .orElse(null);
            // 지난 밤에도 자기 자신을 보호했다면 409
            if (last != null && Objects.equals(last.getTargetPlayerId(), actor.getPlayerId())) {
                // 지난 밤 행동이 차단됐더라도 '자기 보호 선택' 자체가 연속이었다면 금지한다.
                throw error(HttpStatus.CONFLICT, "Cannot protect yourself on consecutive nights");
            }
        }

        // ── (3) 저장 ──
        if (pirateAttack) {
            // 같은 밤에 여러 번 제출할 수 있다. 마지막으로 저장된 선택이 팀의 공유 대상이다.
            // saveAndFlush: 저장 후 즉시 DB에 INSERT를 보내 id를 확정한다(응답에 id가 필요).
            PirateAttackSelectionEntity selection = pirateSelections.saveAndFlush(
                    new PirateAttackSelectionEntity(roomsId, room.getNightNumber(),
                            actor.getPlayerId(), request.requestId(), request.targetPlayerId()));
            return ActionResponse.from(selection); // status: "SELECTED"
        }

        // 일반 행동 저장. flush 시점에 DB 유니크 제약이 한 번 더 중복을 막는다.
        RoomActionEntity action = actions.saveAndFlush(new RoomActionEntity(
                roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(),
                request.requestId(), code.name(), request.targetPlayerId()));
        // ── (4) 포수만 즉시 효과 ──
        if (code == ActionCode.DAY_SHOOT) {
            // 낮 사격은 타이머를 기다리지 않는다. 같은 트랜잭션에서 사망·공개 보고·승리를 갱신한다.
            target.kill();    // 대상 사망 (커밋 때 UPDATE 자동)
            action.resolve(); // 상태 SUBMITTED → RESOLVED
            // 참가자 전원(죽은 사람 포함)에게 공개 보고 한 줄씩
            // ※ 메시지에 포수 번호가 들어가므로 포수 정체가 공개된다(기획 의도인지 확인 필요).
            for (RoomPlayerEntity recipient : players.findByRoomIdOrderByPlayerIdAsc(roomsId)) {
                reports.save(new RoomReportEntity(roomsId, recipient.getPlayerId(), room.getNightNumber(),
                        "DAY_SHOOT", actor.getPlayerId() + "번 포수가 " + target.getPlayerId() + "번을 처형했습니다."));
            }
            // 사망 직후 승리 검사. 해적이 모두 죽었으면 여기서 GAME_OVER가 된다.
            outcome.check(room, players.findByRoomIdOrderByPlayerIdAsc(roomsId));
        }
        return ActionResponse.from(action); // 밤 행동은 "SUBMITTED", 포수는 "RESOLVED"
    }

    // ───────────────────────── 도우미 메서드 ─────────────────────────

    // 능력 하나의 표시용 정보(코드, 남은 횟수, 단계)를 만든다.
    private AbilityView ability(Long roomsId, RoomPlayerEntity actor, ActionCode code) {
        // 남은 횟수는 room_players 컬럼이 아니라 실제 제출 기록에서 계산한다.
        // 무제한(-1)이면 -1 그대로, 제한이 있으면 (최대 - 사용 횟수)를 0 밑으로 내려가지 않게.
        int remaining = code.maxUses() < 0 ? -1 : Math.max(0,
                code.maxUses() - (int) actions.countByRoomIdAndActorPlayerIdAndActionCode(
                        roomsId, actor.getPlayerId(), code.name()));
        return new AbilityView(code.name(), remaining, code.phase());
    }

    // 직업 코드 → 직업 정의. 없으면 데이터 오류라 500.
    private RoleEntity role(String code) {
        return roles.findById(code)
                .orElseThrow(() -> error(HttpStatus.INTERNAL_SERVER_ERROR, "Assigned role is missing"));
    }

    // 방 조회(잠금 없음). 없으면 404.
    private RoomEntity room(Long roomsId) {
        return rooms.findById(roomsId)
                .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
    }

    // JWT 사용자 → 이 방 참가자 정보. 참가자가 아니면 403(남의 방 정보를 못 보게).
    private RoomPlayerEntity member(Long roomsId, Long userId) {
        return players.findByRoomIdAndUserId(roomsId, userId)
                .orElseThrow(() -> error(HttpStatus.FORBIDDEN, "Not a room member"));
    }

    // 문자열 → ActionCode. enum에 없는 이름이면 valueOf가 IllegalArgumentException을 던지므로 400으로 바꾼다.
    private static ActionCode parseAction(String code) {
        try {
            return ActionCode.valueOf(code);
        } catch (IllegalArgumentException e) {
            throw error(HttpStatus.BAD_REQUEST, "Unknown actionCode");
        }
    }

    // 예외 생성 도우미. throw error(...) 형태로 쓰려고 "던지지 않고 반환"만 한다.
    // ⚠ 스프링 기본 설정은 이 message를 응답 본문에 넣지 않는다. Unity에서 이유를 보려면 오류 응답 형식을 따로 정해야 한다.
    private static ResponseStatusException error(HttpStatus status, String message) {
        return new ResponseStatusException(status, message);
    }
//    @Transactional(readOnly = true)
//    public RoleListResponse listRoles(Faction faction) {
//        // enabled=false인 보류 직업은 기존 DB 참조를 위해 남겨 두되 새 게임 목록에는 보이지 않는다.
//        List<RoleEntity> result = faction == null
//            ? roles.findByEnabledTrueOrderByCodeAsc()
//            : roles.findByFactionAndEnabledTrueOrderByCodeAsc(faction);
//        return new RoleListResponse(result.stream().map(RoleView::from).toList());
//    }
//
//    @Transactional(readOnly = true)
//    public MyRoleResponse getMyRole(Long roomsId, Long userId) {
//        RoomEntity room = room(roomsId);
//        RoomPlayerEntity actor = member(roomsId, userId);
//        if (room.getPhase() == RoomPhase.SETUP || actor.getRoleCode() == null) {
//            throw error(HttpStatus.CONFLICT, "Roles have not been assigned yet");
//        }
//
//        // 보이는 직업을 반환한다. 원숭이의 실제 CREW_MONKEY는 여기서 공개하지 않는다.
//        RoleEntity shownRole = role(actor.getShownRoleCode());
//        List<AbilityView> available = new ArrayList<>();
//        if (shownRole.getActionCode() != null) {
//            available.add(ability(roomsId, actor, ActionCode.valueOf(shownRole.getActionCode())));
//        }
//        List<Long> allies = List.of();
//        if (outcome.factionOf(actor) == Faction.PIRATE) {
//            // 해적끼리는 서로 알지만 공유 공격 대상 변경은 PIRATE_RAIDER만 할 수 있다.
//            allies = players.findByRoomIdOrderByPlayerIdAsc(roomsId).stream()
//                .filter(player -> !Objects.equals(player.getPlayerId(), actor.getPlayerId()))
//                .filter(player -> outcome.factionOf(player) == Faction.PIRATE)
//                .map(RoomPlayerEntity::getPlayerId).toList();
//        }
//        return new MyRoleResponse(roomsId, RoleView.from(shownRole), actor.isAlive(), available, allies);
//    }
//
//    @Transactional(readOnly = true)
//    public ReportsResponse getMyReports(Long roomsId, Long userId) {
//        room(roomsId);
//        RoomPlayerEntity actor = member(roomsId, userId);
//        // recipient_player_id로 조회해 다른 플레이어의 조사 결과가 섞이지 않게 한다.
//        return new ReportsResponse(reports.findByRoomIdAndRecipientPlayerIdOrderByIdAsc(
//            roomsId, actor.getPlayerId()).stream().map(ReportView::from).toList());
//    }
//
//    @Transactional(readOnly = true)
//    public PirateAttackTargetResponse getPirateAttackTarget(Long roomsId, Long userId) {
//        RoomEntity room = room(roomsId);
//        RoomPlayerEntity actor = member(roomsId, userId);
//        if (room.getPhase() != RoomPhase.NIGHT) {
//            throw error(HttpStatus.CONFLICT, "Attack target is visible only at night");
//        }
//        if (!actor.isAlive() || actor.getRoleCode() == null
//            || outcome.factionOf(actor) != Faction.PIRATE) {
//            throw error(HttpStatus.FORBIDDEN, "Only living pirates can view the attack target");
//        }
//        PirateAttackSelectionEntity selected = pirateSelections
//            .findTopByRoomIdAndNightNumberOrderByIdDesc(roomsId, room.getNightNumber())
//            .orElse(null);
//        return new PirateAttackTargetResponse(roomsId, room.getNightNumber(), selected != null,
//            selected == null ? 0L : selected.getTargetPlayerId(),
//            selected == null ? 0L : selected.getActorPlayerId());
//    }
//
//    @Transactional
//    public ActionResponse submitAction(Long roomsId, Long userId, SubmitActionRequest request) {
//        // 모든 행동과 단계 전환은 방 잠금을 먼저 잡아 같은 라운드의 동시 요청을 직렬화한다.
//        RoomEntity room = rooms.lockById(roomsId)
//            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
//        RoomPlayerEntity actor = member(roomsId, userId);
//
//        // 네트워크 재시도는 현재 단계가 변하거나 능력 횟수가 소진되어도 원래 결과를 돌려준다.
//        RoomActionEntity previous = actions.findByRoomIdAndActorPlayerIdAndRequestId(
//            roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
//        if (previous != null) {
//            if (!Objects.equals(previous.getActionCode(), request.actionCode())
//                || !Objects.equals(previous.getTargetPlayerId(), request.targetPlayerId())) {
//                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
//            }
//            return ActionResponse.from(previous);
//        }
//        PirateAttackSelectionEntity priorSelection = pirateSelections
//            .findByRoomIdAndActorPlayerIdAndRequestId(
//                roomsId, actor.getPlayerId(), request.requestId()).orElse(null);
//        if (priorSelection != null) {
//            if (!ActionCode.SELECT_ATTACK_TARGET.name().equals(request.actionCode())
//                || !Objects.equals(priorSelection.getTargetPlayerId(), request.targetPlayerId())) {
//                throw error(HttpStatus.CONFLICT, "requestId was already used with different data");
//            }
//            return ActionResponse.from(priorSelection);
//        }
//
//        ActionCode code = parseAction(request.actionCode());
//        // 순서: 현재 단계/생존 → 직업 권한 → 라운드 중복/게임 횟수 → 대상 규칙 → 저장.
//        // 새 능력의 세부 제한(예: 연속 자기 보호)은 저장 전 이 검증 구간에 넣는다.
//        if (room.getPhase() != code.phase()) {
//            throw error(HttpStatus.CONFLICT, "Action is unavailable in this phase");
//        }
//        if (!actor.isAlive() || actor.getRoleCode() == null) {
//            throw error(HttpStatus.FORBIDDEN, "Player cannot act");
//        }
//        RoleEntity shownRole = role(actor.getShownRoleCode());
//        // 공유 공격 대상 변경은 해적 직업의 행동이다. 원숭이 위장 행동은 밤 판정에서 무효화된다.
//        boolean pirateAttack = code == ActionCode.SELECT_ATTACK_TARGET;
//        if (!code.name().equals(shownRole.getActionCode())) {
//            throw error(HttpStatus.FORBIDDEN, "Action is not available to this role");
//        }
//        if (!pirateAttack && actions.findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
//            roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(), code.name()
//        ).isPresent()) {
//            throw error(HttpStatus.CONFLICT, "Action already submitted this phase");
//        }
//        if (code.maxUses() >= 0 && actions.countByRoomIdAndActorPlayerIdAndActionCode(
//            roomsId, actor.getPlayerId(), code.name()) >= code.maxUses()) {
//            throw error(HttpStatus.FORBIDDEN, "No ability uses remain");
//        }
//
//        RoomPlayerEntity target = players.findByRoomIdAndPlayerId(roomsId, request.targetPlayerId())
//            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Target player not found"));
//        if (target.isAlive() != code.requiresLivingTarget()) {
//            throw error(HttpStatus.BAD_REQUEST, "Invalid target life state");
//        }
//        if (!code.allowsSelfTarget() && Objects.equals(actor.getPlayerId(), target.getPlayerId())) {
//            throw error(HttpStatus.BAD_REQUEST, "Cannot target yourself");
//        }
//        if (pirateAttack && outcome.factionOf(target) == Faction.PIRATE) {
//            throw error(HttpStatus.BAD_REQUEST, "Pirates cannot attack a teammate");
//        }
//        if (code == ActionCode.PROTECT
//            && Objects.equals(actor.getPlayerId(), target.getPlayerId())
//            && room.getNightNumber() > 1) {
//            RoomActionEntity last = actions
//                .findByRoomIdAndActorPlayerIdAndPhaseAndRoundNumberAndActionCode(
//                    roomsId, actor.getPlayerId(), RoomPhase.NIGHT,
//                    room.getNightNumber() - 1, ActionCode.PROTECT.name())
//                .orElse(null);
//            if (last != null && Objects.equals(last.getTargetPlayerId(), actor.getPlayerId())) {
//                // 지난 밤 행동이 차단됐더라도 '자기 보호 선택' 자체가 연속이었다면 금지한다.
//                throw error(HttpStatus.CONFLICT, "Cannot protect yourself on consecutive nights");
//            }
//        }
//
//        if (pirateAttack) {
//            // 같은 밤에 여러 번 제출할 수 있다. 마지막으로 저장된 선택이 팀의 공유 대상이다.
//            PirateAttackSelectionEntity selection = pirateSelections.saveAndFlush(
//                new PirateAttackSelectionEntity(roomsId, room.getNightNumber(),
//                    actor.getPlayerId(), request.requestId(), request.targetPlayerId()));
//            return ActionResponse.from(selection);
//        }
//
//        RoomActionEntity action = actions.saveAndFlush(new RoomActionEntity(
//            roomsId, actor.getPlayerId(), room.getPhase(), room.getNightNumber(),
//            request.requestId(), code.name(), request.targetPlayerId()));
//        if (code == ActionCode.DAY_SHOOT) {
//            // 낮 사격은 타이머를 기다리지 않는다. 같은 트랜잭션에서 사망·공개 보고·승리를 갱신한다.
//            target.kill();
//            action.resolve();
//            for (RoomPlayerEntity recipient : players.findByRoomIdOrderByPlayerIdAsc(roomsId)) {
//                reports.save(new RoomReportEntity(roomsId, recipient.getPlayerId(), room.getNightNumber(),
//                    "DAY_SHOOT", actor.getPlayerId() + "번 포수가 " + target.getPlayerId() + "번을 처형했습니다."));
//            }
//            outcome.check(room, players.findByRoomIdOrderByPlayerIdAsc(roomsId));
//        }
//        return ActionResponse.from(action);
//    }
//
//    private AbilityView ability(Long roomsId, RoomPlayerEntity actor, ActionCode code) {
//        // 남은 횟수는 room_players 컬럼이 아니라 실제 제출 기록에서 계산한다.
//        int remaining = code.maxUses() < 0 ? -1 : Math.max(0,
//            code.maxUses() - (int) actions.countByRoomIdAndActorPlayerIdAndActionCode(
//                roomsId, actor.getPlayerId(), code.name()));
//        return new AbilityView(code.name(), remaining, code.phase());
//    }
//
//    private RoleEntity role(String code) {
//        return roles.findById(code)
//            .orElseThrow(() -> error(HttpStatus.INTERNAL_SERVER_ERROR, "Assigned role is missing"));
//    }
//
//    private RoomEntity room(Long roomsId) {
//        return rooms.findById(roomsId)
//            .orElseThrow(() -> error(HttpStatus.NOT_FOUND, "Room not found"));
//    }
//
//    private RoomPlayerEntity member(Long roomsId, Long userId) {
//        return players.findByRoomIdAndUserId(roomsId, userId)
//            .orElseThrow(() -> error(HttpStatus.FORBIDDEN, "Not a room member"));
//    }
//
//    private static ActionCode parseAction(String code) {
//        try {
//            return ActionCode.valueOf(code);
//        } catch (IllegalArgumentException e) {
//            throw error(HttpStatus.BAD_REQUEST, "Unknown actionCode");
//        }
//    }
//
//    private static ResponseStatusException error(HttpStatus status, String message) {
//        return new ResponseStatusException(status, message);
//    }
}
