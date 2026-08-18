using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class SubmitEventTrigger : MonoBehaviour, ISubmitHandler
    {
        public event Action<BaseEventData> Callback;

        public void OnSubmit(BaseEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}