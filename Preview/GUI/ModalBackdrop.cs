using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    internal sealed class ModalBackdrop : MonoBehaviour, IPointerClickHandler
    {
        private ModalContainer container;

        internal void Initialize(ModalContainer owner)
        {
            container = owner;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            container?.HandleBackdropClick();
        }
    }
}
