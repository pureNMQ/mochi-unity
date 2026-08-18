using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class DropEventTrigger : MonoBehaviour, IDropHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnDrop(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}