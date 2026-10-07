package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.night.entity.NightAction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import lombok.Getter;
import lombok.AccessLevel;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 게임 한 판의 상태(Aggregate Root).
 * 상태 변경은 모두 이 클래스의 메서드를 통해서만 일어나며, 규칙 위반은 GameRuleException으로 거부한다.
 * 동시성 제어는 Service 계층에서 Game 인스턴스 단위로 synchronized 한다.
 */
@Getter
public class Game {

    private final String gameId;
    private final String roomId;
    // 전적(User.playCount/winCount)에 반영할 게임인지. 방에서 시작한 실제 게임만 true, 개발용 테스트 게임은 false
    private final boolean recordStats;
    private final Map<Long, GamePlayer> players; // 입장 순서 유지
    private final Instant createdAt;

    private GamePhase phase;
    private int day;              // 첫 밤이 1일차
    private long phaseVersion;    // 페이즈가 바뀔 때마다 증가. 오래된 타이머 작업을 무시하는 데 사용
    private Instant phaseEndsAt;

//    private final Map<Long, Long> nightActions = new LinkedHashMap<>(); // actorId -> targetId
    private final Map<Long, NightAction> nightActions = new LinkedHashMap<>(); // actorId -> 최종 제출 행동
    @Getter(AccessLevel.NONE)
    private final Set<Long> lockedActors = new HashSet<>(); // 이번 밤 행동이 확정되어 바꿀 수 없는 플레이어 (접선한 앵무새)
    @Getter(AccessLevel.NONE)
    private final Set<Long> skippedActors = new HashSet<>(); // 이번 밤 능력을 쓰지 않고 넘긴 플레이어
    private final Map<Long, Long> votes = new LinkedHashMap<>();        // voterId -> targetId
    @Getter(AccessLevel.NONE)
    private final Set<Long> confirmedVoters = new HashSet<>(); // 이번 투표에서 "투표 완료"를 누른 플레이어 (표가 없으면 기권)
    @Getter(AccessLevel.NONE)
    private final Set<Long> voteBanned = new HashSet<>(); // 요리사 때문에 오늘 투표를 못 하는 플레이어. 밤 판정 때 정하고 다음 밤에 지운다

    // 플레이어별 마지막 요청 시각. 상태 조회(폴링)가 게임 잠금 밖에서 기록하므로 동시 접근 가능한 Map을 쓴다.
    @Getter(AccessLevel.NONE)
    private final Map<Long, Instant> lastSeenAt = new ConcurrentHashMap<>();
    // 마지막으로 누군가 죽은 날. 아무도 죽지 않은 날이 이어지는지(daysWithoutDeath) 셀 때 쓴다. 시작 전은 0
    private int lastDeathDay;

    private NightResult lastNightResult;
    private ExecutionResult lastExecutionResult;
    private Winner winner;
    private List<Long> winnerIds = List.of(); // 실제로 이긴 플레이어(팀 승리면 사망자 포함). 진행 중·취소면 빈 목록
    private GameEndReason endReason; // 진행 중이면 null

    public Game(String roomId, List<GamePlayer> players) {
        this(roomId, players, false);
    }

    public Game(String roomId, List<GamePlayer> players, boolean recordStats) {
        this.gameId = UUID.randomUUID().toString();
        this.roomId = roomId;
        this.recordStats = recordStats;
        this.players = new LinkedHashMap<>();
        for (GamePlayer p : players) {
            this.players.put(p.getPlayerId(), p);
        }
        this.createdAt = Instant.now();
        // start 전
        this.phase = null;
        this.day = 0;
    }

    // ---------- 페이즈 전이 ----------

    public void changePhase(GamePhase next, Instant endsAt) {
        if (phase == GamePhase.ENDED) {
            throw new GameRuleException("이미 종료된 게임입니다.");
        }
        if (next == GamePhase.NIGHT) {
            day++;
            nightActions.clear();
            lockedActors.clear();
            skippedActors.clear();
            voteBanned.clear();
        }
        if (next == GamePhase.VOTE) {
            votes.clear();
            confirmedVoters.clear();
        }
        this.phase = next;
        this.phaseEndsAt = endsAt;
        this.phaseVersion++;
    }

    public void end(Winner winner, Collection<Long> winnerIds) {
        this.winner = winner;
        this.winnerIds = List.copyOf(winnerIds);
        this.endReason = GameEndReason.WIN;
        this.phase = GamePhase.ENDED;
        this.phaseEndsAt = null;
        this.phaseVersion++;
    }

    /** 승리 팀 없이 게임을 끝낸다. 이미 끝난 게임이면 아무것도 하지 않는다. */
    public void cancel(GameEndReason reason) {
        if (phase == GamePhase.ENDED) {
            return;
        }
        this.winner = null;
        this.winnerIds = List.of();
        this.endReason = reason;
        this.phase = GamePhase.ENDED;
        this.phaseEndsAt = null;
        this.phaseVersion++;
    }

    public boolean isCancelled() {
        return endReason != null && endReason.isCancelled();
    }

    // ---------- 접속 확인 / 이탈 ----------

    /** 게임 시작 시각으로 모두의 마지막 요청 시각을 맞춘다. (씬을 불러오는 동안 미접속으로 판정되지 않도록) */
    public void markAllSeen(Instant now) {
        players.keySet().forEach(id -> lastSeenAt.put(id, now));
    }

    /** 플레이어의 요청을 기록한다. 게임 잠금 없이 호출해도 된다. 참가자가 아니면 무시한다. */
    public void touch(Long playerId, Instant now) {
        if (playerId != null && players.containsKey(playerId)) {
            lastSeenAt.put(playerId, now);
        }
    }

    /** 마지막 요청이 cutoff보다 오래된, 아직 내보내지 않은 플레이어. (사망자 포함) */
    public List<GamePlayer> inactivePlayers(Instant cutoff) {
        return players.values().stream()
                .filter(p -> !p.isDeparted())
                .filter(p -> {
                    Instant seen = lastSeenAt.get(p.getPlayerId());
                    return seen != null && seen.isBefore(cutoff);
                })
                .toList();
    }

    /**
     * 연결이 끊긴 플레이어를 게임에서 내보낸다. 살아 있으면 사망 처리한다.
     * 이번 페이즈에 그 사람이 낸 표·밤 행동과, 그 사람을 대상으로 한 표·밤 행동(살아 있는 대상이 필요한 능력)을 지운다.
     * 지우지 않으면 남은 사람이 다 내기 전에 투표가 끝나거나, 이미 죽은 사람이 처형·습격으로 다시 발표된다.
     * 대상을 잃은 사람은 다시 고를 수 있다(접선으로 행동이 고정된 앵무새 포함).
     *
     * @return 이번에 사망 처리했으면 true
     */
    public boolean depart(Long playerId) {
        GamePlayer player = getPlayer(playerId);
        if (player.isDeparted()) {
            return false;
        }
        player.markDeparted();
        boolean died = player.isAlive();
        if (died) {
            player.kill(DeathCause.DISCONNECT);
            recordDeath();
        }

        votes.remove(playerId);
        confirmedVoters.remove(playerId);
        // 떠난 사람에게 던진 표는 지운다. 그 표로 "투표 완료"한 사람도 다시 고를 수 있게 완료를 푼다
        votes.entrySet().removeIf(e -> {
            if (playerId.equals(e.getValue())) {
                confirmedVoters.remove(e.getKey());
                return true;
            }
            return false;
        });

        nightActions.remove(playerId);
        skippedActors.remove(playerId);
        lockedActors.remove(playerId);
        Iterator<NightAction> it = nightActions.values().iterator();
        while (it.hasNext()) {
            NightAction action = it.next();
            if (action.code().requiresLivingTarget() && playerId.equals(action.targetId())) {
                it.remove();
                lockedActors.remove(action.actorId());
            }
        }
        return died;
    }

    // ---------- 사망자 없는 날 세기 ----------

    /** 오늘 누군가 죽었다. (밤 습격, 처형, 연결 끊김) */
    public void recordDeath() {
        this.lastDeathDay = day;
    }

    /** 마지막으로 누군가 죽은 뒤 지난 날 수. 1일차부터 아무도 안 죽었으면 오늘 일차와 같다. */
    public int daysWithoutDeath() {
        return day - lastDeathDay;
    }

    // ---------- 3. 밤 능력 ----------

//    public void recordNightAction(Long actorId, Long targetId) {
//        requirePhase(GamePhase.NIGHT);
//        GamePlayer actor = getAlivePlayer(actorId, "행동하는 플레이어");
//        GamePlayer target = getAlivePlayer(targetId, "대상 플레이어");
//
//        // 직업 이름이 아니라 DB에 연결된 ActionCode 규칙으로 검증한다.
//        if (!actor.getRole().hasNightAction()) {
//            throw new GameRuleException("밤에 사용할 능력이 없는 직업입니다.");
//        }
//        ActionCode code = actor.getRole().actionCode();
//        switch (code) {
//            // 현재 게임 로직이 처리하는 밤 능력: 공격(해적) / 조사(선장) / 보호(선의)
//            case SELECT_ATTACK_TARGET, INVESTIGATE_FACTION, PROTECT -> { }
//            default -> throw new GameRuleException("아직 지원하지 않는 능력입니다: " + code);
//        }
//        if (!code.allowsSelfTarget() && actor.getPlayerId().equals(target.getPlayerId())) {
//            throw new GameRuleException("자신을 대상으로 할 수 없는 능력입니다.");
//        }
////        nightActions.put(actorId, targetId);
//        nightActions.put(actorId, new NightAction(actorId, code, targetId)); // 판정 전 다시 제출은 덮어쓰기
//    }
//
//    public boolean allNightActionsSubmitted() {
//        long required = players.values().stream()
//                .filter(p -> p.isAlive() && p.getRole().hasNightAction())
//                .count();
//        return nightActions.size() >= required;
//    }
    /** 제출 시각이 중요하지 않은 곳(테스트 등)에서 쓰는 편의 메서드. */
    public boolean recordNightAction(Long actorId, Long targetId) {
        return recordNightAction(actorId, targetId, Instant.now());
    }

    /** 직업의 기본 능력(roles.action_code)으로 제출한다. */
    public boolean recordNightAction(Long actorId, Long targetId, Instant now) {
        return recordNightAction(actorId, null, targetId, now);
    }

    /**
     * 밤 행동 제출. 판정 전까지는 다시 제출하면 덮어쓴다.
     * 검증 순서: 페이즈/행동자 생존 → 행동 확정 여부 → 능력 보유 → 고를 수 있는 능력 → 남은 횟수 → 세이렌 휴식
     *           → (대상 없는 능력) 크라켄 표식 유무 / (대상 있는 능력) 대상 생존 규칙 → 자기 대상 → 연속 자기 보호 → 연속 투표 금지 → 중복 표식.
     * 사용 횟수 차감과 자기 보호·투표 금지·유혹 성공 기록은 제출이 아니라 판정 시점(NightActionResolver)에 한다.
     *
     * @param requested 고른 능력. null이면 직업의 기본 능력. 크라켄만 KRAKEN_STRIKE를 고를 수 있다(ActionCode.choices)
     * @param targetId  대상. 대상이 없는 능력(KRAKEN_STRIKE)이면 무시한다
     * @param now       접선 시각으로 기록할 현재 시각
     * @return 이번 제출로 앵무새가 해적과 접선했으면 true
     */
    public boolean recordNightAction(Long actorId, ActionCode requested, Long targetId, Instant now) {
        requirePhase(GamePhase.NIGHT);
        GamePlayer actor = getAlivePlayer(actorId, "행동하는 플레이어");
        if (lockedActors.contains(actorId)) {
            throw new GameRuleException("이번 밤 행동이 이미 확정되었습니다.");
        }

        // 직업 이름이 아니라 DB에 연결된 ActionCode 규칙으로 검증한다. 원숭이는 위장 직업(shownRole)의 능력을 쓴다.
        if (!actor.getShownRole().hasNightAction()) {
            throw new GameRuleException("밤에 사용할 능력이 없는 직업입니다.");
        }
        ActionCode primary = actor.getShownRole().actionCode();
        ActionCode code = requested == null ? primary : requested;
        if (!primary.choices().contains(code)) {
            throw new GameRuleException("이 직업이 쓸 수 없는 능력입니다: " + code);
        }
        if (!actor.hasUsesLeft(code)) {
            throw new GameRuleException("능력의 남은 사용 횟수가 없습니다.");
        }
        if (code == ActionCode.SEDUCE && actor.seducedOn(day - 1)) {
            throw new GameRuleException("유혹에 성공한 다음 밤에는 쉬어야 합니다.");
        }

        if (!code.needsTarget()) {
            if (code == ActionCode.KRAKEN_STRIKE && aliveKrakenMarks(actor).isEmpty()) {
                throw new GameRuleException("표식을 남긴 살아 있는 사람이 없습니다.");
            }
            nightActions.put(actorId, new NightAction(actorId, code, null)); // 판정 전 재제출은 덮어쓰기
            skippedActors.remove(actorId);
            return false;
        }
        if (targetId == null) {
            throw new GameRuleException("대상을 골라 주세요.");
        }
        GamePlayer target = getPlayer(targetId); // 대상 생존 여부는 능력 규칙(requiresLivingTarget)으로 검사
        if (code.requiresLivingTarget() && !target.isAlive()) {
            throw new GameRuleException("살아 있는 플레이어만 대상으로 할 수 있는 능력입니다.");
        }
        if (!code.requiresLivingTarget() && target.isAlive()) {
            throw new GameRuleException("사망한 플레이어만 대상으로 할 수 있는 능력입니다.");
        }
        boolean selfTarget = actor.getPlayerId().equals(target.getPlayerId());
        if (!code.allowsSelfTarget() && selfTarget) {
            throw new GameRuleException("자신을 대상으로 할 수 없는 능력입니다.");
        }
        if (code == ActionCode.PROTECT && selfTarget && actor.selfProtectedOn(day - 1)) {
            throw new GameRuleException("이틀 연속으로 자신을 보호할 수 없습니다.");
        }
        if (code == ActionCode.BAN_VOTE && actor.voteBannedOn(day - 1, targetId)) {
            throw new GameRuleException("어젯밤과 같은 사람은 고를 수 없습니다.");
        }
        if (code == ActionCode.KRAKEN_MARK && actor.hasKrakenMark(targetId)) {
            throw new GameRuleException("이미 표식을 남긴 사람입니다.");
        }
        nightActions.put(actorId, new NightAction(actorId, code, targetId)); // 판정 전 재제출은 덮어쓰기
        skippedActors.remove(actorId); // 넘겼다가 다시 제출하면 제출이 이긴다
        return tryContact(actor, target, code, now);
    }

    /**
     * 이번 밤 능력을 쓰지 않고 넘긴다. 이미 제출한 행동이 있으면 취소된다(마지막 선택이 이긴다).
     * 능력이 없는 직업은 원래 제출할 필요가 없으므로 거부하고, 접선으로 행동이 확정된 앵무새도 거부한다.
     */
    public void skipNightAction(Long actorId) {
        requirePhase(GamePhase.NIGHT);
        GamePlayer actor = getAlivePlayer(actorId, "행동하는 플레이어");
        if (lockedActors.contains(actorId)) {
            throw new GameRuleException("이번 밤 행동이 이미 확정되었습니다.");
        }
        if (!actor.getShownRole().hasNightAction()) {
            throw new GameRuleException("밤에 사용할 능력이 없는 직업입니다.");
        }
        nightActions.remove(actorId);
        skippedActors.add(actorId);
    }

    /**
     * 앵무새가 살아 있는 해적을 지목하면 제출 즉시 접선한다.
     * 갑판장의 차단은 밤 판정 때 적용되므로 접선을 막지 못한다. 접선한 밤에는 앵무새의 행동을 고정한다.
     */
    private boolean tryContact(GamePlayer actor, GamePlayer target, ActionCode code, Instant now) {
        if (code != ActionCode.WATCH_ACTION || !actor.isParrot() || actor.isContacted() || !target.isRaider()) {
            return false;
        }
        actor.markContacted(now);
        lockedActors.add(actor.getPlayerId());
        return true;
    }

    /**
     * me가 알고 있는 해적 진영 동료(본인 제외, 사망자 포함).
     * 해적 진영은 처음부터 서로 알고(해적, 요리사), 앵무새만 접선한 뒤에 서로 안다. (GamePlayer.isActivePirate)
     * 해적 전용 정보(동료 목록, 해적 채팅 등)의 권한은 반드시 이 메서드로 판단한다. isPirate()를 쓰면 접선 전 앵무새가 드러난다.
     */
    public List<GamePlayer> knownPirateAllies(GamePlayer me) {
        if (!me.isActivePirate()) {
            return List.of();
        }
        return players.values().stream()
                .filter(p -> !p.getPlayerId().equals(me.getPlayerId()))
                .filter(GamePlayer::isActivePirate)
                .toList();
    }

    /**
     * me가 알고 있는 세이렌 팀 동료(본인 제외, 사망자 포함). 세이렌 팀이면 처음부터(세이렌) 또는 유혹당한 뒤부터 서로 안다.
     */
    public List<GamePlayer> knownSirenTeam(GamePlayer me) {
        if (me.getTeam() != Team.SIREN) {
            return List.of();
        }
        return players.values().stream()
                .filter(p -> !p.getPlayerId().equals(me.getPlayerId()))
                .filter(p -> p.getTeam() == Team.SIREN)
                .toList();
    }

    /** 크라켄이 표식을 남긴 사람 중 살아 있는 사람 (표식 순서) */
    public List<Long> aliveKrakenMarks(GamePlayer kraken) {
        return kraken.getKrakenMarks().stream()
                .filter(id -> getPlayer(id).isAlive())
                .toList();
    }

    /**
     * 이번 밤에 능력을 쓸 수 있는 생존자가 모두 제출하거나 넘겼는지.
     * 쓸 수 없는 사람까지 세면 밤이 일찍 끝나지 않는다.
     */
    public boolean allNightActionsSubmitted() {
        return players.values().stream()
                .filter(p -> canActOn(p, day))
                .allMatch(p -> nightActions.containsKey(p.getPlayerId()) || skippedActors.contains(p.getPlayerId()));
    }

    /** 이번 밤(밤이 아니면 다음 밤)에 능력을 쓸 수 있는지. 내 역할 조회에서 클라이언트가 능력 버튼을 켤지 정하는 데 쓴다. */
    public boolean canUseAbilityTonight(GamePlayer p) {
        return canActOn(p, phase == GamePhase.NIGHT ? day : day + 1);
    }

    private boolean canActOn(GamePlayer p, int nightDay) {
        if (!p.isAlive() || !p.getShownRole().hasNightAction()) {
            return false;
        }
        ActionCode code = p.getShownRole().actionCode();
        if (!p.hasUsesLeft(code)) {
            return false;
        }
        // 세이렌은 유혹에 성공한 다음 밤에 쉰다.
        if (code == ActionCode.SEDUCE && p.seducedOn(nightDay - 1)) {
            return false;
        }
        // 시체 대상 능력(주정뱅이)은 사망자가 있어야 쓸 수 있다.
        return code.requiresLivingTarget() || players.values().stream().anyMatch(o -> !o.isAlive());
    }

    // ---------- 6. 투표 ----------

    /** 투표 + 투표 완료 (기존 방식: 한 번 내면 제출한 것으로 본다). 재투표 시 덮어쓰기 */
    public void recordVote(Long voterId, Long targetId) {
        recordVote(voterId, targetId, true);
    }

    /**
     * 투표.
     * - targetId == null(또는 0) 이면 표를 거둔다(카드를 카드패로 되돌림). 이 상태로 시간이 끝나거나 완료하면 기권이다.
     * - confirm == true 이면 "투표 완료": 지금 상태(표 또는 기권)로 고정한다. 완료한 뒤에는 임시 선택(confirm=false)을 받지 않는다.
     * - confirm == false 이면 임시 선택: 시간이 끝나면 그 표가 그대로 집계된다.
     */
    public void recordVote(Long voterId, Long targetId, boolean confirm) {
        requirePhase(GamePhase.VOTE);
        getAlivePlayer(voterId, "투표하는 플레이어");
        if (voteBanned.contains(voterId)) {
            throw new GameRuleException("오늘은 투표할 수 없습니다.");
        }
        if (!confirm && confirmedVoters.contains(voterId)) {
            throw new GameRuleException("이미 투표를 완료했습니다.");
        }
        if (targetId == null || targetId == 0L) {
            votes.remove(voterId); // 표를 거둔다 (기권). Unity JsonUtility는 null을 못 보내서 0도 기권으로 본다
        } else {
            getAlivePlayer(targetId, "투표 대상");
            votes.put(voterId, targetId); // 재투표 시 덮어쓰기
        }
        if (confirm) {
            confirmedVoters.add(voterId);
        }
    }

    /** 이 플레이어가 이번 투표를 완료했는지 */
    public boolean hasConfirmedVote(Long voterId) {
        return confirmedVoters.contains(voterId);
    }

    /** 투표할 수 있는 생존자가 모두 "투표 완료"했는지(기권 포함). 요리사에게 당한 사람은 기다리지 않는다. */
    public boolean allVotesSubmitted() {
        return confirmedVoters.size() >= eligibleVoterCount();
    }

    /** 오늘 투표할 수 있는 생존자 수 (요리사에게 당한 사람 제외) */
    public long eligibleVoterCount() {
        return players.values().stream()
                .filter(p -> p.isAlive() && !voteBanned.contains(p.getPlayerId()))
                .count();
    }

    /** 요리사의 투표 금지. 밤 판정(NightActionResolver)이 호출하고, 다음 밤이 시작될 때 풀린다. */
    public void banVote(Long playerId) {
        voteBanned.add(playerId);
    }

    /** 오늘 투표를 못 하는지 (요리사). 본인 외에는 공개하지 않는다. */
    public boolean isVoteBanned(Long playerId) {
        return voteBanned.contains(playerId);
    }

    // ---------- 조회/헬퍼 ----------

    public void requirePhase(GamePhase expected) {
        if (phase != expected) {
            throw new GameRuleException("현재 페이즈(" + phase + ")에서는 할 수 없는 요청입니다. 필요 페이즈: " + expected);
        }
    }

    public GamePlayer getPlayer(Long playerId) {
        GamePlayer p = players.get(playerId);
        if (p == null) {
            throw new GameRuleException("이 게임에 참가하지 않은 플레이어입니다: " + playerId);
        }
        return p;
    }

    private GamePlayer getAlivePlayer(Long playerId, String label) {
        GamePlayer p = getPlayer(playerId);
        if (!p.isAlive()) {
            throw new GameRuleException(label + "가 이미 사망했습니다: " + playerId);
        }
        return p;
    }

    public long aliveCount() {
        return players.values().stream().filter(GamePlayer::isAlive).count();
    }

    public List<GamePlayer> getPlayers() {
        return List.copyOf(players.values());
    }

//    public Map<Long, Long> getNightActions() {
//        return Collections.unmodifiableMap(nightActions);
//    }
    public Map<Long, NightAction> getNightActions() {
        return Collections.unmodifiableMap(nightActions);
    }

    public Map<Long, Long> getVotes() {
        return Collections.unmodifiableMap(votes);
    }

    public void setLastNightResult(NightResult result) { this.lastNightResult = result; }
    public void setLastExecutionResult(ExecutionResult result) { this.lastExecutionResult = result; }

    public boolean isEnded() { return phase == GamePhase.ENDED; }
}
