using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
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

        public int Count => StackCount;
        public ModalPanel Current => StackCount == 0 ? null : (ModalPanel)GetStackEntry(StackCount - 1).Panel;

        private void Awake()
        {
            EnsureBackdrop();
        }

        public override void Initialize(IPanelLoader panelLoader)
        {
            base.Initialize(panelLoader);
            EnsureBackdrop();
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
            //获取当前焦点对象，确保在模态面板关闭后可以恢复焦点
            StackEntry previousEntry = StackCount == 0 ? null : GetStackEntry(StackCount - 1);
            GameObject previousFocus = EventSystem.current == null
                ? previousEntry?.Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject;


            GameObject prefab = null;
            TModal modal = null;
            bool modalEnterStarted = false;
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                //保存焦点对象，并禁用前一个模态面板的交互
                if (previousEntry != null)
                {
                    previousEntry.SavedFocusTarget = previousFocus;
                    previousEntry.Panel.SetInteractionEnabledInternal(false);
                }

                EnsureBackdrop();
                //加载预制体资源
                prefab = await Loader.LoadAsync(key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (prefab == null)
                {
                    return null;
                }

                GameObject instance = Instantiate(prefab, transform, false);
                instance.SetActive(false);

                modal = instance.GetComponent<TModal>();
                if (modal == null)
                {
                    Destroy(instance);
                    GameObject loadedPrefab = prefab;
                    prefab = null;
                    Loader.Release(loadedPrefab);
                    return null;
                }

                modal.Bind(this);
                //给modal绑定回调函数
                bindCallback?.Invoke(modal);
                await prepareAsync(modal, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                //执行生命周期函数
                modal.OpenInternal();
                modal.ShowInternal();
                modal.SetInteractionEnabledInternal(false);

                MoveBackdropBelowPanel(modal);

                //backdrop动画和panel进入动画同时执行
                modalEnterStarted = true;
                UniTask enterTask = RunEnterTransitionAsync(modal, cancellationToken);
                UniTask backdropTask = previousEntry == null
                    ? FadeBackdropAsync(1f, cancellationToken)
                    : UniTask.CompletedTask;
                await UniTask.WhenAll(enterTask, backdropTask);
                cancellationToken.ThrowIfCancellationRequested();

                modal.SetInteractionEnabledInternal(true);
                modal.FocusDefaultInternal();
                AddStackEntry(modal, prefab);
                committed = true;
                return modal;
            }
            catch
            {
                await RollbackPushAsync(prefab, modal, modalEnterStarted);
                throw;
            }
            finally
            {
                if (!committed && previousEntry != null)
                {
                    previousEntry.Panel.SetInteractionEnabledInternal(true);
                    previousEntry.Panel.FocusInternal(
                        previousEntry.SavedFocusTarget ?? previousEntry.Panel.DefaultFocusTarget);
                }

                IsBusy = false;
            }
        }

        private async UniTask RollbackPushAsync(
            GameObject prefab,
            ModalPanel modal,
            bool modalEnterStarted)
        {
            if (modal != null)
            {
                if (modalEnterStarted)
                {
                    await TryCleanupAsync(() => RunExitTransitionAsync(modal, CancellationToken.None));
                }

                if (modal.IsVisible || modal.gameObject.activeSelf)
                {
                    TryCleanup(modal.HideInternal);
                }

                if (modal.IsOpen)
                {
                    TryCleanup(modal.CloseInternal);
                }

                modal.Unbind();
                TryCleanup(() => Destroy(modal.gameObject));
            }

            if (prefab != null)
            {
                TryCleanup(() => Loader.Release(prefab));
            }

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

        public UniTask<bool> PopAsync(int count = 1)
        {
            if (IsDisposed || IsBusy || count < 1 || count > StackCount)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(StackCount - count, false, null);
        }

        public UniTask<bool> CloseAsync()
        {
            if (IsDisposed || IsBusy || StackCount == 0)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(0, false, null);
        }

        internal UniTask<bool> CancelAsync(ModalPanel modal)
        {
            if (modal == null || !IsTop(modal) || IsDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(StackCount - 1, false, null);
        }

        internal UniTask<bool> CompleteAsync<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            if (modal == null || !IsTop(modal) || IsDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(StackCount - 1, true, result);
        }

        internal void HandleBackdropClick()
        {
            if (IsDisposed || IsBusy || StackCount == 0 || !Current.CloseOnBackdropClick)
            {
                return;
            }

            CancelAsync(Current).Forget();
        }

        private async UniTask<bool> PopEntriesAsync(
            int removeStartIndex,
            bool hasResult,
            object result)
        {
            IsBusy = true;
            if (removeStartIndex > 0)
            {
                MoveBackdropBelowPanel(GetStackEntry(removeStartIndex - 1).Panel);
            }

            int removeCount = StackCount - removeStartIndex;
            List<StackEntry> removedEntries = new List<StackEntry>(removeCount);
            for (int index = StackCount - 1; index >= removeStartIndex; index--)
            {
                StackEntry entry = GetStackEntry(index);
                entry.Panel.SetInteractionEnabledInternal(false);
                removedEntries.Add(entry);
            }

            GameObject currentFocus = EventSystem.current == null
                ? removedEntries[0].Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? removedEntries[0].Panel.DefaultFocusTarget;
            CancellationToken cancellationToken = LifetimeCancellationToken;
            Exception transitionException = null;

            try
            {
                UniTask[] exitTasks = new UniTask[removedEntries.Count];
                for (int index = 0; index < removedEntries.Count; index++)
                {
                    exitTasks[index] = RunExitTransitionAsync(removedEntries[index].Panel, cancellationToken);
                }

                UniTask[] transitionTasks = new UniTask[exitTasks.Length + 1];
                Array.Copy(exitTasks, transitionTasks, exitTasks.Length);
                transitionTasks[transitionTasks.Length - 1] = removeStartIndex == 0
                    ? FadeBackdropAsync(0f, cancellationToken)
                    : UniTask.CompletedTask;
                await UniTask.WhenAll(transitionTasks);
            }
            catch (Exception exception)
            {
                transitionException = exception;
            }

            if (transitionException != null)
            {
                if (IsDisposed)
                {
                    IsBusy = false;
                    return false;
                }

                await RollbackPopAsync(removedEntries, currentFocus);
                IsBusy = false;
                ExceptionDispatchInfo.Capture(transitionException).Throw();
            }

            Exception cleanupException = null;
            List<Action> notifications = new List<Action>(removedEntries.Count);
            foreach (StackEntry entry in removedEntries)
            {
                if (entry.Panel.IsVisible || entry.Panel.gameObject.activeSelf)
                {
                    CaptureCleanupException(entry.Panel.HideInternal, ref cleanupException);
                }

                if (entry.Panel.IsOpen)
                {
                    CaptureCleanupException(entry.Panel.CloseInternal, ref cleanupException);
                }

                Action notification = ((ModalPanel)entry.Panel).CaptureResultNotification(hasResult, result);
                ((ModalPanel)entry.Panel).Unbind();
                notifications.Add(notification);
                CaptureCleanupException(() => Destroy(entry.Panel.gameObject), ref cleanupException);

                if (entry.Prefab != null)
                {
                    CaptureCleanupException(() => Loader.Release(entry.Prefab), ref cleanupException);
                }
            }

            RemoveStackEntries(removeStartIndex, removeCount);
            if (StackCount == 0)
            {
                SetBackdropAlpha(0f);
                SetBackdropRaycasts(false);
            }
            else
            {
                MoveBackdropBelowPanel(GetStackEntry(StackCount - 1).Panel);
                SetBackdropRaycasts(true);
                StackEntry nextTop = GetStackEntry(StackCount - 1);
                nextTop.Panel.SetInteractionEnabledInternal(true);
                nextTop.Panel.FocusInternal(nextTop.SavedFocusTarget ?? nextTop.Panel.DefaultFocusTarget);
            }

            IsBusy = false;
            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }

            if (cleanupException != null)
            {
                ExceptionDispatchInfo.Capture(cleanupException).Throw();
            }

            return true;
        }

        private async UniTask RollbackPopAsync(
            List<StackEntry> removedEntries,
            GameObject currentFocus)
        {
            SetBackdropAlpha(1f);
            SetBackdropRaycasts(true);
            MoveBackdropBelowPanel(removedEntries[0].Panel);

            foreach (StackEntry entry in removedEntries)
            {
                if (!entry.Panel.gameObject.activeSelf || !entry.Panel.IsVisible)
                {
                    TryCleanup(entry.Panel.ShowInternal);
                }

                await TryCleanupAsync(() => RunEnterTransitionAsync(entry.Panel, CancellationToken.None));
                entry.Panel.SetInteractionEnabledInternal(false);
            }

            StackEntry currentEntry = removedEntries[0];
            currentEntry.Panel.SetInteractionEnabledInternal(true);
            currentEntry.Panel.FocusInternal(currentFocus ?? currentEntry.Panel.DefaultFocusTarget);
        }

        private async UniTask FadeBackdropAsync(float targetAlpha, CancellationToken cancellationToken)
        {
            EnsureBackdrop();
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

        //保证Backdrop存在
        private void EnsureBackdrop()
        {
            if (backdrop != null)
            {
                return;
            }

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
            if (backdrop == null || panel == null)
            {
                return;
            }

            int panelIndex = panel.transform.GetSiblingIndex();
            backdrop.transform.SetSiblingIndex(Mathf.Max(0, panelIndex - 1));
        }

        private void SetBackdropAlpha(float alpha)
        {
            if (backdropCanvasGroup != null)
            {
                backdropCanvasGroup.alpha = Mathf.Clamp01(alpha);
            }
        }

        private void SetBackdropRaycasts(bool enabled)
        {
            if (backdropCanvasGroup != null)
            {
                backdropCanvasGroup.blocksRaycasts = enabled;
                backdropCanvasGroup.interactable = enabled;
            }
        }

        private bool IsTop(ModalPanel modal)
        {
            return StackCount > 0 && ReferenceEquals(Current, modal);
        }

        protected override void CleanupStackOnDestroy()
        {
            List<Action> notifications = new List<Action>(StackCount);

            for (int index = StackCount - 1; index >= 0; index--)
            {
                StackEntry entry = GetStackEntry(index);
                if (entry.Panel != null)
                {
                    if (entry.Panel.IsVisible || entry.Panel.gameObject.activeSelf)
                    {
                        TryCleanup(entry.Panel.HideInternal);
                    }

                    if (entry.Panel.IsOpen)
                    {
                        TryCleanup(entry.Panel.CloseInternal);
                    }
                    notifications.Add(((ModalPanel)entry.Panel).CaptureResultNotification(false, null));
                    ((ModalPanel)entry.Panel).Unbind();
                    TryCleanup(() => Destroy(entry.Panel.gameObject));
                }

                if (Loader != null && entry.Prefab != null)
                {
                    TryCleanup(() => Loader.Release(entry.Prefab));
                }
            }

            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }
        }
    }
}
