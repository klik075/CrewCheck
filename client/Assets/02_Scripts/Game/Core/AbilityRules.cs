using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    /// <summary>능력 하나의 대상 규칙. 서버 ActionCode enum(maxUses, livingTarget, selfTargetAllowed)과 같아야 한다.</summary>
    public sealed class ActionRule
    {
        public readonly string ActionCode;
        public readonly bool LivingTarget;  // true: 살아 있는 사람만, false: 사망한 사람만
        public readonly bool SelfAllowed;   // 자기 자신을 대상으로 할 수 있는지
        public readonly int MaxUses;        // -1 = 제한 없음

        public ActionRule(string actionCode, bool livingTarget, bool selfAllowed, int maxUses)
        {
            ActionCode = actionCode;
            LivingTarget = livingTarget;
            SelfAllowed = selfAllowed;
            MaxUses = maxUses;
        }
    }

    /// <summary>밤 능력을 쓸 수 없는 이유. None이면 쓸 수 있다. 검사 순서는 서버 Game.recordNightAction과 같다.</summary>
    public enum AbilityBlock
    {
        None,
        NotNight,        // 밤이 아님
        Dead,            // 내가 사망함
        LockedByContact, // 앵무새가 이번 밤 해적과 접선해 행동이 확정됨
        NoAbility,       // 밤 능력이 없는 직업
        UnknownAction,   // 클라이언트가 모르는 능력 코드 (서버에 새 능력이 추가됨)
        NoUsesLeft,      // 남은 사용 횟수 0
        NoCorpse         // 시체 대상 능력인데 사망자가 없음
    }

    /// <summary>
    /// 밤 능력·투표 버튼을 켤지, 누구를 고를 수 있는지 정한다. 화면 안내용이고 최종 판정은 서버가 한다.
    /// 서버만 아는 규칙(선의의 이틀 연속 자기 보호 금지)은 여기서 막지 못하고 서버의 409 메시지로 안내한다.
    /// </summary>
    public static class AbilityRules
    {
        private static readonly Dictionary<string, ActionRule> Rules = CreateRules();

        private static Dictionary<string, ActionRule> CreateRules()
        {
            var rules = new Dictionary<string, ActionRule>();
            Add(rules, ActionCodes.InvestigateFaction, true, false, -1);
            Add(rules, ActionCodes.Protect, true, true, -1);
            Add(rules, ActionCodes.WatchVisitors, true, true, -1);
            Add(rules, ActionCodes.Block, true, false, -1);
            Add(rules, ActionCodes.ReadCorpseRole, false, false, 2);
            Add(rules, ActionCodes.SelectAttackTarget, true, false, -1);
            Add(rules, ActionCodes.WatchAction, true, false, -1);
            return rules;
        }

        private static void Add(Dictionary<string, ActionRule> rules, string code, bool livingTarget, bool selfAllowed, int maxUses)
        {
            rules.Add(code, new ActionRule(code, livingTarget, selfAllowed, maxUses));
        }

        /// <summary>능력 코드의 규칙. 모르는 코드나 null이면 null.</summary>
        public static ActionRule Find(string actionCode)
        {
            ActionRule rule;
            return actionCode != null && Rules.TryGetValue(actionCode, out rule) ? rule : null;
        }

        /// <summary>
        /// 지금 밤 능력을 쓸 수 있는지. 생존 여부는 /me보다 자주 갱신되는 state로 판단한다.
        /// lockedTonight: 이번 밤 내 제출 응답에 contactedPirateIds가 왔으면 true (다음 밤에 풀린다).
        /// /me의 contacted는 한 번 접선하면 계속 true라서 잠금 판단에 쓰지 않는다.
        /// </summary>
        public static AbilityBlock CheckNightAbility(GameStateDto state, MyRoleDto me, bool lockedTonight)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            if (me == null)
            {
                throw new ArgumentNullException("me");
            }
            if (state.phase != GamePhases.Night)
            {
                return AbilityBlock.NotNight;
            }
            if (!IsMeAlive(state, me))
            {
                return AbilityBlock.Dead;
            }
            if (lockedTonight)
            {
                return AbilityBlock.LockedByContact;
            }
            if (!me.HasAbility)
            {
                return AbilityBlock.NoAbility;
            }
            ActionRule rule = Find(me.actionCode);
            if (rule == null)
            {
                return AbilityBlock.UnknownAction;
            }
            if (!me.IsUnlimited && me.remainingUses <= 0)
            {
                return AbilityBlock.NoUsesLeft;
            }
            if (!rule.LivingTarget && !GameStateQueries.AnyDead(state))
            {
                return AbilityBlock.NoCorpse;
            }
            return AbilityBlock.None;
        }

        /// <summary>
        /// 능력 규칙상 고를 수 있는 대상 (state.players 순서). 버튼을 켤지는 CheckNightAbility로 따로 판단한다.
        /// 해적은 동료 해적·앵무새도 고를 수 있다(서버 규칙).
        /// </summary>
        public static List<PlayerViewDto> NightTargets(GameStateDto state, MyRoleDto me)
        {
            var targets = new List<PlayerViewDto>();
            ActionRule rule = me == null ? null : Find(me.actionCode);
            if (state == null || rule == null)
            {
                return targets;
            }
            foreach (PlayerViewDto p in state.players)
            {
                if (IsAllowedTarget(rule, me.playerId, p))
                {
                    targets.Add(p);
                }
            }
            return targets;
        }

        /// <summary>targetId가 능력 규칙상 고를 수 있는 대상인지.</summary>
        public static bool IsValidNightTarget(GameStateDto state, MyRoleDto me, long targetId)
        {
            ActionRule rule = me == null ? null : Find(me.actionCode);
            PlayerViewDto target = GameStateQueries.FindPlayer(state, targetId);
            return rule != null && target != null && IsAllowedTarget(rule, me.playerId, target);
        }

        /// <summary>지금 투표할 수 있는지 (VOTE 페이즈의 생존자).</summary>
        public static bool CanVote(GameStateDto state, long myPlayerId)
        {
            return state != null && state.phase == GamePhases.Vote && GameStateQueries.IsAlive(state, myPlayerId);
        }

        /// <summary>투표 대상. 서버는 살아 있는 사람이면 누구든(자기 자신 포함) 받는다.</summary>
        public static List<PlayerViewDto> VoteTargets(GameStateDto state)
        {
            var targets = new List<PlayerViewDto>();
            if (state == null)
            {
                return targets;
            }
            foreach (PlayerViewDto p in state.players)
            {
                if (p.alive)
                {
                    targets.Add(p);
                }
            }
            return targets;
        }

        private static bool IsAllowedTarget(ActionRule rule, long myPlayerId, PlayerViewDto target)
        {
            if (rule.LivingTarget != target.alive)
            {
                return false;
            }
            return rule.SelfAllowed || target.playerId != myPlayerId;
        }

        private static bool IsMeAlive(GameStateDto state, MyRoleDto me)
        {
            PlayerViewDto p = GameStateQueries.FindPlayer(state, me.playerId);
            return p != null ? p.alive : me.alive;
        }
    }
}
