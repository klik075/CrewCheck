using TMPro;
using UnityEngine;

namespace WhoisntCitizen.Game
{
    /// <summary>내 직업 카드. 직업 이름·진영·능력(남은 횟수)·해적 동료·사망 표시. 원숭이는 위장 직업이 그대로 보인다(서버가 그렇게 준다).</summary>
    public sealed class RoleCardView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI roleNameText;
        [SerializeField] private TextMeshProUGUI factionText;
        [SerializeField] private TextMeshProUGUI abilityText;
        [SerializeField] private TextMeshProUGUI teammatesText; // 해적 진영만. 동료가 없으면 비운다
        [SerializeField] private GameObject deadBadge;

        public void Show(MyRoleDto me, GameStateDto state)
        {
            if (me == null)
            {
                return;
            }
            bool alive = state != null && GameStateQueries.FindPlayer(state, me.playerId) != null
                ? GameStateQueries.IsAlive(state, me.playerId)
                : me.alive;

            SetText(roleNameText, me.roleName);
            SetText(factionText, ReportFormatter.FactionName(me.faction));
            SetText(abilityText, GameScreenText.AbilityLine(me));
            SetText(teammatesText, GameScreenText.NoRichText(ReportFormatter.TeammatesLine(state, me)));
            if (deadBadge != null)
            {
                deadBadge.SetActive(!alive);
            }
        }

        private static void SetText(TextMeshProUGUI text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
