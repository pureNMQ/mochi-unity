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
    public sealed class ModalContainer : PanelContainer
    {
        [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField, Min(0f)] private float backdropFadeDuration = 0.2f;

        private readonly List<ModalStackItem> modals = new List<ModalStackItem>();
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private IPanelLoader loader;
        private ModalBackdrop backdrop;
        private Image backdropImage;
        private CanvasGroup backdropCanvasGroup;
        private int nextHandleVersion;
        private bool isDisposed;

        public int Count => modals.Count;
        public ModalPanel Current => modals.Count == 0 ? null : modals[modals.Count - 1].Panel;
        public bool IsBusy { get; private set; }

        private void Awake()
        {
            EnsureBackdrop();
        }

        public void Initialize(IPanelLoader panelLoader)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(ModalContainer));
            }

            loader = panelLoader ?? throw new ArgumentNullException(nameof(panelLoader));
            EnsureBackdrop();
        }

        public UniTask<TModal> PushAsync<TModal>(string key) where TModal : ModalPanel
        {
            return PushCoreAsync<TModal>(
                key,
                (modal, cancellationToken) => modal.PrepareInternalAsync(cancellationToken),
                null);
        }

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
            if (isDisposed || IsBusy)
            {
                return null;
            }

            if (loader == null)
            {
                throw new InvalidOperationException("ModalContainer must be initialized before pushing a modal.");
            }

            IsBusy = true;
            ModalStackItem previousEntry = modals.Count == 0 ? null : modals[modals.Count - 1];
            GameObject previousFocus = EventSystem.current == null
                ? previousEntry?.Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject;
            GameObject prefab = null;
            TModal modal = null;
            bool modalEnterStarted = false;
            bool committed = false;
            CancellationToken cancellationToken = lifetimeCancellation.Token;

            try
            {
                if (previousEntry != null)
                {
                    previousEntry.SavedFocusTarget = previousFocus;
                    previousEntry.Panel.SetInteractionEnabledInternal(false);
                }

                EnsureBackdrop();
                prefab = await loader.LoadAsync(key, cancellationToken);
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
                    loader.Release(loadedPrefab);
                    return null;
                }

                ModalHandle handle = new ModalHandle(this, ++nextHandleVersion);
                modal.Bind(this, handle);
                bindCallback?.Invoke(modal);
                await prepareAsync(modal, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                modal.OpenInternal();
                modal.ShowInternal();
                modal.SetInteractionEnabledInternal(false);
                MoveBackdropBelowPanel(modal);

                modalEnterStarted = true;
                UniTask enterTask = modal.EnterInternalAsync(cancellationToken);
                UniTask backdropTask = previousEntry == null
                    ? FadeBackdropAsync(1f, cancellationToken)
                    : UniTask.CompletedTask;
                await UniTask.WhenAll(enterTask, backdropTask);
                cancellationToken.ThrowIfCancellationRequested();

                modal.SetInteractionEnabledInternal(true);
                modal.FocusDefaultInternal();
                modals.Add(new ModalStackItem(modal, prefab, handle));
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
                    await TryCleanupAsync(() => modal.ExitInternalAsync(CancellationToken.None));
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
                TryCleanup(() => loader.Release(prefab));
            }

            if (modals.Count == 0)
            {
                SetBackdropAlpha(0f);
                SetBackdropRaycasts(false);
            }
            else
            {
                MoveBackdropBelowPanel(modals[modals.Count - 1].Panel);
                SetBackdropRaycasts(true);
            }
        }

        public UniTask<bool> PopAsync(int count = 1)
        {
            if (isDisposed || IsBusy || count < 1 || count > modals.Count)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(modals.Count - count, false, null);
        }

        public UniTask<bool> PopToAsync(ModalHandle destination)
        {
            if (isDisposed || IsBusy || !ContainsHandle(destination))
            {
                return UniTask.FromResult(false);
            }

            int destinationIndex = FindHandleIndex(destination);
            if (destinationIndex < 0)
            {
                return UniTask.FromResult(false);
            }

            if (destinationIndex == modals.Count - 1)
            {
                return UniTask.FromResult(true);
            }

            return PopEntriesAsync(destinationIndex + 1, false, null);
        }

        public UniTask<bool> CloseAsync()
        {
            if (isDisposed || IsBusy || modals.Count == 0)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(0, false, null);
        }

        internal UniTask<bool> CancelAsync(ModalPanel modal)
        {
            if (modal == null || !IsTop(modal) || isDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(modals.Count - 1, false, null);
        }

        internal UniTask<bool> CompleteAsync<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            if (modal == null || !IsTop(modal) || isDisposed || IsBusy)
            {
                return UniTask.FromResult(false);
            }

            return PopEntriesAsync(modals.Count - 1, true, result);
        }

        internal bool ContainsHandle(ModalHandle handle)
        {
            return !isDisposed && handle.Container == this && FindHandleIndex(handle) >= 0;
        }

        internal void HandleBackdropClick()
        {
            if (isDisposed || IsBusy || modals.Count == 0 || !Current.CloseOnBackdropClick)
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
                MoveBackdropBelowPanel(modals[removeStartIndex - 1].Panel);
            }

            int removeCount = modals.Count - removeStartIndex;
            List<ModalStackItem> removedEntries = new List<ModalStackItem>(removeCount);
            for (int index = modals.Count - 1; index >= removeStartIndex; index--)
            {
                ModalStackItem entry = modals[index];
                entry.Panel.SetInteractionEnabledInternal(false);
                removedEntries.Add(entry);
            }

            GameObject currentFocus = EventSystem.current == null
                ? removedEntries[0].Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? removedEntries[0].Panel.DefaultFocusTarget;
            CancellationToken cancellationToken = lifetimeCancellation.Token;
            Exception transitionException = null;

            try
            {
                UniTask[] exitTasks = new UniTask[removedEntries.Count];
                for (int index = 0; index < removedEntries.Count; index++)
                {
                    exitTasks[index] = removedEntries[index].Panel.ExitInternalAsync(cancellationToken);
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
                if (isDisposed)
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
            foreach (ModalStackItem entry in removedEntries)
            {
                if (entry.Panel.IsVisible || entry.Panel.gameObject.activeSelf)
                {
                    CaptureCleanupException(entry.Panel.HideInternal, ref cleanupException);
                }

                if (entry.Panel.IsOpen)
                {
                    CaptureCleanupException(entry.Panel.CloseInternal, ref cleanupException);
                }

                Action notification = entry.Panel.CaptureResultNotification(hasResult, result);
                entry.Panel.Unbind();
                notifications.Add(notification);
                CaptureCleanupException(() => Destroy(entry.Panel.gameObject), ref cleanupException);

                if (entry.Prefab != null)
                {
                    CaptureCleanupException(() => loader.Release(entry.Prefab), ref cleanupException);
                }
            }

            modals.RemoveRange(removeStartIndex, removeCount);
            if (modals.Count == 0)
            {
                SetBackdropAlpha(0f);
                SetBackdropRaycasts(false);
            }
            else
            {
                MoveBackdropBelowPanel(modals[modals.Count - 1].Panel);
                SetBackdropRaycasts(true);
                ModalStackItem nextTop = modals[modals.Count - 1];
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
            List<ModalStackItem> removedEntries,
            GameObject currentFocus)
        {
            SetBackdropAlpha(1f);
            SetBackdropRaycasts(true);
            MoveBackdropBelowPanel(removedEntries[0].Panel);

            foreach (ModalStackItem entry in removedEntries)
            {
                if (!entry.Panel.gameObject.activeSelf || !entry.Panel.IsVisible)
                {
                    TryCleanup(entry.Panel.ShowInternal);
                }

                await TryCleanupAsync(() => entry.Panel.EnterInternalAsync(CancellationToken.None));
                entry.Panel.SetInteractionEnabledInternal(false);
            }

            ModalStackItem currentEntry = removedEntries[0];
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
            return modals.Count > 0 && ReferenceEquals(Current, modal);
        }

        private int FindHandleIndex(ModalHandle handle)
        {
            if (handle.Container != this)
            {
                return -1;
            }

            for (int index = 0; index < modals.Count; index++)
            {
                if (modals[index].Handle == handle)
                {
                    return index;
                }
            }

            return -1;
        }

        private void OnDestroy()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            lifetimeCancellation.Cancel();
            IsBusy = false;
            List<Action> notifications = new List<Action>(modals.Count);

            for (int index = modals.Count - 1; index >= 0; index--)
            {
                ModalStackItem entry = modals[index];
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
                    notifications.Add(entry.Panel.CaptureResultNotification(false, null));
                    entry.Panel.Unbind();
                    TryCleanup(() => Destroy(entry.Panel.gameObject));
                }

                if (loader != null && entry.Prefab != null)
                {
                    TryCleanup(() => loader.Release(entry.Prefab));
                }
            }

            modals.Clear();
            lifetimeCancellation.Dispose();
            foreach (Action notification in notifications)
            {
                notification?.Invoke();
            }
        }

        private sealed class ModalStackItem
        {
            public ModalStackItem(ModalPanel panel, GameObject prefab, ModalHandle handle)
            {
                Panel = panel;
                Prefab = prefab;
                Handle = handle;
            }

            public ModalPanel Panel { get; }
            public GameObject Prefab { get; }
            public ModalHandle Handle { get; }
            public GameObject SavedFocusTarget { get; set; }
        }

        private static async UniTask TryCleanupAsync(Func<UniTask> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void TryCleanup(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void CaptureCleanupException(Action action, ref Exception firstException)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }
    }
}
