using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhoisntCitizen.Game
{
    /// <summary>FakeGameApi 설정. 기본값은 UI 개발용이다(짧은 페이즈, 봇 행동 켜짐).</summary>
    public sealed class FakeGameOptions
    {
        public string MyRole = RoleCodes.PirateRaider;       // 내 실제 직업 (RoleCodes)
        public string MonkeyDisguise = RoleCodes.CrewDoctor; // 내가 원숭이일 때 보이는 직업 (선장·선의·망루지기·갑판장·주정뱅이)
        public string MyNickname = "나";
        public int Seed = 42;                                // 봇이 대상을 고를 때 쓰는 난수 씨앗
        public bool BotsAct = true;                          // false면 봇이 밤 능력·투표를 하지 않는다 (결과를 고정하고 싶을 때)
        public double NightSeconds = 20;
        public double NightResultSeconds = 5;
        public double DaySeconds = 15;
        public double VoteSeconds = 15;
        public double ExecutionSeconds = 5;
    }

    /// <summary>
    /// 서버 없이 한 판을 흉내 내는 IGameApi. 나(101) + 봇 7명(해적·앵무새·선장·선의·망루지기·주정뱅이·선원, 102~108).
    /// - 판정 순서와 409 메시지는 서버 dev b41b546(Game, NightActionResolver, VoteResolver, WinConditionChecker)을 따른다.
    /// - 시간은 생성자에 넘긴 clock(단조 증가 초)으로 흐르고, API를 부를 때마다 지난 마감을 처리한다. 따로 Update를 돌리지 않는다.
    /// - 봇은 페이즈가 시작될 때 바로 행동·투표한다. 그래서 내가 제출하면(내가 할 일이 없으면 곧바로) 서버처럼 즉시 판정된다.
    /// - 콜백은 호출 안에서 바로(동기로) 불린다.
    /// - 내가 원숭이면 위장 직업으로 행동하고, 효과 없이 가짜 결과를 받는다(서버와 같음). 봇 중에는 원숭이·갑판장이 없다.
    /// - 끝난 게임도 지우지 않는다(서버는 보관 시간이 지나면 404).
    /// </summary>
    public sealed class FakeGameApi : IGameApi
    {
        public const string FakeGameId = "fake-game";
        public const long MyPlayerId = 101;

        private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Dictionary<string, RoleInfo> Roles = CreateRoles();

        // 봇: 닉네임, 실제 직업. playerId는 102부터 차례로 붙는다.
        private static readonly string[,] Bots =
        {
            { "철수", RoleCodes.PirateRaider },
            { "영희", RoleCodes.PirateParrot },
            { "민수", RoleCodes.CrewCaptain },
            { "지훈", RoleCodes.CrewDoctor },
            { "수진", RoleCodes.CrewLookout },
            { "현우", RoleCodes.CrewDrunk },
            { "유나", RoleCodes.CrewSailor }
        };

        // 서버 RoleAssigner와 같은 원숭이 위장 후보
        private static readonly string[] MonkeyDisguises =
        {
            RoleCodes.CrewCaptain, RoleCodes.CrewDoctor, RoleCodes.CrewLookout, RoleCodes.CrewBoatswain, RoleCodes.CrewDrunk
        };

        private readonly FakeGameOptions options;
        private readonly Func<double> clock;
        private readonly Random random;
        private readonly List<FakePlayer> players = new List<FakePlayer>();
        private readonly List<FakeAction> nightActions = new List<FakeAction>(); // 행동자당 1개. 다시 내면 같은 자리에서 바뀐다
        private readonly HashSet<long> skippedActors = new HashSet<long>();
        private readonly HashSet<long> lockedActors = new HashSet<long>();      // 이번 밤 접선해서 행동이 확정된 앵무새
        private readonly Dictionary<long, long> votes = new Dictionary<long, long>(); // 투표자 → 대상

        private string phase;
        private int day;
        private long phaseVersion;
        private double phaseEndsAt;
        private string winner;
        private NightResultDto lastNightResult;
        private ExecutionResultDto lastExecutionResult;

        /// <param name="options">null이면 기본값</param>
        /// <param name="clock">단조 증가 시간(초). Unity에서는 () => Time.realtimeSinceStartupAsDouble</param>
        public FakeGameApi(FakeGameOptions options, Func<double> clock)
        {
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            this.options = options ?? new FakeGameOptions();
            this.clock = clock;
            random = new Random(this.options.Seed);
            ValidateOptions(this.options);

            string shown = this.options.MyRole == RoleCodes.CrewMonkey ? this.options.MonkeyDisguise : this.options.MyRole;
            players.Add(new FakePlayer(MyPlayerId, this.options.MyNickname, this.options.MyRole, shown));
            for (int i = 0; i < Bots.GetLength(0); i++)
            {
                players.Add(new FakePlayer(MyPlayerId + 1 + i, Bots[i, 0], Bots[i, 1], Bots[i, 1]));
            }
            EnterNight(clock());
        }

        public string GameId
        {
            get { return FakeGameId; }
        }

        /// <summary>개발용: 지금 페이즈의 남은 시간을 건너뛴다 (타이머가 끝난 것처럼 처리).</summary>
        public void SkipToNextPhase()
        {
            Advance();
            if (phase != GamePhases.Ended)
            {
                OnTimeout(clock());
            }
        }

        // ================================================================ IGameApi

        public void GetState(string gameId, Action<GameApiResult<GameStateDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            var state = new GameStateDto
            {
                gameId = FakeGameId,
                phase = phase,
                day = day,
                phaseEndsAt = phase == GamePhases.Ended ? null : Iso(phaseEndsAt),
                serverTime = Iso(clock()),
                phaseVersion = phaseVersion,
                winner = winner
            };
            foreach (FakePlayer p in players)
            {
                state.players.Add(new PlayerViewDto { playerId = p.Id, nickname = p.Nickname, alive = p.Alive });
            }
            Reply(onDone, GameApiResult<GameStateDto>.Ok(state));
        }

        public void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            FakePlayer me = Me;
            RoleInfo shown = Roles[me.Shown];
            var dto = new MyRoleDto
            {
                playerId = me.Id,
                role = shown.Code,
                roleName = shown.Name,
                faction = shown.Faction,
                actionCode = shown.ActionCode,
                remainingUses = shown.ActionCode == null ? -1 : me.UsesLeft,
                alive = me.Alive,
                contacted = me.Contacted
            };
            foreach (FakePlayer ally in KnownAllies(me))
            {
                dto.mafiaTeammateIds.Add(ally.Id);
            }
            Reply(onDone, GameApiResult<MyRoleDto>.Ok(dto));
        }

        public void SubmitNightAction(string gameId, long targetId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            FakePlayer me = Me;
            FakePlayer target = Find(targetId);
            string error = CheckNightAction(me, target, targetId);
            if (error != null)
            {
                Reply(onDone, RuleViolation<NightActionResultDto>(error));
                return;
            }

            Record(me, target);
            var result = new NightActionResultDto { accepted = true };
            if (TryContact(me, target))
            {
                // 접선 직후에는 해적이 표를 바꿀 수 있도록 바로 판정하지 않는다(서버와 같음)
                foreach (FakePlayer ally in KnownAllies(me))
                {
                    result.contactedPirateIds.Add(ally.Id);
                }
            }
            else if (AllNightActionsSubmitted())
            {
                ResolveNight(clock());
            }
            result.phase = phase;
            result.phaseVersion = phaseVersion;
            Reply(onDone, GameApiResult<NightActionResultDto>.Ok(result));
        }

        public void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            FakePlayer me = Me;
            string error = CheckActor(me);
            if (error == null && me.ActionCode == null)
            {
                error = "밤에 사용할 능력이 없는 직업입니다.";
            }
            if (error != null)
            {
                Reply(onDone, RuleViolation<NightActionResultDto>(error));
                return;
            }

            nightActions.RemoveAll(a => a.ActorId == me.Id);
            skippedActors.Add(me.Id);
            if (AllNightActionsSubmitted())
            {
                ResolveNight(clock());
            }
            Reply(onDone, GameApiResult<NightActionResultDto>.Ok(
                new NightActionResultDto { accepted = true, phase = phase, phaseVersion = phaseVersion }));
        }

        public void GetNightResult(string gameId, Action<GameApiResult<NightResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            Reply(onDone, lastNightResult == null
                ? RuleViolation<NightResultDto>("아직 공개된 밤 결과가 없습니다.")
                : GameApiResult<NightResultDto>.Ok(lastNightResult));
        }

        public void Vote(string gameId, long targetId, Action<GameApiResult<VoteResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            FakePlayer target = Find(targetId);
            string error = null;
            if (phase != GamePhases.Vote)
            {
                error = PhaseError(GamePhases.Vote);
            }
            else if (!Me.Alive)
            {
                error = "투표하는 플레이어가 이미 사망했습니다: " + MyPlayerId;
            }
            else if (target == null)
            {
                error = "이 게임에 참가하지 않은 플레이어입니다: " + targetId;
            }
            else if (!target.Alive)
            {
                error = "투표 대상가 이미 사망했습니다: " + targetId; // 서버 문구 그대로
            }
            if (error != null)
            {
                Reply(onDone, RuleViolation<VoteResultDto>(error));
                return;
            }

            votes[MyPlayerId] = targetId;
            if (AllVotesSubmitted())
            {
                ResolveVote(clock());
            }
            Reply(onDone, GameApiResult<VoteResultDto>.Ok(
                new VoteResultDto { accepted = true, phase = phase, phaseVersion = phaseVersion }));
        }

        public void GetExecutionResult(string gameId, Action<GameApiResult<ExecutionResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            Reply(onDone, lastExecutionResult == null
                ? RuleViolation<ExecutionResultDto>("아직 처형 결과가 없습니다.")
                : GameApiResult<ExecutionResultDto>.Ok(lastExecutionResult));
        }

        public void GetResult(string gameId, Action<GameApiResult<GameResultDto>> onDone)
        {
            if (!Begin(gameId, onDone))
            {
                return;
            }
            var result = new GameResultDto { lastDay = day };
            if (phase == GamePhases.Ended)
            {
                result.ended = true;
                result.winner = winner;
                foreach (FakePlayer p in players)
                {
                    RoleInfo role = Roles[p.Role];
                    result.players.Add(new PlayerResultDto
                    {
                        playerId = p.Id,
                        nickname = p.Nickname,
                        role = role.Code,
                        roleName = role.Name,
                        alive = p.Alive
                    });
                }
            }
            Reply(onDone, GameApiResult<GameResultDto>.Ok(result));
        }

        // ================================================================ 페이즈 진행

        private void Advance()
        {
            double now = clock();
            while (phase != GamePhases.Ended && now >= phaseEndsAt)
            {
                OnTimeout(phaseEndsAt);
            }
        }

        private void OnTimeout(double at)
        {
            switch (phase)
            {
                case GamePhases.Night: ResolveNight(at); break;
                case GamePhases.NightResult: MoveTo(GamePhases.Day, at, options.DaySeconds); break;
                case GamePhases.Day: EnterVote(at); break;
                case GamePhases.Vote: ResolveVote(at); break;
                case GamePhases.Execution: EnterNight(at); break;
            }
        }

        private void MoveTo(string next, double at, double seconds)
        {
            phase = next;
            phaseEndsAt = at + seconds;
            phaseVersion++;
        }

        private void EnterNight(double at)
        {
            day++;
            nightActions.Clear();
            skippedActors.Clear();
            lockedActors.Clear();
            MoveTo(GamePhases.Night, at, options.NightSeconds);
            if (options.BotsAct)
            {
                BotsSubmitNightActions();
            }
            if (AllNightActionsSubmitted())
            {
                ResolveNight(at);
            }
        }

        private void EnterVote(double at)
        {
            votes.Clear();
            MoveTo(GamePhases.Vote, at, options.VoteSeconds);
            if (options.BotsAct)
            {
                BotsVote();
            }
            if (AllVotesSubmitted())
            {
                ResolveVote(at);
            }
        }

        /// <summary>승리 조건(WinConditionChecker): 해적 진영 생존 0명 → 선원 승, 해적 진영 ≥ 나머지 → 해적 승.</summary>
        private bool FinishIfWinnerDecided()
        {
            int pirates = 0;
            int others = 0;
            foreach (FakePlayer p in players)
            {
                if (!p.Alive)
                {
                    continue;
                }
                if (p.IsPirate)
                {
                    pirates++;
                }
                else
                {
                    others++;
                }
            }
            string decided = pirates == 0 ? Factions.Crew : (pirates >= others ? Factions.Pirate : null);
            if (decided == null)
            {
                return false;
            }
            winner = decided;
            phase = GamePhases.Ended;
            phaseVersion++;
            return true;
        }

        // ================================================================ 밤

        /// <summary>서버 Game.recordNightAction과 같은 순서로 검사한다. 통과하면 null.</summary>
        private string CheckNightAction(FakePlayer me, FakePlayer target, long targetId)
        {
            string error = CheckActor(me);
            if (error != null)
            {
                return error;
            }
            if (target == null)
            {
                return "이 게임에 참가하지 않은 플레이어입니다: " + targetId;
            }
            if (me.ActionCode == null)
            {
                return "밤에 사용할 능력이 없는 직업입니다.";
            }
            if (me.UsesLeft == 0)
            {
                return "능력의 남은 사용 횟수가 없습니다.";
            }
            ActionRule rule = AbilityRules.Find(me.ActionCode);
            if (rule.LivingTarget && !target.Alive)
            {
                return "살아 있는 플레이어만 대상으로 할 수 있는 능력입니다.";
            }
            if (!rule.LivingTarget && target.Alive)
            {
                return "사망한 플레이어만 대상으로 할 수 있는 능력입니다.";
            }
            bool self = target.Id == me.Id;
            if (self && !rule.SelfAllowed)
            {
                return "자신을 대상으로 할 수 없는 능력입니다.";
            }
            if (self && me.ActionCode == ActionCodes.Protect && me.LastSelfProtectDay == day - 1)
            {
                return "이틀 연속으로 자신을 보호할 수 없습니다.";
            }
            return null;
        }

        private string CheckActor(FakePlayer actor)
        {
            if (phase != GamePhases.Night)
            {
                return PhaseError(GamePhases.Night);
            }
            if (!actor.Alive)
            {
                return "행동하는 플레이어가 이미 사망했습니다: " + actor.Id;
            }
            if (lockedActors.Contains(actor.Id))
            {
                return "이번 밤 행동이 이미 확정되었습니다.";
            }
            return null;
        }

        private void Record(FakePlayer actor, FakePlayer target)
        {
            var action = new FakeAction(actor.Id, actor.ActionCode, target.Id);
            int index = nightActions.FindIndex(a => a.ActorId == actor.Id);
            if (index >= 0)
            {
                nightActions[index] = action;
            }
            else
            {
                nightActions.Add(action);
            }
            skippedActors.Remove(actor.Id);
        }

        /// <summary>앵무새가 살아 있는 해적을 지목하면 제출 즉시 접선하고, 그날 밤 행동을 고정한다.</summary>
        private bool TryContact(FakePlayer actor, FakePlayer target)
        {
            if (actor.ActionCode != ActionCodes.WatchAction || !actor.IsParrot || actor.Contacted || !target.IsRaider)
            {
                return false;
            }
            actor.Contacted = true;
            lockedActors.Add(actor.Id);
            return true;
        }

        private bool CanActTonight(FakePlayer p)
        {
            if (!p.Alive || p.ActionCode == null || p.UsesLeft == 0)
            {
                return false;
            }
            return AbilityRules.Find(p.ActionCode).LivingTarget || AnyDead();
        }

        private bool AllNightActionsSubmitted()
        {
            foreach (FakePlayer p in players)
            {
                if (CanActTonight(p) && !skippedActors.Contains(p.Id) && nightActions.FindIndex(a => a.ActorId == p.Id) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void BotsSubmitNightActions()
        {
            foreach (FakePlayer bot in players)
            {
                if (bot.Id == MyPlayerId || !CanActTonight(bot))
                {
                    continue;
                }
                List<FakePlayer> candidates = BotNightTargets(bot);
                if (candidates.Count == 0)
                {
                    skippedActors.Add(bot.Id);
                    continue;
                }
                FakePlayer target = candidates[random.Next(candidates.Count)];
                Record(bot, target);
                TryContact(bot, target);
            }
        }

        /// <summary>봇이 고를 대상. 해적 봇은 아는 동료를, 선의 봇은 이틀 연속 자기 보호를 피한다.</summary>
        private List<FakePlayer> BotNightTargets(FakePlayer bot)
        {
            ActionRule rule = AbilityRules.Find(bot.ActionCode);
            List<FakePlayer> allies = KnownAllies(bot);
            var result = new List<FakePlayer>();
            foreach (FakePlayer p in players)
            {
                if (p.Alive != rule.LivingTarget)
                {
                    continue;
                }
                bool self = p.Id == bot.Id;
                if (self && (!rule.SelfAllowed || (bot.ActionCode == ActionCodes.Protect && bot.LastSelfProtectDay == day - 1)))
                {
                    continue;
                }
                if (bot.IsRaider && allies.Contains(p))
                {
                    continue;
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>서버 NightActionResolver.resolve와 같은 순서: 차단 → 방문 → 효과 → 보호·공격 → 개인 결과 → 사용 기록 → 승리 검사.</summary>
        private void ResolveNight(double at)
        {
            var submitted = new List<FakeAction>();
            foreach (FakeAction a in nightActions)
            {
                if (Find(a.ActorId).Alive)
                {
                    submitted.Add(a);
                }
            }
            var aliveAtNightStart = new List<FakePlayer>();
            foreach (FakePlayer p in players)
            {
                if (p.Alive)
                {
                    aliveAtNightStart.Add(p);
                }
            }

            // 1~2. 차단. 원숭이의 위장 차단은 효과가 없고, 차단 행동 자체는 막히지 않는다.
            var blocked = new HashSet<long>();
            foreach (FakeAction a in submitted)
            {
                if (a.Code == ActionCodes.Block && !Find(a.ActorId).IsMonkey)
                {
                    blocked.Add(a.TargetId);
                }
            }
            var unblocked = new List<FakeAction>();
            var visits = new List<FakeAction>();   // 3. 방문 기록. 공격 선택은 빼고 실행자 1명만 아래에서 넣는다
            var effective = new List<FakeAction>(); // 4. 효과가 있는 행동 (원숭이 제외)
            foreach (FakeAction a in submitted)
            {
                if (a.Code != ActionCodes.Block && blocked.Contains(a.ActorId))
                {
                    continue;
                }
                unblocked.Add(a);
                if (a.Code != ActionCodes.SelectAttackTarget)
                {
                    visits.Add(a);
                }
                if (!Find(a.ActorId).IsMonkey)
                {
                    effective.Add(a);
                }
            }

            // 5~6. 보호와 공격
            var protectedIds = new HashSet<long>();
            foreach (FakeAction a in effective)
            {
                if (a.Code == ActionCodes.Protect)
                {
                    protectedIds.Add(a.TargetId);
                }
            }
            long killedId = 0;
            bool saved = false;
            long attackTargetId = PickMostVoted(effective);
            if (attackTargetId != 0)
            {
                visits.Add(new FakeAction(PickExecutor(effective, attackTargetId), ActionCodes.SelectAttackTarget, attackTargetId));
                if (protectedIds.Contains(attackTargetId))
                {
                    saved = true;
                }
                else
                {
                    Find(attackTargetId).Alive = false;
                    killedId = attackTargetId;
                }
            }

            // 7~8. 내 개인 결과 (이번 밤에 죽었어도 받는다). 원숭이는 같은 형식의 가짜 결과.
            var result = new NightResultDto
            {
                day = day,
                killedPlayerId = killedId,
                killedNickname = killedId != 0 ? Find(killedId).Nickname : null,
                protectedByDoctor = saved
            };
            FakeAction mine = unblocked.Find(a => a.ActorId == MyPlayerId);
            if (mine != null)
            {
                ReportDto report = Me.IsMonkey ? FakeReport(mine, aliveAtNightStart) : RealReport(mine, visits);
                if (report != null)
                {
                    result.reports.Add(report);
                }
            }

            // 9. 사용 기록. 차단당한 행동은 횟수를 쓰지 않는다. 원숭이도 위장 능력의 횟수를 똑같이 쓴다.
            foreach (FakeAction a in unblocked)
            {
                FakePlayer actor = Find(a.ActorId);
                if (actor.UsesLeft > 0)
                {
                    actor.UsesLeft--;
                }
                if (a.Code == ActionCodes.Protect && a.ActorId == a.TargetId)
                {
                    actor.LastSelfProtectDay = day;
                }
            }

            lastNightResult = result;
            if (!FinishIfWinnerDecided())
            {
                MoveTo(GamePhases.NightResult, at, options.NightResultSeconds);
            }
        }

        private long PickMostVoted(List<FakeAction> actions)
        {
            var targets = new List<long>();
            var counts = new List<int>();
            foreach (FakeAction a in actions)
            {
                if (a.Code != ActionCodes.SelectAttackTarget)
                {
                    continue;
                }
                int index = targets.IndexOf(a.TargetId);
                if (index < 0)
                {
                    targets.Add(a.TargetId);
                    counts.Add(1);
                }
                else
                {
                    counts[index]++;
                }
            }
            if (targets.Count == 0)
            {
                return 0;
            }
            int max = 0;
            foreach (int c in counts)
            {
                max = Math.Max(max, c);
            }
            var top = new List<long>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (counts[i] == max)
                {
                    top.Add(targets[i]);
                }
            }
            return top[random.Next(top.Count)];
        }

        /// <summary>공격 대상에게 표를 준 해적 중 무작위 1명. 망루지기·앵무새에게는 이 해적의 방문만 보인다.</summary>
        private long PickExecutor(List<FakeAction> actions, long attackTargetId)
        {
            var voters = new List<long>();
            foreach (FakeAction a in actions)
            {
                if (a.Code == ActionCodes.SelectAttackTarget && a.TargetId == attackTargetId)
                {
                    voters.Add(a.ActorId);
                }
            }
            return voters[random.Next(voters.Count)];
        }

        private ReportDto RealReport(FakeAction action, List<FakeAction> visits)
        {
            FakePlayer target = Find(action.TargetId);
            var report = new ReportDto { targetId = target.Id, targetNickname = target.Nickname };
            switch (action.Code)
            {
                case ActionCodes.InvestigateFaction:
                    report.type = ReportTypes.Faction;
                    report.faction = Roles[target.Role].Faction;
                    return report;

                case ActionCodes.ReadCorpseRole:
                    report.type = ReportTypes.CorpseRole;
                    report.roleCode = target.Role;
                    report.roleName = Roles[target.Role].Name;
                    return report;

                case ActionCodes.WatchVisitors:
                    report.type = ReportTypes.Visitors;
                    var visitorIds = new List<long>();
                    foreach (FakeAction v in visits)
                    {
                        if (v.TargetId == target.Id && v.ActorId != action.ActorId && v.ActorId != target.Id && !visitorIds.Contains(v.ActorId))
                        {
                            visitorIds.Add(v.ActorId);
                        }
                    }
                    visitorIds.Sort();
                    foreach (long id in visitorIds)
                    {
                        report.players.Add(new PlayerRefDto { playerId = id, nickname = Find(id).Nickname });
                    }
                    return report;

                case ActionCodes.WatchAction:
                    // 해적을 지목한 앵무새는 제출 시점의 접선으로 대신하고, 그 해적의 행동은 알려 주지 않는다.
                    if (target.IsRaider)
                    {
                        return null;
                    }
                    report.type = ReportTypes.Actions;
                    foreach (FakeAction v in visits)
                    {
                        if (v.ActorId == target.Id)
                        {
                            FakePlayer actedOn = Find(v.TargetId);
                            report.actions.Add(new ActionViewDto { actionCode = v.Code, targetId = actedOn.Id, targetNickname = actedOn.Nickname });
                        }
                    }
                    return report;

                default:
                    return null; // 보호·공격·차단은 개인 결과가 없다
            }
        }

        /// <summary>원숭이의 가짜 결과 (서버 fakeReportOf). 선의·갑판장 위장은 진짜도 결과가 없으므로 null.</summary>
        private ReportDto FakeReport(FakeAction action, List<FakePlayer> aliveAtNightStart)
        {
            FakePlayer target = Find(action.TargetId);
            var report = new ReportDto { targetId = target.Id, targetNickname = target.Nickname };
            switch (action.Code)
            {
                case ActionCodes.InvestigateFaction:
                    report.type = ReportTypes.Faction;
                    report.faction = random.Next(2) == 0 ? Factions.Crew : Factions.Pirate;
                    return report;

                case ActionCodes.WatchVisitors:
                    report.type = ReportTypes.Visitors;
                    var candidates = new List<FakePlayer>();
                    foreach (FakePlayer p in aliveAtNightStart)
                    {
                        if (p.Id != action.ActorId && p.Id != target.Id)
                        {
                            candidates.Add(p);
                        }
                    }
                    int count = random.Next(Math.Min(2, candidates.Count) + 1);
                    var picked = new List<FakePlayer>();
                    for (int i = 0; i < count; i++)
                    {
                        int index = random.Next(candidates.Count);
                        picked.Add(candidates[index]);
                        candidates.RemoveAt(index);
                    }
                    picked.Sort((a, b) => a.Id.CompareTo(b.Id));
                    foreach (FakePlayer p in picked)
                    {
                        report.players.Add(new PlayerRefDto { playerId = p.Id, nickname = p.Nickname });
                    }
                    return report;

                case ActionCodes.ReadCorpseRole:
                    report.type = ReportTypes.CorpseRole;
                    var assigned = new List<string>();
                    foreach (FakePlayer p in players)
                    {
                        if (!assigned.Contains(p.Role))
                        {
                            assigned.Add(p.Role);
                        }
                    }
                    string role = assigned[random.Next(assigned.Count)];
                    report.roleCode = role;
                    report.roleName = Roles[role].Name;
                    return report;

                default:
                    return null;
            }
        }

        // ================================================================ 투표

        private bool AllVotesSubmitted()
        {
            int alive = 0;
            int voted = 0;
            foreach (FakePlayer p in players)
            {
                if (p.Alive)
                {
                    alive++;
                    if (votes.ContainsKey(p.Id))
                    {
                        voted++;
                    }
                }
            }
            return voted >= alive;
        }

        /// <summary>봇은 자기 자신과 아는 해적 동료를 빼고 무작위로 투표한다.</summary>
        private void BotsVote()
        {
            foreach (FakePlayer bot in players)
            {
                if (bot.Id == MyPlayerId || !bot.Alive)
                {
                    continue;
                }
                List<FakePlayer> allies = KnownAllies(bot);
                var candidates = new List<FakePlayer>();
                foreach (FakePlayer p in players)
                {
                    if (p.Alive && p.Id != bot.Id && !allies.Contains(p))
                    {
                        candidates.Add(p);
                    }
                }
                if (candidates.Count > 0)
                {
                    votes[bot.Id] = candidates[random.Next(candidates.Count)].Id;
                }
            }
        }

        /// <summary>서버 VoteResolver: 최다 득표자 1명 처형. 동률이거나 투표가 없으면 처형하지 않는다.</summary>
        private void ResolveVote(double at)
        {
            var counts = new Dictionary<long, int>();
            foreach (KeyValuePair<long, long> v in votes)
            {
                if (!Find(v.Key).Alive)
                {
                    continue;
                }
                int count;
                counts.TryGetValue(v.Value, out count);
                counts[v.Value] = count + 1;
            }

            var result = new ExecutionResultDto { day = day };
            int max = 0;
            foreach (int c in counts.Values)
            {
                max = Math.Max(max, c);
            }
            var top = new List<long>();
            foreach (KeyValuePair<long, int> c in counts)
            {
                if (c.Value == max)
                {
                    top.Add(c.Key);
                }
            }
            if (top.Count == 1)
            {
                FakePlayer executed = Find(top[0]);
                executed.Alive = false;
                result.executedPlayerId = executed.Id;
                result.executedNickname = executed.Nickname;
            }
            else if (top.Count > 1)
            {
                result.tie = true;
            }

            foreach (KeyValuePair<long, int> c in counts)
            {
                result.votes.Add(new VoteCountDto { playerId = c.Key, nickname = Find(c.Key).Nickname, count = c.Value });
            }
            result.votes.Sort((a, b) => a.count != b.count ? b.count.CompareTo(a.count) : a.playerId.CompareTo(b.playerId));

            lastExecutionResult = result;
            if (!FinishIfWinnerDecided())
            {
                MoveTo(GamePhases.Execution, at, options.ExecutionSeconds);
            }
        }

        // ================================================================ 도우미

        private FakePlayer Me
        {
            get { return players[0]; }
        }

        private FakePlayer Find(long playerId)
        {
            foreach (FakePlayer p in players)
            {
                if (p.Id == playerId)
                {
                    return p;
                }
            }
            return null;
        }

        private bool AnyDead()
        {
            foreach (FakePlayer p in players)
            {
                if (!p.Alive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>서버 Game.knownPirateAllies: 해적끼리는 처음부터, 앵무새와 해적은 접선한 뒤에만 서로 안다.</summary>
        private List<FakePlayer> KnownAllies(FakePlayer me)
        {
            var allies = new List<FakePlayer>();
            if (!me.IsRaider && !(me.IsParrot && me.Contacted))
            {
                return allies;
            }
            foreach (FakePlayer p in players)
            {
                if (p.Id != me.Id && (p.IsRaider || (p.IsParrot && p.Contacted)))
                {
                    allies.Add(p);
                }
            }
            return allies;
        }

        private string PhaseError(string expected)
        {
            return "현재 페이즈(" + phase + ")에서는 할 수 없는 요청입니다. 필요 페이즈: " + expected;
        }

        private bool Begin<T>(string gameId, Action<GameApiResult<T>> onDone) where T : class
        {
            if (gameId != FakeGameId)
            {
                Reply(onDone, GameApiResult<T>.Fail(404, GameErrorCodes.GameNotFound, "게임을 찾을 수 없습니다: " + gameId));
                return false;
            }
            Advance();
            return true;
        }

        private static GameApiResult<T> RuleViolation<T>(string message) where T : class
        {
            return GameApiResult<T>.Fail(409, GameErrorCodes.GameRuleViolation, message);
        }

        private static void Reply<T>(Action<GameApiResult<T>> onDone, GameApiResult<T> result) where T : class
        {
            if (onDone != null)
            {
                onDone(result);
            }
        }

        private static string Iso(double seconds)
        {
            return Epoch.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static void ValidateOptions(FakeGameOptions o)
        {
            if (o.MyRole == null || !Roles.ContainsKey(o.MyRole))
            {
                throw new ArgumentException("모르는 직업입니다: " + o.MyRole);
            }
            if (o.MyRole == RoleCodes.CrewMonkey && Array.IndexOf(MonkeyDisguises, o.MonkeyDisguise) < 0)
            {
                throw new ArgumentException("원숭이 위장 직업이 될 수 없습니다: " + o.MonkeyDisguise);
            }
            if (o.NightSeconds <= 0 || o.NightResultSeconds <= 0 || o.DaySeconds <= 0 || o.VoteSeconds <= 0 || o.ExecutionSeconds <= 0)
            {
                throw new ArgumentException("페이즈 시간은 0보다 커야 합니다.");
            }
        }

        private static Dictionary<string, RoleInfo> CreateRoles()
        {
            var roles = new Dictionary<string, RoleInfo>();
            AddRole(roles, RoleCodes.PirateRaider, "해적", Factions.Pirate, ActionCodes.SelectAttackTarget);
            AddRole(roles, RoleCodes.PirateParrot, "앵무새", Factions.Pirate, ActionCodes.WatchAction);
            AddRole(roles, RoleCodes.CrewCaptain, "선장", Factions.Crew, ActionCodes.InvestigateFaction);
            AddRole(roles, RoleCodes.CrewDoctor, "선의", Factions.Crew, ActionCodes.Protect);
            AddRole(roles, RoleCodes.CrewLookout, "망루지기", Factions.Crew, ActionCodes.WatchVisitors);
            AddRole(roles, RoleCodes.CrewBoatswain, "갑판장", Factions.Crew, ActionCodes.Block);
            AddRole(roles, RoleCodes.CrewDrunk, "주정뱅이", Factions.Crew, ActionCodes.ReadCorpseRole);
            AddRole(roles, RoleCodes.CrewMonkey, "원숭이", Factions.Crew, null);
            AddRole(roles, RoleCodes.CrewSailor, "선원", Factions.Crew, null);
            return roles;
        }

        private static void AddRole(Dictionary<string, RoleInfo> roles, string code, string name, string faction, string actionCode)
        {
            roles.Add(code, new RoleInfo(code, name, faction, actionCode));
        }

        // ================================================================ 내부 상태

        private sealed class RoleInfo
        {
            public readonly string Code;
            public readonly string Name;
            public readonly string Faction;
            public readonly string ActionCode;

            public RoleInfo(string code, string name, string faction, string actionCode)
            {
                Code = code;
                Name = name;
                Faction = faction;
                ActionCode = actionCode;
            }
        }

        private sealed class FakePlayer
        {
            public readonly long Id;
            public readonly string Nickname;
            public readonly string Role;   // 실제 직업
            public readonly string Shown;  // 본인에게 보이는 직업 (원숭이만 다름)
            public bool Alive = true;
            public int UsesLeft;           // -1 = 제한 없음
            public int LastSelfProtectDay = -1;
            public bool Contacted;

            public FakePlayer(long id, string nickname, string role, string shown)
            {
                Id = id;
                Nickname = nickname;
                Role = role;
                Shown = shown;
                ActionRule rule = AbilityRules.Find(ActionCode);
                UsesLeft = rule != null ? rule.MaxUses : -1;
            }

            public string ActionCode
            {
                get { return Roles[Shown].ActionCode; }
            }

            public bool IsRaider
            {
                get { return Role == RoleCodes.PirateRaider; }
            }

            public bool IsParrot
            {
                get { return Role == RoleCodes.PirateParrot; }
            }

            public bool IsMonkey
            {
                get { return Role == RoleCodes.CrewMonkey; }
            }

            public bool IsPirate
            {
                get { return Roles[Role].Faction == Factions.Pirate; }
            }
        }

        private sealed class FakeAction
        {
            public readonly long ActorId;
            public readonly string Code;
            public readonly long TargetId;

            public FakeAction(long actorId, string code, long targetId)
            {
                ActorId = actorId;
                Code = code;
                TargetId = targetId;
            }
        }
    }
}
