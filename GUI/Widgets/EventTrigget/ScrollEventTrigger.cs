using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class ScrollEventTrigger : MonoBehaviour, IScrollHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnScroll(PointerEventData eventData)
        {
            eventData.scrollDelta = new Vector2(Input.mouseScrollDelta.x, Input.mouseScrollDelta.y); //只保留竖直滚�?
            Callback?.Invoke(eventData);
        }
    }
}