using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.Preview.GUI
{
    public sealed class ModalContainer : StackContainer
    {
        [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField, Min(0f)] private float backdropFadeDuration = 0.2f;

        private ModalBackdrop backdrop;
        private Image backdropImage;
        private CanvasGroup backdropCanvasGroup;

        public ModalPanel Current => StackCount == 0 ? null : (ModalPanel)GetStackEntry(StackCount - 1).Panel;

        public override void Initialize(IPanelLoader panelLoader)
        {
            base.Initialize(panelLoader);
            CreateBackdrop();
        }

        /// <summary>
        /// 无参数，无返回值的模态面板
        /// </summary>
        /// <typeparam name="TModal">模态面板类型</typeparam>
        /// <param name="key">预制体资源键值</param>
        /// <returns></returns>
        public UniTask<TModal> PushAsync<TModal>(string key) where TModal : ModalPanel
        {
            return PushCoreAsync<TModal>(
                key,
                (modal, cancellationToken) => modal.PrepareInternalAsync(cancellationToken),
                null);
        }

        /// <summary>
        /// 带返回值的模态面板
        /// </summary>
        /// <typeparam name="TModal">模态面板类型</typeparam>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key">预制体资源键值</param>
        /// <param name="callback">结果回调函数</param>
        /// <returns></returns>
        public UniTask<TModal> PushAsync<TModal, TResult>(
            string key,
            ModalResultCallback<TResult> callback = null)
            where TModal : ModalPanel<TResult>
        {
            return PushCoreAsync<TModal>(
                key,
                (modal, cancellationToken) => modal.PrepareInternalAsync(cancellationToken),
                modal => modal.BindResultCallback(callback));
        }

        /// <summary>
        /// 带参数和返回值的模态面板
        /// </summary>
        /// <typeparam name="TModal">模态面板类型</typeparam>
        /// <typeparam name="TData">参数类型</typeparam>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key">预制体资源键值</param>
        /// <param name="data">面传递给模态板的参数</param>
        /// <param name="callback">结果回调函数</param>
        /// <returns></returns>
        public UniTask<TModal> PushAsync<TModal, TData, TResult>(
            string key,
            TData data,
            ModalResultCallback<TResult> callback = null)
            where TModal : ModalPanel<TData, TResult>
        {
            return PushCoreAsync<TModal>(
                key,
                (modal, cancellationToken) => modal.PrepareInternalAsync(data, cancellationToken),
                modal => modal.BindResultCallback(callback));
        }

        private async UniTask<TModal> PushCoreAsync<TModal>(
            string key,
            Func<TModal, CancellationToken, UniTask> prepareAsync,
            Action<TModal> bindCallback)
            where TModal : ModalPanel
        {
            if (IsDisposed || IsBusy)
            {
                return null;
            }

            if (Loader == null)
            {
                throw new InvalidOperationException("ModalContainer must be initialized before pushing a modal.");
            }

            IsBusy = true;
            StackEntry previousEntry = StackCount == 0 ? null : GetStackEntry(StackCount - 1);
            GameObject prefab = null;
            GameObject instance = null;
            TModal modal = null;
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                //禁用前一个模态面板的交互
                if (previousEntry != null)
                {
                    previousEntry.Panel.SetInteractable(false);
                }

                //加载预制体资源
                prefab = await Loader.LoadAsync(key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (prefab == null)
                {
                    return null;
                }

                instance = Instantiate(prefab, transform, false);
                instance.SetActive(false);

                modal = instance.GetComponent<TModal>();
                if (modal == null)
                {
                    //实例缺少 ModalPanel 组件：committed 保持 false，由 finally 集中收尾销毁实例并释放资源
                    return null;
                }

                modal.Bind(this);
                //给modal绑定回调函数
                bindCallback?.Invoke(modal);
                await prepareAsync(modal, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                //执行生命周期函数
                modal.OpenInternal();

                //C1：可见期先 Show 再入场；隐藏期新 Modal 保持逻辑隐藏——
                //不先 Show 再 Hide、不触发 OnShow、不播放入场与 Backdrop 淡入
                if (!SuppressVisuals)
                {
                    modal.ShowInternal();
                    modal.SetInteractable(false);
                }
                else
                {
                    //C1 隐藏期：不执行 OnShow 与入场，保持实例失活；交互组同步禁用，不启用输入
                    modal.SetInteractable(false);
                }

                MoveBackdropBelowPanel(modal);

                //backdrop动画和panel进入动画同时执行
                //（C1：经视觉作用域等待，Hide 停止视觉后按隐藏期规则继续；隐藏期整段跳过）
                if (!SuppressVisuals)
                {
                    UniTask enterTask = RunEnterTransitionAsync(modal, VisualCancellationToken);
                    UniTask backdropTask = previousEntry == null
                        ? FadeBackdropAsync(1f, VisualCancellationToken)
                        : UniTask.CompletedTask;
                    await WaitVisualTransitionAsync(UniTask.WhenAll(enterTask, backdropTask), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                //C1：可见期启用交互并交接焦点；隐藏期保持逻辑隐藏、不启用输入。
                //隐藏期入栈且容器中途恢复显示的 Modal 不在此补播（IsVisible 为假），
                //由下一次 ShowAsync 的呈现流程按最终栈统一补齐
                if (!SuppressVisuals && modal.IsVisible)
                {
                    modal.SetInteractable(true);
                    modal.FocusDefaultInternal();
                }

                //C1：可见期 Push 的入场视觉被 Hide 中断时，Modal 已执行过 Show——
                //按隐藏期规则收回逻辑隐藏（恰好一次 OnHide）；显示期由 ShowAsync 呈现补齐
                if (SuppressVisuals && modal.IsVisible)
                {
                    modal.HideInternal();
                }

                AddStackEntry(modal, prefab);
                committed = true;
                return modal;
            }
            finally
            {
                //F1：操作级单层 try/finally，未提交（入栈失败）时集中收尾，检测分支只 return
                //Busy 先于收尾复位：收尾中 Release 抛错时容器仍可用（与 PageContainer 口径一致）
                IsBusy = false;

                if (!committed)
                {
                    RollbackPush(modal, instance, prefab, previousEntry);
                }
            }
        }

        //Push 失败的集中收尾（同步，只服务 Push 失败路径）：销毁失败实例、释放资源、
        //恢复前一个 Modal 的交互与焦点、复位 Backdrop；由 PushCoreAsync 的 finally 在未提交时调用
        private void RollbackPush(
            ModalPanel modal,
            GameObject instance,
            GameObject prefab,
            StackEntry previousEntry)
        {
            //失败实例的 Hide/Close 由 Panel 幂等保证安全；instance 保存原始引用，
            //组件缺失（modal 为空）或实例已随容器销毁（Unity 侧判空）时仍能正确收尾
            if (instance != null)
            {
                if (modal != null)
                {
                    modal.HideInternal();
                    modal.CloseInternal();
                    modal.Unbind();
                }

                Destroy(instance);
            }

            if (prefab != null)
            {
                Loader.Release(prefab);
            }

            //恢复前一个 Modal 的交互；T1 幂等化后重复 ShowInternal 不再隐式恢复焦点，改为显式焦点补偿
            if (previousEntry != null && previousEntry.Panel != null)
            {
                previousEntry.Panel.SetInteractable(true);
                if (previousEntry.Panel.IsVisible)
                {
                    previousEntry.Panel.FocusDefaultInternal();
                }
            }

            //恢复 Backdrop 位置与射线状态：首个 Modal 入栈失败时复位为隐藏；
            //F4：容器已销毁/接管（IsDisposed）时 Backdrop 即将销毁或已随清栈处理，跳过复位
            if (!IsDisposed)
            {
                if (StackCount == 0)
                {
                    SetBackdropAlpha(0f);
                    SetBackdropRaycasts(false);
                }
                else
                {
                    MoveBackdropBelowPanel(GetStackEntry(StackCount - 1).Panel);
                    SetBackdropRaycasts(true);
                }
            }
        }

        /// <summary>
        /// 返回：只移除当前栈顶的模态面板（一次一个），等价于取消语义（hasResult=false）
        /// </summary>
        public UniTask<bool> BackAsync()
        {
            if (IsDisposed || IsBusy || StackCount == 0)
            {
                return UniTask.FromResult(false);
            }

            return BackCoreAsync(false, null);
        }

        internal UniTask<bool> CancelAsync(ModalPanel modal)
        {
            if (modal == null || !IsTop(modal) || IsDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return BackCoreAsync(false, null);
        }

        internal UniTask<bool> CompleteAsync<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            if (modal == null || !IsTop(modal) || IsDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return BackCoreAsync(true, result);
        }

        internal void HandleBackdropClick()
        {
            if (IsDisposed || IsBusy || StackCount == 0 || !Current.CloseOnBackdropClick)
            {
                return;
            }

            CancelAsync(Current).Forget();
        }

        //Back/Cancel/Complete 共用的单条目收尾：一次只处理当前栈顶，接受后确定关闭、不回滚
        private async UniTask<bool> BackCoreAsync(bool hasResult, object result)
        {
            IsBusy = true;
            StackEntry currentEntry = GetStackEntry(StackCount - 1);
            StackEntry nextTop = StackCount >= 2 ? GetStackEntry(StackCount - 2) : null;
            Action notification = null;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                //R2 实例有效性检查：实例可能在 Back 前已被外部销毁，仅存活时禁用交互并运行退出过渡
                if (currentEntry.Panel != null)
                {
                    currentEntry.Panel.SetInteractable(false);
                }

                //过渡门控挂起点前，Backdrop 已移动到剩余栈顶之下
                if (nextTop != null)
                {
                    MoveBackdropBelowPanel(nextTop.Panel);
                }

                //退出动画只需当前 Modal Exit；最后一个 Modal 退出时与 Backdrop 淡出并行
                //（F5：普通 WhenAll 等待，不逐项包装、不收集错误）。
                //C1：隐藏期跳过退出过渡与 Backdrop 淡出，直接进入清理与通知（不等容器显示）
                if (!SuppressVisuals)
                {
                    UniTask exitTask = currentEntry.Panel != null
                        ? RunExitTransitionAsync(currentEntry.Panel, VisualCancellationToken)
                        : UniTask.CompletedTask;
                    UniTask backdropTask = nextTop == null
                        ? FadeBackdropAsync(0f, VisualCancellationToken)
                        : UniTask.CompletedTask;
                    await WaitVisualTransitionAsync(UniTask.WhenAll(exitTask, backdropTask), cancellationToken);
                }
            }
            finally
            {
                //F4：销毁/Busy 关闭接管（IsDisposed 前移）已接管整栈时，Back 的异步收尾
                //不再登记通知、清理条目、移除索引、复位 Backdrop 或恢复剩余栈顶，
                //防止重复释放与错误索引；接管方会按同一所有权规则完成清理与通知交付
                if (!IsDisposed)
                {
                    //R5：退出动画失败/取消也不恢复当前 Modal，继续清理；
                    //无论实例是否存活都登记通知、释放资源并移除条目（防重复释放）。
                    //登记与 Unbind 是纯 C# 操作，实例已销毁时仍可执行，通知按条目交付不依赖 Panel 存活
                    ModalPanel modal = (ModalPanel)currentEntry.Panel;
                    notification = modal.CaptureResultNotification(hasResult, result);
                    modal.Unbind();

                    if (modal != null)
                    {
                        //Hide/Close 由 Panel 幂等保证安全，失败实例不再重新入场
                        modal.HideInternal();
                        modal.CloseInternal();
                        Destroy(modal.gameObject);
                    }

                    //F4 所有权转移：先移出条目再释放引用（销毁清栈不再拿到同一条目，防重复 Release）；
                    //移除仍晚于 Hide/Close 步骤：生命周期抛错时条目保留在栈内，由后续清栈接管
                    RemoveStackEntryAt(StackCount - 1);

                    if (currentEntry.Prefab != null)
                    {
                        Loader.Release(currentEntry.Prefab);
                    }

                    //更新 Backdrop 与剩余栈顶交互/焦点（外层已保证容器可用）
                    if (nextTop == null)
                    {
                        SetBackdropAlpha(0f);
                        SetBackdropRaycasts(false);
                    }
                    else
                    {
                        MoveBackdropBelowPanel(nextTop.Panel);
                        SetBackdropRaycasts(true);
                        if (nextTop.Panel != null)
                        {
                            //C1：仅可见期恢复剩余栈顶显示与输入；隐藏期不 Show 前层、不启用输入，
                            //由下一次 ShowAsync 的呈现流程按最终栈统一恢复
                            if (!SuppressVisuals)
                            {
                                nextTop.Panel.SetInteractable(true);
                                nextTop.Panel.ShowInternal();
                                //T1 幂等化后 ShowInternal 不再隐式恢复焦点，改为显式焦点补偿
                                if (nextTop.Panel.IsVisible)
                                {
                                    nextTop.Panel.FocusDefaultInternal();
                                }
                            }
                        }
                    }

                    IsBusy = false;

                    //清理完成、Busy 解除后按条目交付通知；
                    //回调异常已由 InvokeSafely 隔离，不会掩盖过渡异常
                    notification?.Invoke();
                }
            }

            return true;
        }

        //C1：容器显示的呈现流程——按多层展示规则恢复所需层并同步 Backdrop：
        //全部条目按最终栈执行 OnShow 与入场（隐藏期入栈的层在此补齐，不重放隐藏期中间态），
        //Backdrop 依最终栈恢复淡入或保持在场；动画结束后仅启用栈顶层输入
        protected override async UniTask PresentOnShowAsync()
        {
            if (StackCount == 0)
            {
                //无 Modal：Backdrop 按最终栈复位为隐藏（状态同步，不播放淡出；
                //容器未 Initialize 即调用 ShowAsync 属误用，此处沿用 R2 风格判空兜底）
                if (backdrop != null)
                {
                    SetBackdropAlpha(0f);
                    SetBackdropRaycasts(false);
                }

                return;
            }

            //Backdrop 同步到当前栈顶之下
            MoveBackdropBelowPanel(GetStackEntry(StackCount - 1).Panel);

            //呈现中再次隐藏：不启动视觉、不启用输入，由下一次 ShowAsync 呈现最终态
            if (!IsVisible)
            {
                return;
            }

            List<UniTask> visuals = new List<UniTask>();
            for (int index = 0; index < StackCount; index++)
            {
                Panel layer = GetStackEntry(index).Panel;
                //隐藏期入栈的层在此执行 OnShow；已显示层由幂等空操作，入场同理不再重复播放
                bool layerWasVisible = layer.IsVisible;
                layer.ShowInternal();
                layer.SetInteractable(false);
                if (!layerWasVisible)
                {
                    visuals.Add(RunEnterTransitionAsync(layer, VisualCancellationToken));
                }
            }

            //Backdrop 同步：依最终栈恢复淡入（隐藏期跳过淡入时 alpha 可能仍为 0）或保持在场
            if (backdropCanvasGroup.alpha < 1f)
            {
                visuals.Add(FadeBackdropAsync(1f, VisualCancellationToken));
            }
            else
            {
                SetBackdropRaycasts(true);
            }

            await WaitVisualTransitionAsync(UniTask.WhenAll(visuals), LifetimeCancellationToken);

            //动画结束后仅启用栈顶层输入；呈现中再次隐藏/接管则不启用（销毁按 F4 以取消异常向上传播）
            if (IsVisible && !IsDisposed)
            {
                Panel top = GetStackEntry(StackCount - 1).Panel;
                top.SetInteractable(true);
                top.FocusDefaultInternal();
            }
        }

        protected override void OnClose()
        {
            //F4 收尾（2026-09-12）：父容器关闭触发本容器 OnClose 且本容器 Busy 时立即接管，
            //不排队/挂起：先标记不可用并取消在途操作，在途 Back/Push 的异步收尾据此跳过条目清理，
            //清栈由下方 ClearModals 与销毁共用同一套所有权规则
            if (IsBusy)
            {
                BeginBusyTakeOver();
            }

            //显式维护 Busy：清栈期间容器不接受新操作，清理完成且 Busy 解除后交付通知，容器本身继续可用
            IsBusy = true;
            List<Action> notifications;
            try
            {
                notifications = ClearModals();
                //Modal 额外复位 Backdrop：属于容器自身状态清理，随清栈完成、先于通知交付
                SetBackdropAlpha(0f);
                SetBackdropRaycasts(false);
            }
            finally
            {
                IsBusy = false;
            }

            //清理完成、Busy 解除后按条目交付通知；回调异常已由 InvokeSafely 隔离，不影响容器状态
            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }

            base.OnClose();
        }

        //R4 共用清栈：从栈顶向下逐条目先移出栈（所有权转移）再同步清理——关闭仍存活实例、
        //销毁实例、释放条目 Prefab 引用；移出后的条目不会被后续清栈重复处理。另收集每个条目的
        //无结果通知并解绑，方法内部不执行任何外部回调，由调用方在清理完成后交付。
        //通知登记不依赖实例存活（Modal 已提前销毁时回调由栈条目持有）；生命周期调用不做
        //IsVisible/IsOpen 包裹判断，由 Panel 内部幂等与 Unity 有效性检查决定（R2/R3）
        private List<Action> ClearModals()
        {
            List<Action> notifications = new List<Action>();
            while (StackCount > 0)
            {
                StackEntry entry = GetStackEntry(StackCount - 1);
                RemoveStackEntryAt(StackCount - 1);

                //登记与解绑是纯 C# 操作，实例已提前销毁时仍可执行
                ModalPanel modal = (ModalPanel)entry.Panel;
                notifications.Add(modal.CaptureResultNotification(false, null));
                modal.Unbind();

                //R2 实例有效性检查：仅存活实例执行生命周期调用与销毁；资源释放不放在实例分支内
                if (entry.Panel != null)
                {
                    modal.HideInternal();
                    modal.CloseInternal();
                    Destroy(entry.Panel.gameObject);
                }

                if (entry.Prefab != null)
                {
                    Loader.Release(entry.Prefab);
                }
            }

            return notifications;
        }

        private async UniTask FadeBackdropAsync(float targetAlpha, CancellationToken cancellationToken)
        {
            backdrop.gameObject.SetActive(true);
            SetBackdropRaycasts(targetAlpha > 0f);
            float startAlpha = backdropCanvasGroup.alpha;

            if (backdropFadeDuration <= 0f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetBackdropAlpha(targetAlpha);
                SetBackdropRaycasts(targetAlpha > 0f);
                return;
            }

            float elapsed = 0f;
            while (elapsed < backdropFadeDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                SetBackdropAlpha(Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / backdropFadeDuration)));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            SetBackdropAlpha(targetAlpha);
            SetBackdropRaycasts(targetAlpha > 0f);
        }

        //创建Backdrop；仅在Initialize中调用一次，由容器独占管理并随容器销毁，不再自动重建
        private void CreateBackdrop()
        {
            GameObject backdropObject = new GameObject(
                "ModalBackdrop",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(ModalBackdrop));
            backdropObject.transform.SetParent(transform, false);
            RectTransform rect = backdropObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            backdrop = backdropObject.GetComponent<ModalBackdrop>();
            backdrop.Initialize(this);
            backdropImage = backdropObject.GetComponent<Image>();
            backdropImage.color = backdropColor;
            backdropImage.raycastTarget = true;
            backdropCanvasGroup = backdropObject.GetComponent<CanvasGroup>();
            backdropCanvasGroup.alpha = 0f;
            SetBackdropRaycasts(false);
        }

        private void MoveBackdropBelowPanel(Panel panel)
        {
            //目标Panel可能已销毁的调用边界由R2处理，这里保留判空
            if (panel == null)
            {
                return;
            }

            //按Backdrop相对目标Panel的位置计算插入索引：Backdrop在Panel前方时插到Panel前一位，否则插到Panel原位
            int panelIndex = panel.transform.GetSiblingIndex();
            int backdropIndex = backdrop.transform.GetSiblingIndex();
            int targetIndex = backdropIndex < panelIndex ? panelIndex - 1 : panelIndex;
            backdrop.transform.SetSiblingIndex(targetIndex);
        }

        private void SetBackdropAlpha(float alpha)
        {
            backdropCanvasGroup.alpha = Mathf.Clamp01(alpha);
        }

        private void SetBackdropRaycasts(bool enabled)
        {
            backdropCanvasGroup.blocksRaycasts = enabled;
            backdropCanvasGroup.interactable = enabled;
        }

        private bool IsTop(ModalPanel modal)
        {
            return StackCount > 0 && ReferenceEquals(Current, modal);
        }

        protected override void CleanupStackOnDestroy()
        {
            //R4：销毁路径复用同一清栈方法（不访问即将销毁的 Backdrop）；
            //销毁入口（StackContainer.OnDestroy）已先标记不可用并取消在途操作，
            //通知在清理完成后交付，此时容器已不可用、不能接受新导航
            List<Action> notifications = ClearModals();

            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }
        }
    }
}
