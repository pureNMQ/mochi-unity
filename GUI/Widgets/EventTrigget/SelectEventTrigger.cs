using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class SelectEventTrigger : MonoBehaviour, ISelectHandler
    {
        public event Action<BaseEventData> Callback;

        public void OnSelect(BaseEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}