using System;
using UnityEngine;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>Result accents follow the faction colors used by the role information UI.</summary>
    internal static class GameResultPalette
    {
        internal static readonly Color Muted = new Color(.58f, .68f, .79f);

        internal static Color ForFaction(string faction)
        {
            switch (faction)
            {
                case Factions.Crew: return new Color(.5f, .78f, .98f);
                case Factions.Pirate: return new Color(.95f, .42f, .36f);
                case Factions.Neutral: return new Color(.78f, .64f, .98f);
                default: return Muted;
            }
        }

        internal static string FactionForRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return null;
            // Results reveal actual roles; cosmetic design IDs do not determine faction.
            if (role.StartsWith("CREW_", StringComparison.Ordinal)) return Factions.Crew;
            if (role.StartsWith("PIRATE_", StringComparison.Ordinal)) return Factions.Pirate;
            if (role.StartsWith("NEUTRAL_", StringComparison.Ordinal) || role.StartsWith("THIRD_", StringComparison.Ordinal))
                return Factions.Neutral;
            return null;
        }
    }
}
