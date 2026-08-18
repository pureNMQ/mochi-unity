using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerDownEventTrigger : MonoBehaviour, IPointerDownHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerDown(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}