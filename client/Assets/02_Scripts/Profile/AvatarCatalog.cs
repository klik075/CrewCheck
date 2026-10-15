using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 아바타 프리셋 목록. 서버에는 번호(profileImageId)만 저장하고, 번호 → 스프라이트 변환은 여기서 한다.
    ///
    /// 아이콘 등록: Assets/Resources/Avatars/ 폴더에 스프라이트를 넣는다. 이름순으로 0, 1, 2... 번호가 된다.
    /// 폴더가 비어 있으면 번호별 색깔 동그라미(임시 아이콘)로 대신 보여 준다.
    /// </summary>
    public static class AvatarCatalog
    {
        /// <summary>선택 가능한 아바타 수. 서버 UserDto.PROFILE_IMAGE_COUNT 와 같아야 한다.</summary>
        public const int Count = 8;

        private const string ResourcePath = "Avatars";

        private static Sprite[] sprites;
        private static Sprite placeholder;

        /// <summary>번호에 맞는 스프라이트. 범위를 벗어나면 0번. 등록된 아이콘이 없으면 임시 아이콘.</summary>
        public static Sprite Get(int id)
        {
            if (sprites == null)
                sprites = Resources.LoadAll<Sprite>(ResourcePath).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();

            if (id < 0 || id >= Count) id = 0;
            if (id < sprites.Length) return sprites[id];

            if (placeholder == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                placeholder = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            }
            return placeholder;
        }

        /// <summary>Image에 아바타를 표시한다. (등록된 아이콘이 없으면 번호별 색으로 구분)</summary>
        public static void Apply(Image image, int id)
        {
            if (image == null) return;
            Sprite sprite = Get(id);
            image.sprite = sprite;
            image.color = sprite == placeholder ? Color.HSVToRGB(Mathf.Repeat(id, Count) / Count, 0.5f, 0.9f) : Color.white;
            image.enabled = true;
        }
    }
}
