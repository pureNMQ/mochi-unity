using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class Panel : MonoBehaviour, IPanel
    {
        [SerializeField] private GameObject defaultFocusTarget;

        public bool IsOpen { get; private set; }
        public bool IsVisible { get; private set; }
        public GameObject DefaultFocusTarget => defaultFocusTarget;

        internal UniTask PrepareInternalAsync(CancellationToken cancellationToken)
        {
            return OnPrepareAsync(cancellationToken);
        }

        internal void OpenInternal()
        {
            OnOpen();
            IsOpen = true;
        }

        internal void ShowInternal()
        {
            gameObject.SetActive(true);
            OnShow();
            IsVisible = true;
        }

        internal void HideInternal()
        {
            OnHide();
            gameObject.SetActive(false);
            IsVisible = false;
        }

        internal void RestoreHiddenInternal()
        {
            gameObject.SetActive(false);
            IsVisible = false;
        }

        internal void CloseInternal()
        {
            OnClose();
            IsOpen = false;
        }

        internal UniTask EnterInternalAsync(CancellationToken cancellationToken)
        {
            IPanelTransition transition = this as IPanelTransition ?? GetComponent<IPanelTransition>();
            if (transition != null)
            {
                return transition.EnterAsync(cancellationToken);
            }

            return UniTask.CompletedTask;
        }

        internal UniTask ExitInternalAsync(CancellationToken cancellationToken)
        {
            IPanelTransition transition = this as IPanelTransition ?? GetComponent<IPanelTransition>();
            if (transition != null)
            {
                return transition.ExitAsync(cancellationToken);
            }

            return UniTask.CompletedTask;
        }

        internal void SetInteractionEnabledInternal(bool enabled)
        {
            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        internal void FocusDefaultInternal()
        {
            FocusInternal(defaultFocusTarget);
        }

        internal void FocusInternal(GameObject focusTarget)
        {
            if (focusTarget != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(focusTarget);
            }
        }

        protected virtual UniTask OnPrepareAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnShow()
        {
        }

        protected virtual void OnHide()
        {
        }

        protected virtual void OnClose()
        {
        }
    }
}
