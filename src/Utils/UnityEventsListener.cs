using System;
using UnityEngine;
using UnityEngine.EventSystems;

/*
 * AutoGetDependencies v1.0
 * Licensed under CC BY https://creativecommons.org/licenses/by/4.0/
 * (c) 2024 everlaster
 * https://patreon.com/everlaster
 */
namespace everlaster
{
    sealed class UnityEventsListener : MonoBehaviour, IPointerClickHandler
    {
        public bool active => gameObject.activeInHierarchy;
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
