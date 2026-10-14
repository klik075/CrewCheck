using UnityEngine;
using UnityEngine.EventSystems;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// On the chat log: pressed and held still a moment, it opens; let go, it closes. A tap or a swipe to scroll does
    /// not open it, but scrolling once it is open keeps it open. <see cref="Open"/> eases 0 → 1 and back for the layout
    /// to grow the chat window by. The tabs and the filter are buttons, so pressing them does not reach this.
    /// </summary>
    public sealed class ChatPressHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const float HoldDelay = 0.25f, OpenTime = 0.18f, CloseTime = 0.14f;
        private const float SlopInches = 0.15f; // a finger held "still" may wander this far (the drag threshold is a few pixels)

        private int pressed;             // pointers down on the log
        // The first of them. The input module shares this one event between the pointer's buttons and leaves another
        // button's press state in it, so only its position (where the pointer is now) is read; where it went down is kept.
        private PointerEventData press;
        private Vector2 pressAt;
        private float heldFor, open;
        private bool scrolled;           // this press began as a swipe: it scrolls, it does not open

        /// <summary>How far open (0–1), eased.</summary>
        public float Open => open * open * (3f - 2f * open);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (pressed == 0)
            {
                press = eventData;
                pressAt = eventData.position;
                heldFor = 0f;
                scrolled = false;
            }
            pressed++;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || pressed == 0) return;
            pressed--;
            if (pressed == 0) press = null;
        }

        private void OnDisable()
        {
            pressed = 0;
            press = null;
            open = 0f;
        }

        private void Update()
        {
            if (pressed > 0)
            {
                heldFor += Time.unscaledDeltaTime;
                float slop = Screen.dpi > 0f ? Screen.dpi * SlopInches : 24f;
                if (open <= 0f && press != null && (press.position - pressAt).sqrMagnitude > slop * slop) scrolled = true;
            }
            bool want = pressed > 0 && !scrolled && heldFor >= HoldDelay;
            open = Mathf.MoveTowards(open, want ? 1f : 0f, Time.unscaledDeltaTime / (want ? OpenTime : CloseTime));
        }
    }
}
