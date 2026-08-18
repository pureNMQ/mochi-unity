using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class DeselectEventTrigger : MonoBehaviour, IDeselectHandler
    {
        public event Action<BaseEventData> Callback;

        public void OnDeselect(BaseEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}