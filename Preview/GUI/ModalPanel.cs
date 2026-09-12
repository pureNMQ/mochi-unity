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

        //Bind 只负责绑定容器；结果回调由泛型 Push 重载经强类型 BindResultCallback 在 Prepare 前绑定
        internal void Bind(ModalContainer owner)
        {
            container = owner;
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
