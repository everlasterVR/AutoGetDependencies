using System;
using UnityEngine;

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
