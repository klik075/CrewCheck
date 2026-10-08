using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    [CreateAssetMenu(fileName = "AbilityCutIns", menuName = "Game UI/Ability Cut Ins")]
    public sealed class AbilityCutInCatalog : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public string roleCode;
            public Sprite sprite;
            public bool hasGameplayAbility;

            public string RoleCode => roleCode;
            public Sprite Sprite => sprite;
            public bool HasGameplayAbility => hasGameplayAbility;
        }

        [SerializeField] private Entry[] entries;

        public bool TryFind(string roleCode, out Sprite sprite, out bool hasGameplayAbility)
        {
            if (!string.IsNullOrEmpty(roleCode) && entries != null)
            {
                foreach (var entry in entries)
                {
                    if (string.Equals(entry.RoleCode, roleCode, StringComparison.OrdinalIgnoreCase))
                    {
                        sprite = entry.Sprite;
                        hasGameplayAbility = entry.HasGameplayAbility;
                        return sprite != null;
                    }
                }
            }

            sprite = null;
            hasGameplayAbility = false;
            return false;
        }
    }
}
