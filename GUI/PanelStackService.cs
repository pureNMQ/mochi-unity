using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mochi.Unity.GUI
{
    /// <summary>面板导航服务：Page 与 Modal 两栈的唯一持有与协调者。</summary>
    /// <remarks>
    /// 两栈数据不对外暴露，避免调用方绕过本服务直接改栈。面板实例一律经 <see cref="GUIService"/> 按资源地址创建，
    /// 调用方不接触实例与层级；越过该服务创建的实例可经 <see cref="Push"/> 直接入栈，此时不登记也不回收。
    /// 推入顺序固定为先遮蔽旧面板再显示新面板，因此每条进出栈路径都自行维持两条不变量：
    /// Modal 栈非空时页面与下层 Modal 全部处于遮蔽态，仅栈顶 Modal 可交互；
    /// Modal 栈为空时仅栈顶页不遮蔽。
    /// </remarks>
    public sealed class PanelStackService : MonoBehaviour
    {
        private readonly List<InteractivePanel> pageStack = new List<InteractivePanel>();
        private readonly List<ModalPanel> modalStack = new List<ModalPanel>();
        private readonly Dictionary<Panel, UniTaskCompletionSource<ModalResult<object>>> pendingResults =
            new Dictionary<Panel, UniTaskCompletionSource<ModalResult<object>>>();

        private RectTransform pageLayer;
        private RectTransform modalLayer;
        private bool layersBound;
        private GUIService guiService;
        private InputAction cancelAction;

        /// <summary>最后一个 Page 出栈后触发。</summary>
        public event Action StackEmptied;

        /// <summary>
        /// 取消输入无人接住时触发：栈空，或前台面板把 <see cref="InteractivePanel.AllowCancelBack"/> 关掉。
        /// 应用层据此让按键有反馈，而不是把它吞在框架里。
        /// </summary>
        public event Action CancelRejected;

        /// <summary>Page 栈条目数。</summary>
        public int PageCount => pageStack.Count;
        /// <summary>Modal 栈条目数。</summary>
        public int ModalCount => modalStack.Count;
        /// <summary>Page 栈顶，栈空时为 null。</summary>
        public InteractivePanel CurrentPage => pageStack.Count == 0 ? null : pageStack[pageStack.Count - 1];
        /// <summary>Modal 栈顶，栈空时为 null。</summary>
        public ModalPanel CurrentModal => modalStack.Count == 0 ? null : modalStack[modalStack.Count - 1];

        /// <summary>绑定 GUI 服务；Page 层与 Modal 层由该服务解析。</summary>
        /// <exception cref="ArgumentNullException"><paramref name="service"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">该实例已绑定过层。</exception>
        public void BindLayers(GUIService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (layersBound)
            {
                throw new InvalidOperationException("Layers are already bound; each panel stack service owns one pair of layers.");
            }

            guiService = service;
            pageLayer = service.GetLayer(GUILayer.Page);
            modalLayer = service.GetLayer(GUILayer.Modal);
            layersBound = true;
        }

        /// <summary>绑定取消输入；收到取消后由本服务按前台面板路由，面板自身不再订阅取消键。</summary>
        /// <param name="cancelAction">项目 UI 映射的取消动作，生命周期归调用方；重复绑定同一动作无副作用。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cancelAction"/> 为 null。</exception>
        public void BindCancelInput(InputAction cancelAction)
        {
            if (cancelAction == null)
            {
                throw new ArgumentNullException(nameof(cancelAction));
            }

            if (ReferenceEquals(this.cancelAction, cancelAction))
            {
                return;
            }

            UnbindCancelInput();
            this.cancelAction = cancelAction;
            cancelAction.performed += OnCancelPerformed;
        }

        /// <summary>退订取消输入，此后取消键不再由本服务路由；未绑定时无副作用。</summary>
        public void UnbindCancelInput()
        {
            if (cancelAction == null)
            {
                return;
            }

            cancelAction.performed -= OnCancelPerformed;
            cancelAction = null;
        }

        /// <summary>异步创建并推入页面。</summary>
        /// <param name="address">面板的 Addressables 资源地址，必填。</param>
        /// <exception cref="ArgumentException"><paramref name="address"/> 为空。</exception>
        public UniTask<TPanel> PushPageAsync<TPanel>(string address = null) where TPanel : InteractivePanel
        {
            RequireLayers();
            return PushPageCoreAsync<TPanel>(RequireAddress(address));
        }

        /// <summary>异步创建并推入带数据页面。</summary>
        /// <param name="data">页面呈现的数据，打开前应用到面板。</param>
        /// <param name="address">面板的 Addressables 资源地址，必填。</param>
        /// <exception cref="ArgumentException"><paramref name="address"/> 为空。</exception>
        public UniTask<TPanel> PushPageAsync<TPanel, TData>(TData data, string address = null)
            where TPanel : InteractivePanel<TData>
        {
            RequireLayers();
            return PushPageCoreAsync<TPanel, TData>(data, RequireAddress(address));
        }

        /// <summary>异步创建并推入弹窗；结果经 <see cref="GetModalResult{TPanel,TResult}"/> 等待。</summary>
        /// <param name="data">弹窗呈现的数据，打开前应用到面板。</param>
        /// <param name="address">面板的 Addressables 资源地址，必填。</param>
        /// <exception cref="ArgumentException"><paramref name="address"/> 为空。</exception>
        public UniTask<TPanel> PushModalAsync<TPanel, TData, TResult>(TData data, string address = null)
            where TPanel : ModalPanel<TData, TResult>
        {
            RequireLayers();
            return PushModalCoreAsync<TPanel, TData, TResult>(data, RequireAddress(address));
        }

        /// <summary>
        /// 等待弹窗结果：玩家以结果语义关闭时返回该结果，经 back 或外部路径关闭时返回取消信号。
        /// </summary>
        public UniTask<ModalResult<TResult>> GetModalResult<TPanel, TResult>(TPanel modal)
            where TPanel : ModalPanel<TResult>
        {
            if (modal == null)
            {
                return UniTask.FromResult(ModalResult<TResult>.Cancelled());
            }

            UniTaskCompletionSource<ModalResult<object>> completionSource =
                new UniTaskCompletionSource<ModalResult<object>>();
            pendingResults[modal] = completionSource;
            modal.ResultReturned += (hasResult, result) =>
            {
                pendingResults.Remove(modal);
                completionSource.TrySetResult(ModalResult<object>.Create(hasResult, result));
            };
            return AwaitTypedResultAsync<TPanel, TResult>(modal, completionSource);
        }

        private static async UniTask<ModalResult<TResult>> AwaitTypedResultAsync<TPanel, TResult>(
            TPanel modal,
            UniTaskCompletionSource<ModalResult<object>> completionSource) where TPanel : ModalPanel<TResult>
        {
            ModalResult<object> raw = await completionSource.Task;
            if (!raw.HasResult)
            {
                return ModalResult<TResult>.Cancelled();
            }

            TResult typed = raw.Result is TResult result ? result : default;
            return ModalResult<TResult>.Create(true, typed);
        }

        private async UniTask<TPanel> PushPageCoreAsync<TPanel>(string address) where TPanel : InteractivePanel
        {
            TPanel page = await guiService.CreatePanelAsync<TPanel>(address);
            if (page == null)
            {
                return null;
            }

            PushPage(page);
            return page;
        }

        private async UniTask<TPanel> PushPageCoreAsync<TPanel, TData>(TData data, string address)
            where TPanel : InteractivePanel<TData>
        {
            TPanel page = await guiService.CreatePanelAsync<TPanel, TData>(data, address);
            if (page == null)
            {
                return null;
            }

            PushPage(page);
            return page;
        }

        private async UniTask<TPanel> PushModalCoreAsync<TPanel, TData, TResult>(TData data, string address)
            where TPanel : ModalPanel<TData, TResult>
        {
            TPanel modal = await guiService.CreateModalAsync<TPanel, TData, TResult>(data, address);
            if (modal == null)
            {
                return null;
            }

            PushModal(modal);
            return modal;
        }

        /// <summary>推入面板：<see cref="ModalPanel"/> 进 Modal 栈，其余进 Page 栈。</summary>
        public void Push(InteractivePanel panel)
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            RequireLayers();
            if (panel is ModalPanel modal)
            {
                PushModal(modal);
                return;
            }

            CloseAllModals();
            PushPage(panel);
        }

        /// <summary>关闭面板，无论它位于哪个栈；不在任何栈中时返回 false。</summary>
        public bool Close(InteractivePanel panel)
        {
            if (panel == null || (!pageStack.Contains(panel) && !modalStack.Contains(panel as ModalPanel)))
            {
                return false;
            }

            panel.Close();
            return true;
        }

        /// <summary>关闭栈顶 Modal；Modal 栈为空时改为退栈顶 Page。</summary>
        public bool Back()
        {
            RequireLayers();

            if (modalStack.Count > 0)
            {
                CloseAllModals();
                return true;
            }

            return PopTopPage();
        }

        //取消键在同一物理键上还绑着别处（战斗输入、世界呼出），未接住时不能静默吞掉，
        //否则玩家分不清按键是没生效还是被框架吃掉了
        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            HandleCancelInput();
        }

        private void HandleCancelInput()
        {
            if (modalStack.Count > 0)
            {
                ModalPanel modal = CurrentModal;
                if (modal != null && modal.AllowCancelBack && Cancel(modal))
                {
                    return;
                }

                CancelRejected?.Invoke();
                return;
            }

            InteractivePanel page = CurrentPage;
            if (page != null && page.AllowCancelBack)
            {
                page.Close();
                return;
            }

            CancelRejected?.Invoke();
        }

        /// <summary>以结果语义完成栈顶 Modal；调用方不需接触面板的结果通道。</summary>
        public bool CompleteModal<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            return Complete(modal, result);
        }

        /// <summary>服务内部关闭栈顶 Modal，继承 <see cref="ModalPanel"/> 的取消语义。</summary>
        internal bool Cancel(ModalPanel modal)
        {
            if (!ReferenceEquals(CurrentModal, modal))
            {
                return false;
            }

            modal.Close();
            return true;
        }

        /// <summary>服务内部完成栈顶 Modal 并交付结果。</summary>
        internal bool Complete<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            if (!ReferenceEquals(CurrentModal, modal))
            {
                return false;
            }

            //结果先于关闭交付：关闭流程会给未决等待按取消收场，顺序反了会把玩家选择覆盖成取消
            Action notification = modal.CaptureResultNotification(true, result);
            if (pendingResults.TryGetValue(modal, out UniTaskCompletionSource<ModalResult<object>> pending))
            {
                pendingResults.Remove(modal);
                pending.TrySetResult(ModalResult<object>.Create(true, result));
            }

            modal.Close();
            notification?.Invoke();
            return true;
        }

        /// <summary>面板已销毁时清掉仍挂在两栈中的条目，避免出现不可用项。</summary>
        public void PruneDestroyedPanels()
        {
            for (int index = pageStack.Count - 1; index >= 0; index--)
            {
                if (pageStack[index] != null)
                {
                    continue;
                }

                pageStack.RemoveAt(index);
            }

            for (int index = modalStack.Count - 1; index >= 0; index--)
            {
                if (modalStack[index] != null)
                {
                    continue;
                }

                modalStack[index].Closed -= OnModalClosed;
                modalStack.RemoveAt(index);
            }

            //销毁绕过 Closed 事件：栈顶可能仍是遮蔽态，按剩余栈内容重建两条不变量
            if (modalStack.Count > 0)
            {
                CoverLayersBelowTopModal();
                CurrentModal.UncoverInternal();
            }
            else
            {
                for (int index = 0; index < pageStack.Count - 1; index++)
                {
                    pageStack[index]?.CoverInternal();
                }

                CurrentPage?.UncoverInternal();
            }
        }

        private void PushModal(ModalPanel modal)
        {
            if (modalStack.Contains(modal))
            {
                throw new InvalidOperationException("The modal is already in the modal stack.");
            }

            modal.transform.SetParent(modalLayer, false);
            modalStack.Add(modal);
            modal.Closed += OnModalClosed;
            modal.BindService(this);

            //先遮旧后显新：旧面板在自己仍持有选中时完成交接，无需遮蔽后再补采焦点
            CoverLayersBelowTopModal();
            modal.Show();
        }

        private void PushPage(InteractivePanel page)
        {
            if (pageStack.Contains(page))
            {
                throw new InvalidOperationException("The page is already in the page stack.");
            }

            page.transform.SetParent(pageLayer, false);
            InteractivePanel previous = CurrentPage;
            pageStack.Add(page);
            page.Closed += OnPageClosed;

            previous?.CoverInternal();
            page.Show();
        }

        private void OnPageClosed(Panel page)
        {
            InteractivePanel closedPage = page as InteractivePanel;
            int index = closedPage == null ? -1 : pageStack.IndexOf(closedPage);
            if (index < 0)
            {
                return;
            }

            page.Closed -= OnPageClosed;
            bool wasTop = ReferenceEquals(CurrentPage, closedPage);
            pageStack.RemoveAt(index);

            //抽走栈顶后露出的下层页按原状显现；Modal 在场时页面整体保持遮蔽
            if (wasTop && modalStack.Count == 0)
            {
                CurrentPage?.UncoverInternal();
            }

            if (pageStack.Count == 0)
            {
                StackEmptied?.Invoke();
            }
        }

        private void OnModalClosed(Panel modal)
        {
            ModalPanel closedModal = modal as ModalPanel;
            if (closedModal == null || !modalStack.Contains(closedModal))
            {
                return;
            }

            closedModal.Closed -= OnModalClosed;
            closedModal.UnbindService();

            //结果语义的完成路径已在关闭前交付并移除挂起项；此处只兜底场外关闭，
            //避免调用方永久等待，并清空该面板已捕获的结果回调防止泄漏
            if (pendingResults.TryGetValue(closedModal, out UniTaskCompletionSource<ModalResult<object>> pending))
            {
                pendingResults.Remove(closedModal);
                closedModal.CaptureResultNotification(false, null);
                pending.TrySetResult(ModalResult<object>.Cancelled());
            }

            modalStack.Remove(closedModal);

            //按剩余栈恢复：Modal 仍在场时只有新栈顶解除遮蔽，栈空时回到栈顶页
            if (modalStack.Count == 0)
            {
                CurrentPage?.UncoverInternal();
            }
            else
            {
                CurrentModal.UncoverInternal();
            }
        }

        private void CloseAllModals()
        {
            while (CurrentModal != null)
            {
                CurrentModal.Close();
            }
        }

        private bool PopTopPage()
        {
            InteractivePanel page = CurrentPage;
            if (page == null)
            {
                return false;
            }

            page.Close();
            return true;
        }

        //遮蔽规则：Modal 在场时页面与下层 Modal 收回交互（保持渲染），仅栈顶 Modal 可交互
        private void CoverLayersBelowTopModal()
        {
            foreach (InteractivePanel page in pageStack)
            {
                page?.CoverInternal();
            }

            for (int index = 0; index < modalStack.Count - 1; index++)
            {
                modalStack[index].CoverInternal();
            }
        }

        //取消动作挂在常驻的项目输入资产上：服务销毁时必须退订，否则已销毁的实例还会收到取消
        private void OnDestroy()
        {
            UnbindCancelInput();
        }

        private static string RequireAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("Panel address is required.", nameof(address));
            }

            return address;
        }

        private void RequireLayers()
        {
            if (pageLayer == null || modalLayer == null)
            {
                throw new InvalidOperationException("BindLayers must be called before using the panel stacks.");
            }
        }
    }
}
