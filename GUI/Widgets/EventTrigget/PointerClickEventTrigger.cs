using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerClickEventTrigger : MonoBehaviour, IPointerClickHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerClick(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}