using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class DragEventTrigger : MonoBehaviour, IDragHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnDrag(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}