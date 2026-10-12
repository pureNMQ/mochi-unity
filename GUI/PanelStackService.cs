using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    /// <summary>面板导航服务：Page/Modal 两栈的唯一持有与协调者，并托管模态 backdrop 与面板创建。</summary>
    /// <remarks>
    /// 两栈数据不对外暴露，避免调用方绕过本服务直接改栈。backdrop 为纯遮蔽：只压暗与阻挡被遮层输入，
    /// 无点击语义，Modal 关闭仅由面板自身出口与 <see cref="Back"/> 发起。
    /// 面板资源地址由调用方提供或经 <see cref="PanelAddressMap"/> 解析；调用方不接触面板实例与层级。
    /// </remarks>
    public sealed class PanelStackService : MonoBehaviour
    {
        private readonly List<Panel> pageStack = new List<Panel>();
        private readonly List<ModalPanel> modalStack = new List<ModalPanel>();
        private readonly Dictionary<Panel, UniTaskCompletionSource<ModalResult<object>>> pendingResults =
            new Dictionary<Panel, UniTaskCompletionSource<ModalResult<object>>>();
        private readonly Dictionary<string, Panel> hudPanels = new Dictionary<string, Panel>(StringComparer.Ordinal);

        private RectTransform pageLayer;
        private RectTransform modalLayer;
        private CanvasGroup pageLayerCanvasGroup;
        private bool layersBound;
        private IPanelLoader panelLoader;
        private ServiceProvider services;
        private GUIService guiService;
        private PanelAddressMap addressMap;
        private CancellationTokenSource lifetimeCancellation;

        /// <summary>最后一个 Page 出栈后触发。</summary>
        public event Action StackEmptied;

        /// <summary>Page 栈条目数。</summary>
        public int PageCount => pageStack.Count;
        /// <summary>Modal 栈条目数。</summary>
        public int ModalCount => modalStack.Count;
        /// <summary>兼容旧查询；最终架构不创建 backdrop。</summary>
        public int BackdropCount => 0;
        /// <summary>Page 栈顶，栈空时为 null。</summary>
        public Panel CurrentPage => pageStack.Count == 0 ? null : pageStack[pageStack.Count - 1];
        /// <summary>Modal 栈顶，栈空时为 null。</summary>
        public ModalPanel CurrentModal => modalStack.Count == 0 ? null : modalStack[modalStack.Count - 1];

        /// <summary>
        /// 绑定 Page 层与 Modal 层；Modal 层的 backdrop 与面板呈现均由本服务负责。
        /// </summary>
        public void BindLayers(RectTransform pageLayerTransform, RectTransform modalLayerTransform)
        {
            BindLayers(pageLayerTransform, modalLayerTransform, null, null, null);
        }

        public void BindLayers(GUIService service, PanelAddressMap panelAddressMap = null)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            guiService = service;
            BindLayers(
                service.GetLayer(GUILayer.Page),
                service.GetLayer(GUILayer.Modal),
                null,
                null,
                panelAddressMap);
        }

        /// <summary>
        /// 绑定 Page 层、Modal 层与面板创建所需依赖；面板资源地址经 <paramref name="panelAddressMap"/> 解析。
        /// </summary>
        public void BindLayers(
            RectTransform pageLayerTransform,
            RectTransform modalLayerTransform,
            IPanelLoader loader,
            ServiceProvider panelServices = null,
            PanelAddressMap panelAddressMap = null)
        {
            if (pageLayerTransform == null)
            {
                throw new ArgumentNullException(nameof(pageLayerTransform));
            }

            if (modalLayerTransform == null)
            {
                throw new ArgumentNullException(nameof(modalLayerTransform));
            }

            if (layersBound)
            {
                throw new InvalidOperationException("Layers are already bound; each panel stack service owns one pair of layers.");
            }

            pageLayer = pageLayerTransform;
            modalLayer = modalLayerTransform;
            pageLayerCanvasGroup = ResolveLayerCanvasGroup(pageLayer);
            panelLoader = loader;
            services = panelServices ?? Services.Root ?? new ServiceProvider();
            addressMap = panelAddressMap ?? new PanelAddressMap();
            lifetimeCancellation = new CancellationTokenSource();
            layersBound = true;
        }

        /// <summary>
        /// 异步创建并推入页面；<paramref name="address"/> 省略时按类型经地址映射解析。
        /// </summary>
        public async UniTask<TPanel> PushPageAsync<TPanel>(string address = null) where TPanel : Panel
        {
            RequireLayers();
            TPanel page = guiService != null
                ? await guiService.CreatePanelAsync<TPanel>(ResolveAddress<TPanel>(address))
                : await PanelFactory.CreateAsync<TPanel>(
                    panelLoader, ResolveAddress<TPanel>(address), pageLayer, services, LifetimeToken);
            if (page == null)
            {
                return null;
            }

            Push(page);
            return page;
        }

        /// <summary>
        /// 异步创建并推入带数据页面；<paramref name="address"/> 省略时按类型经地址映射解析。
        /// </summary>
        public async UniTask<TPanel> PushPageAsync<TPanel, TData>(TData data, string address = null)
            where TPanel : Panel<TData>
        {
            RequireLayers();
            TPanel page = guiService != null
                ? await guiService.CreatePanelAsync<TPanel, TData>(data, ResolveAddress<TPanel>(address))
                : await PanelFactory.CreateAsync<TPanel, TData>(
                    panelLoader, ResolveAddress<TPanel>(address), pageLayer, data, services, LifetimeToken);
            if (page == null)
            {
                return null;
            }

            Push(page);
            return page;
        }

        /// <summary>
        /// 异步创建并推入弹窗；结果经 <see cref="GetModalResult{TPanel,TResult}"/> 等待。
        /// </summary>
        public async UniTask<TPanel> PushModalAsync<TPanel, TData, TResult>(TData data, string address = null)
            where TPanel : ModalPanel<TData, TResult>
        {
            RequireLayers();
            TPanel modal = guiService != null
                ? await guiService.CreateModalAsync<TPanel, TData, TResult>(data, ResolveAddress<TPanel>(address))
                : await PanelFactory.CreateAsync<TPanel, TData, TResult>(
                    panelLoader, ResolveAddress<TPanel>(address), modalLayer, data, services, LifetimeToken);
            if (modal == null)
            {
                return null;
            }

            Push(modal);
            return modal;
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

        /// <summary>打开常驻 HUD 面板；同一地址重复打开返回既有实例。</summary>
        public async UniTask<TPanel> OpenHudAsync<TPanel>(string address = null) where TPanel : Panel
        {
            RequireLayers();
            string resolved = ResolveAddress<TPanel>(address);
            if (hudPanels.TryGetValue(resolved, out Panel existing))
            {
                return existing as TPanel;
            }

            TPanel panel = await PanelFactory.CreateAsync<TPanel>(
                panelLoader, resolved, HudLayer, services, LifetimeToken);
            if (panel == null)
            {
                return null;
            }

            hudPanels[resolved] = panel;
            panel.Closed += OnHudPanelClosed;
            panel.Show();
            return panel;
        }

        /// <summary>关闭常驻 HUD 面板并从服务登记中移除。</summary>
        public bool CloseHud(Panel panel)
        {
            if (panel == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, Panel> entry in hudPanels)
            {
                if (ReferenceEquals(entry.Value, panel))
                {
                    hudPanels.Remove(entry.Key);
                    break;
                }
            }

            panel.Closed -= OnHudPanelClosed;
            panel.Close();
            return true;
        }

        private RectTransform HudLayer => modalLayer.parent.Find("HUDLayer") as RectTransform;

        private void OnHudPanelClosed(Panel panel)
        {
            panel.Closed -= OnHudPanelClosed;
            foreach (KeyValuePair<string, Panel> entry in hudPanels)
            {
                if (ReferenceEquals(entry.Value, panel))
                {
                    hudPanels.Remove(entry.Key);
                    break;
                }
            }
        }

        private string ResolveAddress<TPanel>(string address) where TPanel : Panel
        {
            if (!string.IsNullOrEmpty(address))
            {
                return address;
            }

            if (addressMap.TryGetAddress(typeof(TPanel), out string mapped))
            {
                return mapped;
            }

            return typeof(TPanel).Name;
        }

        private CancellationToken LifetimeToken =>
            lifetimeCancellation == null ? CancellationToken.None : lifetimeCancellation.Token;

        private void OnDestroy()
        {
            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;

            foreach (Panel panel in new List<Panel>(hudPanels.Values))
            {
                if (panel != null)
                {
                    panel.Closed -= OnHudPanelClosed;
                    panel.CloseImmediate();
                }
            }

            hudPanels.Clear();
        }

        /// <summary>
        /// 推入面板：ModalPanel 进 Modal 栈，其余进 Page 栈。
        /// </summary>
        public void Push(Panel panel)
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            if (panel is ModalPanel modal)
            {
                RequireLayers();
                PushModal(modal);
                return;
            }

            RequireLayers();
            CloseAllModals();
            PushPage(panel);
        }

        /// <summary>关闭面板，无论它位于哪个栈；不在任何栈中时返回 false。</summary>
        public bool Close(Panel panel)
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

            RefreshCoverage();
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

            //取焦点必须先于遮蔽：被遮面板只有先记下自己停滞的焦点，解除遮蔽后才有正确的恢复目标
            CaptureCoveredFocus();
            modal.Show();
            modal.FocusDefaultInternal();
            RefreshCoverage();
        }

        private void PushPage(Panel page)
        {
            if (pageStack.Contains(page))
            {
                throw new InvalidOperationException("The page is already in the page stack.");
            }

            page.transform.SetParent(pageLayer, false);
            Panel previous = CurrentPage;
            pageStack.Add(page);
            page.Closed += OnPageClosed;

            //同栈盖页同样先采焦点：被盖页解除遮蔽后要回到自己失去焦点前的目标
            previous?.CaptureFocusBeforeCover();
            page.Show();

            if (previous != null && !ReferenceEquals(previous, page))
            {
                //被新页盖住：收回显示与交互但保留实例渲染
                previous.CoverInternal();
            }

            //新页的显现由自身 Show 负责，这里只重算 Modal 遮蔽
            RefreshCoverage();
        }

        private void OnPageClosed(Panel page)
        {
            int index = pageStack.IndexOf(page);
            if (index < 0)
            {
                return;
            }

            page.Closed -= OnPageClosed;
            bool wasTop = ReferenceEquals(CurrentPage, page);
            pageStack.RemoveAt(index);

            //关掉的是显示中的栈顶：暴露出来的下层页按原状显现
            if (wasTop)
            {
                RefreshCoverage();
                if (modalStack.Count == 0)
                {
                    CurrentPage?.UncoverInternal();
                }
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

            int index = modalStack.IndexOf(closedModal);
            modalStack.RemoveAt(index);

            //先按剩余栈重算遮蔽：Page 层与下层 Modal 的锁随栈顶变化即时更新
            RefreshCoverage();

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
            Panel page = CurrentPage;
            if (page == null)
            {
                return false;
            }

            page.Close();
            return true;
        }

        //遮蔽规则：Modal 在场时 Page 层与下层 Modal 去激活（保持渲染但不可交互、不接收射线），仅栈顶 Modal 可交互
        private void RefreshCoverage()
        {
            bool modalPresent = modalStack.Count > 0;

            if (pageLayerCanvasGroup != null)
            {
                pageLayerCanvasGroup.interactable = !modalPresent;
                pageLayerCanvasGroup.blocksRaycasts = !modalPresent;
            }

            if (!modalPresent)
            {
                return;
            }

            foreach (Panel page in pageStack)
            {
                DeactivatePanel(page);
            }

            for (int index = 0; index < modalStack.Count - 1; index++)
            {
                DeactivatePanel(modalStack[index]);
            }
        }

        private static void DeactivatePanel(Panel panel)
        {
            panel?.CoverInternal();
        }

        //遮蔽会收回焦点：先让即将被遮的面板记下自己停滞的焦点，再做任何可能转移焦点的动作
        private void CaptureCoveredFocus()
        {
            foreach (Panel page in pageStack)
            {
                page?.CaptureFocusBeforeCover();
            }

            for (int index = 0; index < modalStack.Count; index++)
            {
                modalStack[index]?.CaptureFocusBeforeCover();
            }
        }

        private static CanvasGroup ResolveLayerCanvasGroup(RectTransform layer)
        {
            CanvasGroup group = layer.GetComponent<CanvasGroup>();
            return group != null ? group : layer.gameObject.AddComponent<CanvasGroup>();
        }

        private void RequireLayers()
        {
            if (modalLayer == null)
            {
                throw new InvalidOperationException("BindLayers must be called before using the panel stacks.");
            }
        }
    }
}
