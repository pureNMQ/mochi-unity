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
        private ModalHandle handle;

        public ModalHandle Handle => handle;
        public bool CloseOnBackdropClick => closeOnBackdropClick;

        protected UniTask<bool> CancelAsync()
        {
            return container == null ? UniTask.FromResult(false) : container.CancelAsync(this);
        }

        internal void Bind(ModalContainer owner, ModalHandle modalHandle)
        {
            container = owner;
            handle = modalHandle;
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
            handle = default;
            ClearResultCallbacks();
        }

        internal virtual void ClearResultCallbacks()
        {
        }
    }
}
