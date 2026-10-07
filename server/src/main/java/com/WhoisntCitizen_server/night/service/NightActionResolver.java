package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightAction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * 3~4. 밤 능력 처리. 제출 순서와 관계없이 아래 순서로 한 번에 판정한다.
 *   1. 차단  2. 차단 적용  3. 방문 기록  4. 유효 행동(원숭이 제외)
 *   5. 보호  5-1. 유혹  6. 공격  6-1. 크라켄  6-2. 투표 금지
 *   7. 개인 결과(차단·투표 금지·표식 당한 사람 안내 포함)  8. 원숭이 가짜 결과  9. 사용 기록
 * - SELECT_ATTACK_TARGET(해적): 해적 각자 선택, 최다 득표 대상 처치 (동률이면 무작위). 표를 준 해적 중 1명이 방문한다.
 * - PROTECT(선의): 보호 대상이 처치 대상과 같으면 생존
 * - INVESTIGATE_FACTION(선장): 대상 진영 조사
 * - READ_CORPSE_ROLE(주정뱅이): 시체의 실제 직업 확인 (게임당 2회)
 * - BLOCK(갑판장): 대상의 그날 밤 행동을 무효로 한다. 차단끼리는 동시에 적용한다.
 *   갑판장은 차단 결과(BLOCK)를, 차단당해 능력이 무효가 된 사람은 본인에게만 차단 안내(BLOCKED)를 받는다.
 * - WATCH_VISITORS(망루지기): 대상을 방문한 사람 목록
 * - WATCH_ACTION(앵무새): 대상이 한 행동 목록. 해적을 지목하면 제출 즉시 접선(Game)하고 밤 결과는 주지 않는다.
 * - BAN_VOTE(요리사): 대상은 다음 투표를 할 수 없고, 투표가 그 사람을 기다리지 않는다. 요리사는 VOTE_BAN, 대상은 본인만 VOTE_BANNED 결과를 받는다.
 * - SEDUCE(세이렌): 대상이 선원 팀이거나 인어면 세이렌 팀으로 만든다(성공하면 다음 밤은 쉰다). 공격보다 먼저 판정하므로
 *   같은 밤에 죽은 대상도 세이렌 팀(사망)이 된다. 세이렌은 성공·실패를, 대상은 본인만 SEDUCED 결과를 받는다. 선의의 보호로는 막지 못한다.
 * - KRAKEN_MARK(크라켄): 대상에게 표식을 남긴다(방문). 대상은 본인만 KRAKEN_MARKED 결과를 받는다.
 * - KRAKEN_STRIKE(크라켄): 표식된 생존자를 모두 처치하고 표식을 지운다(방문 아님). 선의가 보호한 사람은 살아남는다.
 * - 유령 선장은 밤에 죽지 않는다(해적의 습격·크라켄 모두). 습격당해도 아무 일 없던 것처럼 공개한다.
 * - 원숭이: 위장 직업의 능력을 제출하지만 효과가 없다. 방문 흔적과 사용 횟수는 진짜와 똑같이 남고, 가짜 결과를 받는다.
 * 개인 결과는 행동한 본인에게만 공개하며, 이번 밤에 죽은 행동자도 받는다.
 * 개인 결과는 능력이 실제로 적용됐을 때만 만든다(차단당하면 BLOCKED만). 해적의 공격과 선의의 보호는 개인 결과 없이 전체 공개 결과로 알린다.
 */
@Component
public class NightActionResolver {

    // 원숭이 망루지기가 받는 가짜 방문자 수의 최대값
    private static final int MAX_FAKE_VISITORS = 2;

    private final Random random;

    public NightActionResolver(Random random) {
        this.random = random;
    }

    //밤 Actions 처리 및 결과 반환
    public NightResult resolve(Game game) {
        List<NightAction> submitted = game.getNightActions().values().stream()
                .filter(a -> game.getPlayer(a.actorId()).isAlive())
                .toList();
        // 밤 시작 시점(공격 처리 전)의 생존자. 원숭이 망루지기의 가짜 방문자 후보로 쓴다.
        List<Long> aliveAtNightStart = game.getPlayers().stream()
                .filter(GamePlayer::isAlive)
                .map(GamePlayer::getPlayerId)
                .toList();

        // 1. 차단. 원숭이의 위장 BLOCK은 효과가 없으므로 실제 갑판장의 차단만 센다.
        Set<Long> blocked = targetsOf(withoutMonkeys(game, submitted), ActionCode.BLOCK);

        // 2. 차단 적용. 차단 행동 자체는 막히지 않으므로 갑판장끼리 서로 막아도 둘 다 적용된다.
        List<NightAction> unblocked = submitted.stream()
                .filter(a -> a.code() == ActionCode.BLOCK || !blocked.contains(a.actorId()))
                .toList();

        // 3. 방문 기록. 해적의 공격 선택은 방문이 아니며, 6단계에서 실행자 1명의 방문만 추가한다. 크라켄의 발동도 방문이 아니다.
        List<NightAction> visits = new ArrayList<>(unblocked.stream()
                .filter(a -> a.code() != ActionCode.SELECT_ATTACK_TARGET && a.code() != ActionCode.KRAKEN_STRIKE)
                .toList());

        // 4. 실제 효과가 있는 행동. 원숭이 행동은 빠지지만 방문 흔적(3)은 남는다.
        List<NightAction> effective = withoutMonkeys(game, unblocked);

        // 5. 보호
        Set<Long> protectedIds = targetsOf(effective, ActionCode.PROTECT);

        Map<Long, List<PrivateReport>> reports = new LinkedHashMap<>();

        // 5-1. 유혹(세이렌). 공격보다 먼저 판정해 같은 밤에 죽은 대상도 세이렌 팀이 된다.
        for (NightAction action : effective) {
            if (action.code() == ActionCode.SEDUCE) {
                seduce(game, action, reports);
            }
        }

        // 6. 공격. 유령 선장은 밤에 죽지 않으며, 습격당해도 아무 일 없던 것처럼 공개한다(보호보다 먼저 본다).
        List<NightResult.Death> deaths = new ArrayList<>();
        Long attackTargetId = pickMostVoted(countVotes(effective));
        Long killedId = null;
        boolean saved = false;
        if (attackTargetId != null) {
            visits.add(new NightAction(pickExecutor(effective, attackTargetId),
                    ActionCode.SELECT_ATTACK_TARGET, attackTargetId));
            GamePlayer target = game.getPlayer(attackTargetId);
            if (!target.isGhostCaptain()) {
                if (protectedIds.contains(attackTargetId)) {
                    saved = true;
                } else {
                    //사망 처리
                    target.kill(DeathCause.ATTACK);
                    killedId = attackTargetId;
                    deaths.add(new NightResult.Death(attackTargetId, DeathCause.ATTACK));
                }
            }
        }

        // 6-1. 크라켄. 표식을 먼저 남기고, 발동하면 표식된 생존자를 모두 처치한 뒤 표식을 지운다.
        Set<Long> markedIds = new LinkedHashSet<>();
        for (NightAction action : effective) {
            if (action.code() == ActionCode.KRAKEN_MARK) {
                game.getPlayer(action.actorId()).addKrakenMark(action.targetId());
                markedIds.add(action.targetId());
            }
        }
        for (NightAction action : effective) {
            if (action.code() != ActionCode.KRAKEN_STRIKE) {
                continue;
            }
            GamePlayer kraken = game.getPlayer(action.actorId());
            for (Long markedId : game.aliveKrakenMarks(kraken)) {
                GamePlayer marked = game.getPlayer(markedId);
                if (marked.isGhostCaptain()) {
                    continue;
                }
                if (protectedIds.contains(markedId)) {
                    saved = true;
                } else {
                    marked.kill(DeathCause.KRAKEN);
                    deaths.add(new NightResult.Death(markedId, DeathCause.KRAKEN));
                }
            }
            kraken.clearKrakenMarks();
        }

        // 6-2. 투표 금지(요리사). 대상은 다음 투표를 할 수 없고(Game.recordVote), 다음 밤이 되면 풀린다.
        Set<Long> voteBannedIds = targetsOf(effective, ActionCode.BAN_VOTE);
        voteBannedIds.forEach(game::banVote);

        // 7. 개인 결과. 이번 밤에 죽은 행동자도 결과를 받는다(죽은 자 채팅을 보는 직업 등을 위해).
        // 7-1. 차단당한 사람에게 본인만 보는 안내. 원숭이도 진짜와 똑같이 받는다(차단 행동 자체는 막히지 않으므로 갑판장은 제외).
        for (NightAction action : submitted) {
            if (action.code() != ActionCode.BLOCK && blocked.contains(action.actorId())) {
                reports.computeIfAbsent(action.actorId(), id -> new ArrayList<>()).add(PrivateReport.blocked());
            }
        }
        // 7-2. 요리사에게 당한 사람에게 본인만 보는 안내. 이번 밤에 죽었으면 투표가 없으므로 주지 않는다.
        for (Long bannedId : voteBannedIds) {
            if (game.getPlayer(bannedId).isAlive()) {
                reports.computeIfAbsent(bannedId, id -> new ArrayList<>()).add(PrivateReport.voteBanned());
            }
        }
        // 7-3. 크라켄의 표식이 남은 사람에게 본인만 보는 안내. 이번 밤에 죽었으면 주지 않는다.
        for (Long markedId : markedIds) {
            if (game.getPlayer(markedId).isAlive()) {
                reports.computeIfAbsent(markedId, id -> new ArrayList<>()).add(PrivateReport.krakenMarked());
            }
        }
        for (NightAction action : effective) {
            PrivateReport report = reportOf(game, action, visits);
            if (report != null) {
                reports.computeIfAbsent(action.actorId(), id -> new ArrayList<>()).add(report);
            }
        }

        // 8. 원숭이 가짜 결과. 차단당하지 않은 원숭이만 받는다(진짜도 차단당하면 결과가 없다).
        for (NightAction action : unblocked) {
            if (!game.getPlayer(action.actorId()).isMonkey()) {
                continue;
            }
            PrivateReport fake = fakeReportOf(game, action, aliveAtNightStart);
            if (fake != null) {
                reports.computeIfAbsent(action.actorId(), id -> new ArrayList<>()).add(fake);
            }
        }

        // 9. 사용 기록. 차단당한 행동은 횟수를 차감하지 않는다. 같은 밤에 여러 번 바꿔도 1회만 차감된다.
        //    effective가 아니라 unblocked를 쓴다: 원숭이도 위장 능력의 횟수는 똑같이 써야 들키지 않는다.
        recordUses(game, unblocked);

        return new NightResult(game.getDay(), killedId, saved, List.copyOf(deaths), freeze(reports));
    }

    /**
     * 세이렌의 유혹 한 건. 대상이 아직 선원 팀이거나 유혹되지 않은 인어면 세이렌 팀으로 만들고, 아니면 실패한다.
     * 세이렌은 성공·실패를 알고, 성공했을 때만 다음 밤에 쉰다. 유혹당한 사람은 세이렌 팀 동료를 함께 받는다.
     */
    private void seduce(Game game, NightAction action, Map<Long, List<PrivateReport>> reports) {
        GamePlayer siren = game.getPlayer(action.actorId());
        GamePlayer target = game.getPlayer(action.targetId());
        boolean success = target.isSeducible();
        if (success) {
            target.joinSirenTeam(Instant.now());
            siren.recordSeduceSuccess(game.getDay());
            List<Long> teammates = game.knownSirenTeam(target).stream().map(GamePlayer::getPlayerId).toList();
            reports.computeIfAbsent(target.getPlayerId(), id -> new ArrayList<>()).add(PrivateReport.seduced(teammates));
        }
        reports.computeIfAbsent(siren.getPlayerId(), id -> new ArrayList<>())
                .add(PrivateReport.seduce(target.getPlayerId(), success));
    }

    /** 행동 하나가 만드는 개인 결과. 결과가 없는 능력(보호, 공격, 발동)과 따로 만드는 결과(유혹)는 null. */
    private PrivateReport reportOf(Game game, NightAction action, List<NightAction> visits) {
        if (!action.code().needsTarget()) {
            return null;
        }
        GamePlayer target = game.getPlayer(action.targetId());
        return switch (action.code()) {
            case INVESTIGATE_FACTION -> PrivateReport.faction(target.getPlayerId(), investigatedFaction(target));
            case READ_CORPSE_ROLE -> PrivateReport.corpseRole(target.getPlayerId(), target.getRole());
            case WATCH_VISITORS -> PrivateReport.visitors(target.getPlayerId(),
                    visitorsOf(visits, target.getPlayerId(), action.actorId()));
            // 해적을 지목한 앵무새는 제출 시점의 접선으로 대신하고, 그 해적이 누구를 지목했는지는 알려 주지 않는다.
            case WATCH_ACTION -> target.isRaider()
                    ? null
                    : PrivateReport.actions(target.getPlayerId(), actionsOf(visits, target.getPlayerId()));
            case BLOCK -> PrivateReport.block(target.getPlayerId());
            case BAN_VOTE -> PrivateReport.voteBan(target.getPlayerId());
            case KRAKEN_MARK -> PrivateReport.krakenMark(target.getPlayerId());
            default -> null; // 보호·공격은 전체 공개 결과로 알린다. 유혹은 seduce에서 만든다
        };
    }

    /** 선장이 보는 진영. 제3 세력은 선원(CREW)으로 보인다. 유혹당한 사람도 직업의 진영 그대로 보인다. */
    private static Faction investigatedFaction(GamePlayer target) {
        return target.getRole().isNeutral() ? Faction.CREW : target.getRole().faction();
    }

    /**
     * 원숭이가 위장 직업에 맞춰 받는 무작위 결과. 형식은 진짜 결과와 똑같아서 받는 쪽에서는 구분할 수 없다.
     * 선의 위장은 진짜도 결과가 없으므로 null. 갑판장 위장은 진짜와 같은 차단 결과를 받는다(효과는 없다).
     */
    private PrivateReport fakeReportOf(Game game, NightAction action, List<Long> aliveAtNightStart) {
        Long targetId = action.targetId();
        return switch (action.code()) {
            case INVESTIGATE_FACTION -> PrivateReport.faction(targetId, random.nextBoolean() ? Faction.CREW : Faction.PIRATE);
            case WATCH_VISITORS -> PrivateReport.visitors(targetId,
                    randomVisitors(aliveAtNightStart, action.actorId(), targetId));
            case READ_CORPSE_ROLE -> PrivateReport.corpseRole(targetId, randomAssignedRole(game));
            case BLOCK -> PrivateReport.block(targetId);
            default -> null;
        };
    }

    /** 밤 시작 시점 생존자 중 원숭이 본인과 대상을 뺀 0~MAX_FAKE_VISITORS명. */
    private List<Long> randomVisitors(List<Long> aliveAtNightStart, Long monkeyId, Long targetId) {
        List<Long> candidates = new ArrayList<>(aliveAtNightStart);
        candidates.removeIf(id -> id.equals(monkeyId) || id.equals(targetId));
        Collections.shuffle(candidates, random);
        int count = random.nextInt(Math.min(MAX_FAKE_VISITORS, candidates.size()) + 1);
        return candidates.subList(0, count).stream().sorted().toList();
    }

    /** 이번 게임에 배정된 실제 직업 중 하나. */
    private RoleDefinition randomAssignedRole(Game game) {
        List<RoleDefinition> assigned = game.getPlayers().stream()
                .map(GamePlayer::getRole)
                .distinct()
                .toList();
        return assigned.get(random.nextInt(assigned.size()));
    }

    private List<NightAction> withoutMonkeys(Game game, List<NightAction> actions) {
        return actions.stream()
                .filter(a -> !game.getPlayer(a.actorId()).isMonkey())
                .toList();
    }

    /** 대상을 방문한 사람. 지켜본 본인과 대상의 자기 자신 방문(예: 선의의 자기 보호)은 뺀다. */
    private List<Long> visitorsOf(List<NightAction> visits, Long targetId, Long watcherId) {
        return visits.stream()
                .filter(v -> v.targetId().equals(targetId))
                .map(NightAction::actorId)
                .filter(id -> !id.equals(watcherId) && !id.equals(targetId))
                .distinct()
                .sorted()
                .toList();
    }

    /** 대상이 한 행동(방문 기록 기준). 차단당한 행동은 보이지 않는다. */
    private List<PrivateReport.ObservedAction> actionsOf(List<NightAction> visits, Long actorId) {
        return visits.stream()
                .filter(v -> v.actorId().equals(actorId))
                .map(v -> new PrivateReport.ObservedAction(v.code(), v.targetId()))
                .toList();
    }

    /** 최종 공격 대상에게 표를 준 해적 중 무작위 1명. 망루지기·앵무새에게는 이 해적의 방문만 보인다. */
    private Long pickExecutor(List<NightAction> actions, Long attackTargetId) {
        List<Long> voters = actions.stream()
                .filter(a -> a.code() == ActionCode.SELECT_ATTACK_TARGET && a.targetId().equals(attackTargetId))
                .map(NightAction::actorId)
                .toList();
        return voters.get(random.nextInt(voters.size()));
    }

    private void recordUses(Game game, List<NightAction> actions) {
        for (NightAction action : actions) {
            GamePlayer actor = game.getPlayer(action.actorId());
            actor.recordUse(action.code());
            if (action.code() == ActionCode.PROTECT && action.actorId().equals(action.targetId())) {
                actor.recordSelfProtect(game.getDay());
            }
            if (action.code() == ActionCode.BAN_VOTE) {
                actor.recordVoteBan(game.getDay(), action.targetId()); // 차단당한 밤은 기록되지 않아 다음 밤에 같은 대상을 고를 수 있다
            }
        }
    }

    private Set<Long> targetsOf(List<NightAction> actions, ActionCode code) {
        return actions.stream()
                .filter(a -> a.code() == code)
                .map(NightAction::targetId)
                .collect(Collectors.toSet());
    }

    private Map<Long, Integer> countVotes(List<NightAction> actions) {
        Map<Long, Integer> votes = new LinkedHashMap<>();
        for (NightAction a : actions) {
            if (a.code() == ActionCode.SELECT_ATTACK_TARGET) {
                votes.merge(a.targetId(), 1, Integer::sum);
            }
        }
        return votes;
    }

    //최다 득표 수 id 반환, 동률은 랜덤
    private Long pickMostVoted(Map<Long, Integer> picks) {
        if (picks.isEmpty()) {
            return null;
        }
        int max = picks.values().stream().max(Integer::compare).orElse(0);
        List<Long> top = picks.entrySet().stream()
                .filter(e -> e.getValue() == max)
                .map(Map.Entry::getKey)
                .toList();
        return top.get(random.nextInt(top.size()));
    }

    private static Map<Long, List<PrivateReport>> freeze(Map<Long, List<PrivateReport>> reports) {
        return reports.entrySet().stream()
                .collect(Collectors.toUnmodifiableMap(Map.Entry::getKey, e -> List.copyOf(e.getValue())));
    }
}