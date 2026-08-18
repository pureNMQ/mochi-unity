using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class BeginDragEventTrigger : MonoBehaviour, IBeginDragHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnBeginDrag(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}