using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// One sprite per character design id (the same id as <see cref="ChibiCharacterSkin.DesignId"/>), e.g. the bust
    /// portraits in Resources/GameHud/Portraits that the role panel shows for the player's skin.
    /// </summary>
    [CreateAssetMenu(fileName = "DesignSprites", menuName = "Game UI/Design Sprites")]
    public sealed class DesignSpriteCatalog : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            public string designId = string.Empty;
            public Sprite sprite = null;
        }

        [SerializeField] private Entry[] entries = new Entry[0];

        /// <summary>The sprite of this design, or null.</summary>
        public Sprite Find(string designId)
        {
            if (string.IsNullOrEmpty(designId)) return null;
            foreach (Entry entry in entries)
            {
                if (entry != null && string.Equals(entry.designId, designId, StringComparison.OrdinalIgnoreCase)) return entry.sprite;
            }
            return null;
        }
    }
}
