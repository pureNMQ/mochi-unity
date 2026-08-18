using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class CancelEventTrigger : MonoBehaviour, ICancelHandler
    {
        public event Action<BaseEventData> Callback;

        public void OnCancel(BaseEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}