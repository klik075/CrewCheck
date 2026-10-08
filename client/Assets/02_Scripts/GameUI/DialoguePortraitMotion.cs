using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// Add to a portrait's visual object. The parent can still control layout and position.
    /// Call SetSpeaking from dialogue code when this character starts or stops talking.
    /// </summary>
    public sealed class DialoguePortraitMotion : MonoBehaviour
    {
        [SerializeField] private float breathCycleSeconds = 3.2f;
        [SerializeField] private float breathScale = 0.008f;
        [SerializeField] private float breathLift = 1.5f;
        [SerializeField] private float speakingLift = 3f;
        [SerializeField] private float speakingTiltDegrees = 0.65f;
        [SerializeField] private float speakingSpeed = 8f;

        private Vector3 restPosition;
        private Vector3 restScale;
        private Quaternion restRotation;
        private float speakingWeight;
        private bool speaking;

        public bool IsSpeaking => speaking;

        public void SetSpeaking(bool value)
        {
            speaking = value;
        }

        private void OnEnable()
        {
            restPosition = transform.localPosition;
            restScale = transform.localScale;
            restRotation = transform.localRotation;
            speakingWeight = 0f;
        }

        private void Update()
        {
            float time = Time.unscaledTime;
            float breath = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.1f, breathCycleSeconds));
            speakingWeight = Mathf.MoveTowards(speakingWeight, speaking ? 1f : 0f,
                Time.unscaledDeltaTime * 5f);

            // Two frequencies keep the dialogue motion from looking like a uniform bounce.
            float talk = Mathf.Sin(time * speakingSpeed * Mathf.PI * 2f);
            float talkDetail = Mathf.Sin(time * speakingSpeed * 1.37f * Mathf.PI * 2f);
            float lift = breath * breathLift + speakingWeight *
                (talk * speakingLift + talkDetail * speakingLift * 0.25f);

            transform.localPosition = restPosition + Vector3.up * lift;
            transform.localScale = Vector3.Scale(restScale,
                new Vector3(1f - breath * breathScale * 0.5f,
                    1f + breath * breathScale + speakingWeight * talk * 0.003f, 1f));
            transform.localRotation = restRotation * Quaternion.Euler(0f, 0f,
                speakingWeight * talkDetail * speakingTiltDegrees);
        }

        private void OnDisable()
        {
            transform.localPosition = restPosition;
            transform.localScale = restScale;
            transform.localRotation = restRotation;
        }
    }
}
