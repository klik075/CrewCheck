using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    [RequireComponent(typeof(Image))]
    public sealed class NightSceneBackgroundView : MonoBehaviour
    {
        [SerializeField] private NightSceneBackgroundCatalog catalog;
        [SerializeField] private Image targetImage;

        private void Awake()
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        public bool ShowDesign(string designId)
        {
            return SetSprite(catalog != null ? catalog.Find(designId) : null);
        }

        public bool ShowPirateShared()
        {
            return SetSprite(catalog != null ? catalog.PirateShared : null);
        }

        private bool SetSprite(Sprite sprite)
        {
            if (sprite == null || targetImage == null)
                return false;

            targetImage.sprite = sprite;
            targetImage.color = Color.white;
            return true;
        }
    }
}
