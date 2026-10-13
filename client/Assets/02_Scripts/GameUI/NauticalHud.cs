using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// GameScene3's nautical look (Docs/UIConcepts/chat-nautical.png, night-protect-nautical.png): the chat window and the
    /// action line with the player strip in gold frames with ropes and anchors, on a night-blue backdrop. The sprites are
    /// cut from the concepts by Tools/GameHudArt/CutNautical.ps1 into Resources/GameHud/Nautical. Their dark surroundings
    /// are alpha, so they look as on the concept on <see cref="Backdrop"/>.
    /// </summary>
    internal static class NauticalHud
    {
        /// <summary>The concepts' night blue, under every piece.</summary>
        public static readonly Color Backdrop = new Color32(0, 8, 14, 255);
        public static readonly Color Title = new Color(0.96f, 0.9f, 0.76f);
        public static readonly Color Sub = new Color(0.78f, 0.81f, 0.85f); // the concept's grey, as bright as a phone needs
        public static readonly Color Ink = new Color(0.25f, 0.15f, 0.05f);
        public static readonly Color Rule = new Color(0.78f, 0.58f, 0.27f, 0.55f);

        /// <summary>Concept pixels per canvas unit: the chat window and the night panel are drawn 1080 wide.</summary>
        public const float ChatScale = 1942f / 1080f, NightScale = 2172f / 1080f;

        /// <summary>PortraitRing: the ring's centre is (0.5, 0.5) of the sprite's width from its top-left (the anchor badge
        /// hangs below), its inner and outer radii these fractions of that width.</summary>
        public const float RingInner = 115f / 264f, RingOuter = 125.9f / 264f;

        public static Sprite Get(string name)
        {
            return Resources.Load<Sprite>("GameHud/Nautical/" + name);
        }

        /// <summary>The sprite 9-sliced at scale (its pixels per canvas unit).</summary>
        public static Image Sliced(Image image, string sprite, float scale)
        {
            image.sprite = Get(sprite);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = scale;
            image.color = Color.white;
            return image;
        }

        /// <summary>A plate (its ends kept, its middle stretched) drawn this high, so the ends keep their shape.</summary>
        public static Image Plate(Image image, string sprite, float height)
        {
            Sprite art = Get(sprite);
            return Sliced(image, sprite, art != null ? art.rect.height / Mathf.Max(1f, height) : 1f);
        }

        /// <summary>The sprite whole, at its own aspect.</summary>
        public static Image Whole(Image image, string sprite)
        {
            image.sprite = Get(sprite);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            return image;
        }
    }
}
