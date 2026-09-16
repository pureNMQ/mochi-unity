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
        /// <param name="data">传递给模态面板的参数</param>
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

        //Busy 仅覆盖加载+Prepare+逻辑提交，入场与 Backdrop 淡入并行 fire-and-forget，容器不等待
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
            TModal modal = null;
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                //禁用前一个模态面板的交互
                if (previousEntry != null && previousEntry.Panel != null)
                {
                    previousEntry.Panel.SetInteractable(false);
                }

                //工厂完成加载、绑定（Bind 与结果回调）、Prepare 与 OnOpen；失败返回 null 或抛出并已自收尾
                modal = await PanelFactory.CreateAsync<TModal>(
                    Loader,
                    key,
                    transform,
                    created =>
                    {
                        created.Bind(this);
                        bindCallback?.Invoke(created);
                    },
                    prepareAsync,
                    cancellationToken);
                if (modal == null)
                {
                    return null;
                }

                MoveBackdropBelowPanel(modal);

                if (!SuppressVisuals)
                {
                    //首个 Modal 入栈：Backdrop 淡入与入场并行（均 fire-and-forget）
                    if (previousEntry == null)
                    {
                        RunBackdropFadeAsync(1f);
                    }

                    //显示即提交状态、启用输入与焦点，入场过渡由 Panel fire-and-forget
                    modal.Show();
                }
                else
                {
                    //隐藏期：不执行 OnShow 与入场，保持实例失活；交互组同步禁用，不启用输入
                    modal.SetInteractable(false);
                }

                AddStackEntry(modal);
                committed = true;
                return modal;
            }
            finally
            {
                //操作级单层 try/finally，未提交（入栈失败）时集中收尾，检测分支只 return；
                //Busy 先于收尾复位：收尾中抛错时容器仍可用（与 PageContainer 口径一致）
                IsBusy = false;

                if (!committed)
                {
                    RollbackPush(modal, previousEntry);
                }
            }
        }

        //Push 失败的集中收尾（同步，只服务 Push 失败路径）：销毁失败实例、恢复前一个 Modal 的
        //交互与焦点、复位 Backdrop；工厂失败（modal 为 null）时资源已由工厂处理，此处只恢复前层与 Backdrop
        private void RollbackPush(ModalPanel modal, StackEntry previousEntry)
        {
            //失败实例的收尾由 Panel Close 幂等保证安全；显式释放与销毁即时收回（不等退场动画）
            if (modal != null)
            {
                modal.Unbind();
                modal.Close();
                modal.ReleasePrefabHandleImmediate();
                Destroy(modal.gameObject);
            }

            //恢复前一个 Modal 的交互；幂等 Show 不再隐式恢复焦点，改为显式焦点补偿
            if (previousEntry != null && previousEntry.Panel != null)
            {
                previousEntry.Panel.SetInteractable(true);
                if (previousEntry.Panel.IsVisible)
                {
                    previousEntry.Panel.FocusDefaultInternal();
                }
            }

            //恢复 Backdrop 位置与射线状态：首个 Modal 入栈失败时复位为隐藏；
            //容器已销毁/接管时 Backdrop 即将销毁或已随清栈处理，跳过复位
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

        //Back/Cancel/Complete 共用的单条目收尾：容器清理先行——结果登记解绑、Backdrop 与
        //剩余栈顶恢复、条目解除关系，然后关闭面板（自毁接管）；结果在逻辑清理完成时交付，
        //不等退场动画与延迟销毁；剩余栈顶恢复抛错也不跳过当前面板关闭（确定关闭、不回滚）
        private UniTask<bool> BackCoreAsync(bool hasResult, object result)
        {
            IsBusy = true;
            StackEntry currentEntry = GetStackEntry(StackCount - 1);
            StackEntry nextTop = StackCount >= 2 ? GetStackEntry(StackCount - 2) : null;
            Action notification = null;

            try
            {
                //登记与解绑是纯 C# 操作，实例已提前销毁时仍可执行
                ModalPanel modal = (ModalPanel)currentEntry.Panel;
                notification = modal.CaptureResultNotification(hasResult, result);
                modal.Unbind();

                //Backdrop 移到剩余栈顶之下（视觉定位，不构成同步点）
                if (nextTop != null)
                {
                    MoveBackdropBelowPanel(nextTop.Panel);
                }

                RemoveStackEntryAt(StackCount - 1);
                try
                {
                    if (!SuppressVisuals)
                    {
                        //最后一个 Modal 退出：Backdrop 淡出与退场并行 fire-and-forget
                        if (nextTop == null)
                        {
                            RunBackdropFadeAsync(0f);
                        }

                        //剩余栈顶恢复显示与输入（退场动画在其上方自行淡出，天然无缝）
                        if (nextTop != null && nextTop.Panel != null)
                        {
                            nextTop.Panel.Show();
                            nextTop.Panel.SetInteractable(true);
                            nextTop.Panel.FocusDefaultInternal();
                        }
                    }
                    else if (nextTop == null)
                    {
                        //隐藏期无视觉：Backdrop 按最终栈同步复位
                        SetBackdropAlpha(0f);
                        SetBackdropRaycasts(false);
                    }
                }
                finally
                {
                    //当前 Modal 同步关闭：OnClose 即时提交，退场动画播完后自毁并自释放句柄
                    if (modal != null)
                    {
                        modal.Close();
                    }
                }
            }
            finally
            {
                //销毁/Busy 关闭接管整栈时，收尾不再交付本次登记的通知；
                //接管方按同一所有权规则完成清理与通知交付
                if (!IsDisposed)
                {
                    IsBusy = false;

                    //清理完成、Busy 解除后按条目交付通知；
                    //回调异常已由 InvokeSafely 隔离，不会掩盖收尾异常
                    notification?.Invoke();
                }
            }

            return UniTask.FromResult(true);
        }

        //容器显示的呈现流程：按多层展示规则恢复所需层并同步 Backdrop——
        //全部条目执行显示（入场自行 fire-and-forget），仅栈顶层保留交互
        protected override void PresentOnShow()
        {
            if (StackCount == 0)
            {
                //无 Modal：Backdrop 按最终栈复位为隐藏（状态同步，不播放淡出）
                if (backdrop != null)
                {
                    SetBackdropAlpha(0f);
                    SetBackdropRaycasts(false);
                }

                return;
            }

            //Backdrop 同步到当前栈顶之下
            MoveBackdropBelowPanel(GetStackEntry(StackCount - 1).Panel);

            for (int index = 0; index < StackCount; index++)
            {
                Panel layer = GetStackEntry(index).Panel;
                if (layer != null)
                {
                    //隐藏期入栈的层在此执行显示；已显示层由幂等空操作
                    layer.Show();
                }
            }

            //Backdrop 同步：依最终栈恢复淡入（隐藏期跳过淡入时 alpha 可能仍为 0）或保持在场
            if (backdropCanvasGroup.alpha < 1f)
            {
                RunBackdropFadeAsync(1f);
            }
            else
            {
                SetBackdropRaycasts(true);
            }

            //多层展示规则：仅栈顶层可交互，其余层回收输入（Show 已统一启用）
            for (int index = 0; index < StackCount - 1; index++)
            {
                Panel layer = GetStackEntry(index).Panel;
                if (layer != null)
                {
                    layer.SetInteractable(false);
                }
            }

            Panel top = GetStackEntry(StackCount - 1).Panel;
            if (top != null)
            {
                top.FocusDefaultInternal();
            }
        }

        protected override void OnClose()
        {
            //父容器关闭触发本容器 OnClose 且 Busy 时立即接管（不排队）：
            //先标记不可用并取消在途操作，清栈由 ClearModals 与销毁共用所有权规则
            if (IsBusy)
            {
                BeginBusyTakeOver();
            }

            //显式维护 Busy：清栈期间容器不接受新操作，清理完成且 Busy 解除后交付通知，容器本身继续可用
            IsBusy = true;
            List<Action> notifications;
            try
            {
                notifications = ClearModals(immediateDestroy: false);
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

        //共用清栈：从栈顶向下逐条目先解除关系（移出栈）、登记解绑再关闭面板；
        //immediateDestroy 为真时（销毁强杀）显式释放与销毁、不等退场动画。
        //另收集每个条目的无结果通知，由调用方在清理完成后交付
        private List<Action> ClearModals(bool immediateDestroy)
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

                //实例有效性检查：仅存活实例执行生命周期调用与销毁
                if (entry.Panel != null)
                {
                    modal.Close();
                    if (immediateDestroy)
                    {
                        modal.ReleasePrefabHandleImmediate();
                        Destroy(entry.Panel.gameObject);
                    }
                }
            }

            return notifications;
        }

        //面板未经容器自行关闭：条目已抽出、其余条目顺序不变；
        //外部中止按无结果交付；栈顶被关闭时恢复剩余栈顶与 Backdrop
        protected override void HandleSelfClosedPanel(Panel panel, bool wasTop)
        {
            ModalPanel modal = panel as ModalPanel;
            Action notification = modal != null
                ? modal.CaptureResultNotification(false, null)
                : null;
            modal?.Unbind();

            if (StackCount == 0)
            {
                SetBackdropAlpha(0f);
                SetBackdropRaycasts(false);
            }
            else if (wasTop)
            {
                Panel top = GetStackEntry(StackCount - 1).Panel;
                MoveBackdropBelowPanel(top);
                SetBackdropRaycasts(true);
                if (!SuppressVisuals && top != null)
                {
                    top.Show();
                    top.SetInteractable(true);
                    top.FocusDefaultInternal();
                }
            }

            notification?.Invoke();
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

        //Backdrop 淡入淡出保持容器侧视觉，但不再构成任何操作的同步点：
        //fire-and-forget，随容器销毁取消时安静结束
        private async UniTaskVoid RunBackdropFadeAsync(float targetAlpha)
        {
            try
            {
                await FadeBackdropAsync(targetAlpha, LifetimeCancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
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
            //目标Panel可能已销毁的调用边界由实例有效性检查处理，这里保留判空
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
            //销毁路径复用同一清栈方法（强杀口径：立即释放与销毁，不访问即将销毁的 Backdrop）；
            //销毁入口已先标记不可用并取消在途操作，通知在清理完成后交付
            List<Action> notifications = ClearModals(immediateDestroy: true);

            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }
        }
    }
}
