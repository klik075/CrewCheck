using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// The height of a chat row in a vertical layout group: tall enough for its message wrapped to the row's width (the
    /// message sits between fixed left and right columns), never shorter than the name tag.
    /// </summary>
    public sealed class ChatRowLayout : MonoBehaviour, ILayoutElement
    {
        private TextMeshProUGUI body;
        private float left, right, top, bottom, least;
        private float height;

        public void Setup(TextMeshProUGUI message, float leftColumn, float rightColumn, float padTop, float padBottom, float minHeight)
        {
            body = message;
            left = leftColumn;
            right = rightColumn;
            top = padTop;
            bottom = padBottom;
            least = height = minHeight;
        }

        public void CalculateLayoutInputHorizontal() { }

        public void CalculateLayoutInputVertical()
        {
            float width = ((RectTransform)transform).rect.width - left - right;
            height = least;
            if (body != null && width > 1f) height = Mathf.Max(least, body.GetPreferredValues(body.text, width, 0f).y + top + bottom);
        }

        public float minWidth => -1f;
        public float preferredWidth => -1f;
        public float flexibleWidth => -1f;
        public float minHeight => height;
        public float preferredHeight => height;
        public float flexibleHeight => -1f;
        public int layoutPriority => 1;
    }
}
