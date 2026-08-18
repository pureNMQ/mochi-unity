using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class EndDragEventTrigger : MonoBehaviour, IEndDragHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnEndDrag(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}