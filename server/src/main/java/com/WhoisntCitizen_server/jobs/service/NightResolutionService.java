package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

import com.WhoisntCitizen_server.lobby.repository.RoomRepository;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.server.ResponseStatusException;

/**
 * 한 밤의 행동을 동시에 판정한다. 게임 진행 타이머가 밤을 끝낼 때 내부에서 한 번 호출한다.
 * 처리 순서: 차단 → 방문 기록/보호 → 해적 공격 → 개인 조사 보고 → 승리/낮 전환.
 * 행동 제출 순서는 결과에 영향을 주지 않는다. 새 밤 능력의 우선순위를 바꿀 때 이 순서를 검토한다.
 */
@Service
public class NightResolutionService {
    private final RoomRepository rooms;
    private final RoomPlayerRepository players;
    private final RoomActionRepository actions;
    private final PirateAttackSelectionRepository pirateSelections;
    private final RoomReportRepository reports;
    private final RoomOutcomeService outcome;

    public NightResolutionService(RoomRepository rooms, RoomPlayerRepository players,
                                  RoomActionRepository actions,
                                  PirateAttackSelectionRepository pirateSelections,
                                  RoomReportRepository reports,
                                  RoomOutcomeService outcome) {
        this.rooms = rooms;
        this.players = players;
        this.actions = actions;
        this.pirateSelections = pirateSelections;
        this.reports = reports;
        this.outcome = outcome;
    }

    @Transactional
    public void resolveNight(Long roomsId) {
        // 행동 제출도 같은 방 잠금을 사용한다. 밤 종료와 마지막 제출이 겹쳐도 서로 섞이지 않는다.
        RoomEntity room = rooms.lockById(roomsId)
            .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "Room not found"));
        if (room.getPhase() != RoomPhase.NIGHT) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Room is not in the night phase");
        }

        List<RoomPlayerEntity> roster = players.findByRoomIdOrderByPlayerIdAsc(roomsId);
        Map<Long, RoomPlayerEntity> byId = new HashMap<>();
        roster.forEach(player -> byId.put(player.getPlayerId(), player));
        List<RoomActionEntity> submitted = actions.findByRoomIdAndPhaseAndRoundNumberOrderByIdAsc(
            roomsId, RoomPhase.NIGHT, room.getNightNumber());
        // 이번 밤의 제출만 읽는다. 다른 밤 기록은 능력 횟수 계산에는 쓰이지만 이번 판정에는 쓰지 않는다.

        // 실제 갑판장의 차단은 동시에 적용한다. 갑판장끼리 막아도 이미 낸 차단 행동은 유지한다.
        Set<Long> blocked = submitted.stream()
            .filter(action -> action.getActionCode().equals(ActionCode.BLOCK.name()))
            .filter(action -> "CREW_BOATSWAIN".equals(byId.get(action.getActorPlayerId()).getRoleCode()))
            .map(RoomActionEntity::getTargetPlayerId).collect(Collectors.toSet());

        List<RoomActionEntity> unblocked = submitted.stream()
            .filter(action -> !blocked.contains(action.getActorPlayerId())
                || action.getActionCode().equals(ActionCode.BLOCK.name()))
            .toList();
        // 감시자가 보는 '방문'과 실제 효과는 다르다. 옛 버전의 해적 투표는 방문이 아니며,
        // 이번 밤 최종 공격에 한해 아래에서 실행 해적 한 명의 방문을 추가한다.
        // 원숭이는 효과가 없어도 차단되지 않았다면 방문 흔적은 남는다.
        List<Visit> visits = new ArrayList<>();
        for (RoomActionEntity action : unblocked) {
            if (!action.getActionCode().equals(ActionCode.TEAM_ATTACK_VOTE.name())) {
                visits.add(new Visit(action.getActorPlayerId(), action.getTargetPlayerId(),
                    action.getActionCode()));
            }
        }
        List<RoomActionEntity> effective = unblocked.stream()
            // 위장 행동은 받되 실제 보호·차단·조사 효과에는 포함하지 않는다.
            .filter(action -> !"CREW_MONKEY".equals(byId.get(action.getActorPlayerId()).getRoleCode()))
            .toList();

        Set<Long> protectedPlayers = effective.stream()
            .filter(action -> action.getActionCode().equals(ActionCode.PROTECT.name()))
            .map(RoomActionEntity::getTargetPlayerId).collect(Collectors.toSet());

        // 공유 대상은 이번 밤 마지막 선택이다. 표 수는 세지 않는다. 아무도 선택하지 않았다면 공격도 없다.
        PirateAttackSelectionEntity selected = pirateSelections
            .findTopByRoomIdAndNightNumberOrderByIdDesc(roomsId, room.getNightNumber())
            .orElse(null);
        // 살아 있고 차단되지 않은 해적 한 명이 공격을 실행한다. 전원이 차단되면 공격할 수 없다.
        RoomPlayerEntity executor = roster.stream()
            .filter(RoomPlayerEntity::isAlive)
            .filter(player -> "PIRATE_RAIDER".equals(player.getRoleCode()))
            .filter(player -> !blocked.contains(player.getPlayerId()))
            .findFirst().orElse(null); // roster는 playerId 오름차순이다.
        if (selected != null && executor != null) {
            Long victimId = selected.getTargetPlayerId();
            visits.add(new Visit(executor.getPlayerId(), victimId, "PIRATE_ATTACK"));
            RoomPlayerEntity victim = byId.get(victimId);
            if (victim != null && victim.isAlive() && !protectedPlayers.contains(victimId)) {
                victim.kill();
                for (RoomPlayerEntity recipient : roster) {
                    report(roomsId, recipient.getPlayerId(), room.getNightNumber(),
                        "NIGHT_DEATH", victimId + "번 플레이어가 밤에 사망했습니다.");
                }
            }
        }

        for (RoomActionEntity action : effective) {
            // 결과는 수신자별 room_reports에 적는다. GET /me/reports가 본인 것만 반환한다.
            RoomPlayerEntity actor = byId.get(action.getActorPlayerId());
            if (!actor.isAlive()) continue; // 죽은 플레이어에게 조사 결과를 전달하지 않는다.
            Long targetId = action.getTargetPlayerId();
            switch (ActionCode.valueOf(action.getActionCode())) {
                case INVESTIGATE_FACTION -> report(roomsId, actor.getPlayerId(), room.getNightNumber(),
                    "FACTION_RESULT", targetId + "번의 진영: " + outcome.factionOf(byId.get(targetId)));
                case WATCH_VISITORS -> {
                    String visitorIds = visits.stream()
                        .filter(visit -> visit.targetId().equals(targetId)
                            && !visit.actorId().equals(actor.getPlayerId()))
                        .map(visit -> visit.actorId().toString()).distinct().sorted()
                        .collect(Collectors.joining(", "));
                    report(roomsId, actor.getPlayerId(), room.getNightNumber(), "VISITORS",
                        targetId + "번 방문자: " + (visitorIds.isEmpty() ? "없음" : visitorIds));
                }
                case WATCH_ACTION -> {
                    // 앵무새는 방문한 행동과 실제 해적 공격을 본다. 대상 변경 이력은 방문이 아니다.
                    String watched = visits.stream()
                        .filter(visit -> visit.actorId().equals(targetId))
                        .map(visit -> visit.actionCode() + " → " + visit.targetId())
                        .collect(Collectors.joining(", "));
                    report(roomsId, actor.getPlayerId(), room.getNightNumber(), "WATCHED_ACTION",
                        targetId + "번의 행동: " + (watched.isEmpty() ? "없음" : watched));
                }
                case READ_CORPSE_ROLE -> report(roomsId, actor.getPlayerId(), room.getNightNumber(),
                    "CORPSE_ROLE", targetId + "번의 직업: " + byId.get(targetId).getRoleCode());
                default -> { /* 차단·보호와 이전 버전의 투표 기록은 위에서 처리했거나 무시한다. */ }
            }
        }

        // 원숭이의 위장 조사는 그럴듯한 반대 결과를 준다. 다른 위장 행동은 효과가 없다.
        for (RoomActionEntity action : unblocked) {
            RoomPlayerEntity actor = byId.get(action.getActorPlayerId());
            if (actor.isAlive() && "CREW_MONKEY".equals(actor.getRoleCode())
                && action.getActionCode().equals(ActionCode.INVESTIGATE_FACTION.name())) {
                Faction actual = outcome.factionOf(byId.get(action.getTargetPlayerId()));
                Faction falseResult = actual == Faction.PIRATE ? Faction.CREW : Faction.PIRATE;
                report(roomsId, actor.getPlayerId(), room.getNightNumber(), "FACTION_RESULT",
                    action.getTargetPlayerId() + "번의 진영: " + falseResult);
            }
        }

        submitted.forEach(RoomActionEntity::resolve);
        // 사망 적용 후 최종 인원으로 승리를 확인한다. 승자가 있으면 DAY로 되돌리지 않는다.
        outcome.check(room, roster);
        if (room.getPhase() != RoomPhase.GAME_OVER) room.completeNight();
    }

    /** 낮 진행 타이머가 끝날 때 내부에서 호출한다. 밤 번호를 올려 새 행동을 받는다. */
    @Transactional
    public void beginNextNight(Long roomsId) {
        RoomEntity room = rooms.lockById(roomsId)
            .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "Room not found"));
        if (room.getPhase() != RoomPhase.DAY) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Room is not in the day phase");
        }
        room.beginNextNight();
    }

    private void report(Long roomsId, Long recipientPlayerId, int round,
                        String type, String message) {
        reports.save(new RoomReportEntity(roomsId, recipientPlayerId, round, type, message));
    }

    private record Visit(Long actorId, Long targetId, String actionCode) {}
}
