using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
namespace Mochi.Unity.GUI
{
    public delegate void ModalResultCallback<TResult>(bool hasResult, TResult result);

    public abstract class ModalPanel : Panel
    {
        [SerializeField] private bool closeOnBackdropClick;

        private ModalContainer container;
        private PanelStackService service;
        public bool CloseOnBackdropClick => closeOnBackdropClick;

        internal PanelStackService Service => service;

        //容器绑定属旧容器体系，待游戏调用面迁移完成后随容器一同退役；服务绑定为最终架构路径
        internal ModalContainer Container => container;

        protected UniTask<bool> CancelAsync()
        {
            if (service != null)
            {
                return UniTask.FromResult(service.Cancel(this));
            }

            return container == null ? UniTask.FromResult(false) : container.CancelAsync(this);
        }
        internal void BindService(PanelStackService owner)
        {
            service = owner;
        }

        internal void UnbindService()
        {
            service = null;
        }

        //Bind 只负责绑定容器；结果回调由泛型 Push 重载经强类型 BindResultCallback 在工厂返回后绑定
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
