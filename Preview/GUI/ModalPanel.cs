using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public delegate void ModalResultCallback<TResult>(bool hasResult, TResult result);

    public abstract class ModalPanel : Panel
    {
        [SerializeField] private bool closeOnBackdropClick;

        private ModalContainer container;
        public bool CloseOnBackdropClick => closeOnBackdropClick;

        internal ModalContainer Container => container;

        protected UniTask<bool> CancelAsync()
        {
            return container == null ? UniTask.FromResult(false) : container.CancelAsync(this);
        }

        internal void Bind(ModalContainer owner)
        {
            container = owner;
            BindResultCallback(null);
        }

        internal virtual void BindResultCallback(Delegate callback)
        {
        }

        internal virtual Action CaptureResultNotification(bool hasResult, object result)
        {
            return null;
        }

        internal void Unbind()
        {
            container = null;
            ClearResultCallbacks();
        }

        internal virtual void ClearResultCallbacks()
        {
        }
    }
}
