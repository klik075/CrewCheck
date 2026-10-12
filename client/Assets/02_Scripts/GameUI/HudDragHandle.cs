using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WhoisntCitizen.GameUI
{
    /// <summary>A grip that reports vertical drags (in canvas units, up is positive), their end, and taps.</summary>
    public sealed class HudDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public event Action<float> Dragged;
        public event Action DragEnded;
        public event Action Tapped;

        private Canvas rootCanvas;

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (rootCanvas == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                rootCanvas = canvas != null ? canvas.rootCanvas : null;
            }
            float scale = rootCanvas != null && rootCanvas.scaleFactor > 0f ? rootCanvas.scaleFactor : 1f;
            Dragged?.Invoke(eventData.delta.y / scale);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DragEnded?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!eventData.dragging) Tapped?.Invoke();
        }
    }
}
