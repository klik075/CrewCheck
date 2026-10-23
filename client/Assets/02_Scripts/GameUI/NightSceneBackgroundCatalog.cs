using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    [CreateAssetMenu(fileName = "NightSceneBackgrounds", menuName = "Game UI/Night Scene Backgrounds")]
    public sealed class NightSceneBackgroundCatalog : ScriptableObject
    {
        [Serializable]
        private struct BackgroundEntry
        {
            public string designId;
            public Sprite sprite;

            public string DesignId => designId;
            public Sprite Sprite => sprite;
        }

        [SerializeField] private Sprite pirateShared;
        [SerializeField] private BackgroundEntry[] backgrounds;

        public Sprite PirateShared => pirateShared;

        public Sprite Find(string designId)
        {
            if (string.IsNullOrEmpty(designId) || backgrounds == null)
                return null;

            foreach (var entry in backgrounds)
            {
                if (string.Equals(entry.DesignId, designId, StringComparison.OrdinalIgnoreCase))
                    return entry.Sprite;
            }

            return null;
        }
    }
}
