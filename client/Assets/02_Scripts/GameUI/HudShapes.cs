using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// White rounded-rectangle sprites, 9-sliced and tinted by the Image colour, for the small HUD pieces the art sheet
    /// does not have (chat tabs, name tags, the input pill, the timer bar). A panel is a fill with an outline on top.
    /// Built once on first use.
    /// </summary>
    internal static class HudShapes
    {
        private const int Size = 64, Corner = 16, Border = 20; // corner radius and 9-slice border, in texture pixels
        private const float Line = 3f;                          // outline width, in texture pixels
        private static Sprite fill, outline;

        public static Sprite Fill => fill != null ? fill : (fill = Make("HudRoundedFill", false));
        public static Sprite Outline => outline != null ? outline : (outline = Make("HudRoundedOutline", true));

        /// <summary>A rounded panel on rect: fill colour, outline colour, corner radius in canvas units.</summary>
        public static Image Panel(RectTransform rect, Color fillColor, Color lineColor, float radius)
        {
            Image back = Add(rect.gameObject, Fill, fillColor, radius);
            if (lineColor.a > 0f)
            {
                var go = new GameObject("Outline", typeof(RectTransform));
                go.layer = rect.gameObject.layer;
                var line = (RectTransform)go.transform;
                line.SetParent(rect, false);
                line.anchorMin = Vector2.zero;
                line.anchorMax = Vector2.one;
                line.offsetMin = line.offsetMax = Vector2.zero;
                line.SetAsFirstSibling(); // under the panel's own content
                Add(go, Outline, lineColor, radius);
            }
            return back;
        }

        private static Image Add(GameObject go, Sprite sprite, Color color, float radius)
        {
            Image image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Corner / Mathf.Max(1f, radius);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite Make(string name, bool outlineOnly)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[Size * Size];
            float half = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Signed distance to the rounded rectangle's edge (negative inside).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - Corner);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - Corner);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - Corner;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (outlineOnly) a *= 1f - Mathf.Clamp01(0.5f - (d + Line));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
