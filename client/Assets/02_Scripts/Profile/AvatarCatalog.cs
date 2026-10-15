using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 아바타 프리셋 목록. 서버에는 번호(profileImageId)만 저장하고, 번호 → 스프라이트 변환은 여기서 한다.
    ///
    /// 이미지는 직업 초상화(Assets/09_Image/Portrait)이고, 게임 화면이 쓰는 Resources/GameHud/Portraits 카탈로그에서 가져온다.
    /// 번호는 아래 Ids 배열의 순서다. 저장된 번호의 뜻이 바뀌지 않도록 기존 항목의 순서는 바꾸지 말고, 새 초상화는 맨 뒤에 추가한다.
    /// (추가하면 Count가 늘어나므로 서버 UserDto.PROFILE_IMAGE_COUNT도 같이 올린다)
    /// </summary>
    public static class AvatarCatalog
    {
        private static readonly string[] Ids =
        {
            "CREW_CAPTAIN", "CREW_SAILOR", "CREW_DOCTOR", "CREW_GUNNER", "CREW_LOOKOUT", "CREW_BOATSWAIN",
            "CREW_DRUNK", "CREW_MONKEY", "CREW_MONKEY_ALT", "CREW_GHOST",
            "PIRATE_COOK", "PIRATE_COOK_FEMALE", "PIRATE_RAIDER", "PIRATE_SPY", "PIRATE_PARROT", "PIRATE_KRAKEN",
            "THIRD_MERMAID", "THIRD_MERMAID_MALE",
            "CONCEPT_GHOST_CAPTAIN", "CONCEPT_SIREN",
        };

        private const string CatalogPath = "GameHud/Portraits";

        /// <summary>선택 가능한 아바타 수. 서버 UserDto.PROFILE_IMAGE_COUNT 와 같아야 한다.</summary>
        public static int Count => Ids.Length;

        private static DesignSpriteCatalog catalog;
        private static Sprite placeholder;

        /// <summary>삭제된 20번 세이렌은 19번으로 표시한다. 나머지 잘못된 번호는 기본 아바타.</summary>
        public static int NormalizeId(int id)
        {
            if (id == 20) return 19;
            return id >= 0 && id < Ids.Length ? id : 0;
        }

        /// <summary>프로필 이미지와 게임 스킨이 공유하는 캐릭터 디자인 ID.</summary>
        public static string GetDesignId(int id) => Ids[NormalizeId(id)];

        /// <summary>정리한 번호에 맞는 스프라이트. 초상화를 찾지 못하면 임시 흰 이미지.</summary>
        public static Sprite Get(int id)
        {
            if (catalog == null) catalog = Resources.Load<DesignSpriteCatalog>(CatalogPath);
            Sprite sprite = catalog != null ? catalog.Find(GetDesignId(id)) : null;
            return sprite != null ? sprite : Placeholder();
        }

        /// <summary>Image에 아바타를 표시한다.</summary>
        public static void Apply(Image image, int id)
        {
            if (image == null) return;
            Sprite sprite = Get(id);
            image.sprite = sprite;
            image.color = sprite == placeholder ? Color.HSVToRGB(Mathf.Repeat(id, Ids.Length) / Ids.Length, 0.5f, 0.9f) : Color.white;
            image.enabled = true;
        }

        private static Sprite Placeholder()
        {
            if (placeholder == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                placeholder = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            }
            return placeholder;
        }
    }
}
