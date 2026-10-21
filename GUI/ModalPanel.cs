using System;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.GUI
{
    public delegate void ModalResultCallback<TResult>(bool hasResult, TResult result);

    public abstract class ModalPanel : InteractivePanel
    {
        private PanelStackService service;

        internal PanelStackService Service => service;

        protected UniTask<bool> CancelAsync()
        {
            return service == null ? UniTask.FromResult(false) : UniTask.FromResult(service.Cancel(this));
        }

        internal void BindService(PanelStackService owner)
        {
            service = owner;
        }

        internal void UnbindService()
        {
            service = null;
        }

        internal virtual Action CaptureResultNotification(bool hasResult, object result)
        {
            return null;
        }

        internal void Unbind()
        {
            ClearResultCallbacks();
        }

        internal virtual void ClearResultCallbacks()
        {
        }
    }
}
