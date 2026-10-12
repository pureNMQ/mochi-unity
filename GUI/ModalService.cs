using System;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.GUI
{
    /// <summary>将模态面板结果转换为业务异步结果。</summary>
    public sealed class ModalService
    {
        private readonly PanelStackService panelStack;

        /// <summary>绑定唯一的页面与模态栈服务。</summary>
        public ModalService(PanelStackService panelStack)
        {
            this.panelStack = panelStack ?? throw new ArgumentNullException(nameof(panelStack));
        }

        public async UniTask<bool> ConfirmAsync(ConfirmModalData data, string address = null)
        {
            ConfirmModal modal = await panelStack.PushModalAsync<ConfirmModal, ConfirmModalData, ConfirmModalResult>(
                data,
                address);
            if (modal == null)
            {
                return false;
            }

            ModalResult<ConfirmModalResult> result = await panelStack.GetModalResult<ConfirmModal, ConfirmModalResult>(modal);
            return result.HasResult && result.Result == ConfirmModalResult.Confirmed;
        }

        public async UniTask<bool> MessageAsync(MessageModalData data, string address = null)
        {
            MessageModal modal = await panelStack.PushModalAsync<MessageModal, MessageModalData, MessageModalResult>(
                data,
                address);
            if (modal == null)
            {
                return false;
            }

            ModalResult<MessageModalResult> result = await panelStack.GetModalResult<MessageModal, MessageModalResult>(modal);
            return result.HasResult && result.Result == MessageModalResult.Acknowledged;
        }

        public async UniTask<ModalResult<TResult>> ShowAsync<TPanel, TData, TResult>(
            TData data,
            string address = null)
            where TPanel : ModalPanel<TData, TResult>
        {
            TPanel modal = await panelStack.PushModalAsync<TPanel, TData, TResult>(data, address);
            return await panelStack.GetModalResult<TPanel, TResult>(modal);
        }

        public async UniTask ShowAsync<TPanel, TData, TResult>(
            TData data,
            Action<bool, TResult> callback,
            string address = null)
            where TPanel : ModalPanel<TData, TResult>
        {
            ModalResult<TResult> result = await ShowAsync<TPanel, TData, TResult>(data, address);
            callback?.Invoke(result.HasResult, result.Result);
        }
    }
}
