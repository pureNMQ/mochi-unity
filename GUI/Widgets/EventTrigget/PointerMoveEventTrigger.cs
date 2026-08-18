using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    public class PointerMoveEventTrigger : MonoBehaviour, IPointerMoveHandler
    {
        public event Action<PointerEventData> Callback;

        public void OnPointerMove(PointerEventData eventData)
        {
            Callback?.Invoke(eventData);
        }
    }
}
