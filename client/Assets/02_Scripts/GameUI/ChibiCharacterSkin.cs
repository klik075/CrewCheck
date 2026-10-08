using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    [CreateAssetMenu(fileName = "CharacterSkin", menuName = "Characters/Chibi Character Skin")]
    public sealed class ChibiCharacterSkin : ScriptableObject
    {
        [SerializeField] private string designId;
        [SerializeField] private Sprite standing;
        [SerializeField] private Sprite seatedFront;
        [SerializeField] private Sprite seatedFrontBlink;
        [SerializeField] private Sprite seatedFrontTalk;
        [SerializeField] private Sprite seatedSide;
        [SerializeField] private Sprite seatedBack;
        [SerializeField] private Sprite seated1OClock;
        [SerializeField] private Sprite seated5OClock;
        [SerializeField] private Sprite seated7OClock;
        [SerializeField] private Sprite seated11OClock;

        public string DesignId => designId;
        public Sprite Standing => standing;
        public Sprite SeatedFront => seatedFront;
        public Sprite SeatedFrontBlink => seatedFrontBlink;
        public Sprite SeatedFrontTalk => seatedFrontTalk;
        public Sprite SeatedSide => seatedSide;
        public Sprite SeatedBack => seatedBack;
        public Sprite Seated1OClock => seated1OClock;
        public Sprite Seated5OClock => seated5OClock;
        public Sprite Seated7OClock => seated7OClock;
        public Sprite Seated11OClock => seated11OClock;
    }
}
