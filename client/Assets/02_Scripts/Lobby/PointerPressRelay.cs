using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 포인터로 이 오브젝트를 누를 때와 포인터가 벗어날 때 알린다. 직업 구성 창의 직업 설명에 쓴다.
    /// 드래그·스크롤 이벤트는 받지 않으므로 ScrollRect 안에 두어도 목록 스크롤을 막지 않는다.
    /// 터치는 손을 떼면 벗어난 것으로 오므로, 누르고 있는 동안만 보이게 된다.
    /// </summary>
    public sealed class PointerPressRelay : MonoBehaviour, IPointerDownHandler, IPointerExitHandler
    {
        public Action Pressed;
        public Action Exited;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
