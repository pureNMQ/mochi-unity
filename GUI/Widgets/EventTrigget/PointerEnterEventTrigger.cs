using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerEnterEventTrigger : MonoBehaviour, IPointerEnterHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerEnter(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}