using System;
using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// White procedural sprites for the night effects, tinted by the Image colour, so the motions need no extra art.
    /// Built once on first use.
    /// </summary>
    internal static class NightFxSprites
    {
        private static Sprite dot, ring, streak, cone, fade, circle;

        /// <summary>Soft round glow, brightest in the middle.</summary>
        public static Sprite Dot => dot != null ? dot : (dot = Make("FxDot", 64, 64, new Vector2(0.5f, 0.5f), (u, v) =>
        {
            float d = Distance(u, v);
            return (1f - d) * (1f - d);
        }));

        /// <summary>Thin soft ring touching the edges.</summary>
        public static Sprite Ring => ring != null ? ring : (ring = Make("FxRing", 128, 128, new Vector2(0.5f, 0.5f), (u, v) =>
        {
            float d = (Distance(u, v) - 0.86f) / 0.08f;
            return Mathf.Exp(-d * d);
        }));

        /// <summary>Blade-like streak along x: thick in the middle, pointed at both ends.</summary>
        public static Sprite Streak => streak != null ? streak : (streak = Make("FxStreak", 128, 16, new Vector2(0.5f, 0.5f), (u, v) =>
        {
            float width = Mathf.Sin(u * Mathf.PI);
            float across = Mathf.Abs(v - 0.5f) * 2f / Mathf.Max(0.05f, width);
            return Mathf.Clamp01(1f - across * across) * width;
        }));

        /// <summary>Light cone with its apex at the left middle (pivot), widening and fading to the right.</summary>
        public static Sprite Cone => cone != null ? cone : (cone = Make("FxCone", 128, 96, new Vector2(0f, 0.5f), (u, v) =>
        {
            float across = Mathf.Abs(v - 0.5f) * 2f / Mathf.Max(0.02f, u);
            return across >= 1f ? 0f : (1f - across * across) * Mathf.Pow(1f - u, 0.7f) * Mathf.Clamp01(u * 8f);
        }));

        /// <summary>
        /// Solid disc with a one-pixel soft edge (round masks, e.g. the role panel portrait). An asset (Resources/GameHud/
        /// HudCircle, Tools/GameHudArt/MakeHudShapes.ps1) so a layout baked into a scene keeps it; made here if it is missing.
        /// </summary>
        public static Sprite Circle => circle != null ? circle : (circle = LoadCircle());

        private static Sprite LoadCircle()
        {
            Sprite art = Resources.Load<Sprite>("GameHud/HudCircle");
            return art != null ? art : Make("FxCircle", 128, 128, new Vector2(0.5f, 0.5f), (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                return (1f - Mathf.Sqrt(x * x + y * y)) * 64f;
            });
        }

        /// <summary>Vertical fade: clear at the top, solid at the bottom.</summary>
        public static Sprite Fade => fade != null ? fade : (fade = Make("FxFade", 4, 64, new Vector2(0.5f, 0.5f), (u, v) => 1f - v));

        private static Sprite tentacle;

        /// <summary>
        /// A tentacle curling into a spiral, thick at its root (bottom left) and thin at its tip: the kraken's mark on the
        /// roster and the kraken's crest.
        /// </summary>
        public static Sprite Tentacle => tentacle != null ? tentacle : (tentacle = MakeTentacle());

        private static Sprite MakeTentacle()
        {
            // The curve as points with a half-thickness each: from the root outside the spiral, two and a half turns in.
            const int Samples = 140;
            var points = new Vector3[Samples]; // x, y, half-thickness (all in 0..1 of the sprite)
            var centre = new Vector2(0.56f, 0.56f);
            for (int i = 0; i < Samples; i++)
            {
                float s = i / (Samples - 1f);
                float angle = (215f - s * 470f) * Mathf.Deg2Rad;
                float radius = 0.42f * Mathf.Pow(1f - s, 0.85f) + 0.025f;
                points[i] = new Vector3(centre.x + Mathf.Cos(angle) * radius, centre.y + Mathf.Sin(angle) * radius,
                    Mathf.Lerp(0.105f, 0.022f, s));
            }
            const int Size = 96;
            return Make("FxTentacle", Size, Size, new Vector2(0.5f, 0.5f), (u, v) =>
            {
                float inside = float.MaxValue;
                foreach (Vector3 p in points)
                {
                    float dx = u - p.x, dy = v - p.y;
                    inside = Mathf.Min(inside, Mathf.Sqrt(dx * dx + dy * dy) - p.z);
                }
                return -inside * Size; // one soft pixel at the edge
            });
        }

        private static float Distance(float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            return Mathf.Clamp01(Mathf.Sqrt(x * x + y * y));
        }

        private static Sprite Make(string name, int width, int height, Vector2 pivot, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
