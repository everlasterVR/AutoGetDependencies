using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace everlaster
{
    sealed class UnityEventsListener : MonoBehaviour, IPointerClickHandler
    {
        public Action enabledHandlers;
        public Action disabledHandlers;
        public Action<PointerEventData> clickHandlers;

        void OnEnable()
        {
            enabledHandlers?.Invoke();
        }

        void OnDisable()
        {
            disabledHandlers?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            clickHandlers?.Invoke(eventData);
        }
    }
}
