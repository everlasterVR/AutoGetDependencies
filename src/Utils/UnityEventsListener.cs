using System;
using UnityEngine;

// Licensed under Creative Commons Attribution 4.0 International https://creativecommons.org/licenses/by/4.0/
// (c) 2024 everlaster
namespace everlaster
{
    sealed class UnityEventsListener : MonoBehaviour
    {
        public Action enabledHandlers;

        void OnEnable()
        {
            enabledHandlers?.Invoke();
        }
    }
}
