using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerExitEventTrigger : MonoBehaviour, IPointerExitHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerExit(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}