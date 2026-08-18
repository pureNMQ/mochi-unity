using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerUpEventTrigger : MonoBehaviour, IPointerUpHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerUp(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}