using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public class PageContainer : StackContainer
    {
        public int Count => StackCount;
        public IPanel Current => StackCount > 0 ? GetStackEntry(StackCount - 1).Panel : null;
        public bool CanGoBack => !IsBusy && StackCount > 1;

        public UniTask<TPage> PushAsync<TPage>(string key) where TPage : Panel
        {
            return PushCoreAsync<TPage>(key, (page, token) => page.PrepareInternalAsync(token));
        }

        public UniTask<TPage> PushAsync<TPage, TData>(string key, TData data)
            where TPage : Panel<TData>
        {
            return PushCoreAsync<TPage>(key, (page, token) => page.PrepareInternalAsync(data, token));
        }

        private async UniTask<TPage> PushCoreAsync<TPage>(
            string key,
            Func<TPage, CancellationToken, UniTask> prepareAsync)
            where TPage : Panel
        {
            if (IsDisposed)
            {
                return null;
            }

            if (Loader == null)
            {
                throw new InvalidOperationException("PageContainer must be initialized before pushing a page.");
            }

            if (IsBusy)
            {
                return null;
            }

            IsBusy = true;
            StackEntry previousEntry = StackCount > 0 ? GetStackEntry(StackCount - 1) : null;
            Panel previousPage = previousEntry?.Page;
            bool previousWasVisible = previousPage != null && previousPage.IsVisible;
            GameObject prefab = null;
            TPage page = null;
            bool pageEntered = false;
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                if (previousEntry != null)
                {
                    previousEntry.SavedFocusTarget = EventSystem.current == null
                        ? null
                        : EventSystem.current.currentSelectedGameObject;
                    previousPage.SetInteractionEnabledInternal(false);
                }

                prefab = await Loader.LoadAsync(key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (prefab == null)
                {
                    return null;
                }

                GameObject instance = Instantiate(prefab, transform, false);
                instance.SetActive(false);
                page = instance.GetComponent<TPage>();
                if (page == null)
                {
                    Destroy(instance);
                    GameObject loadedPrefab = prefab;
                    prefab = null;
                    Loader.Release(loadedPrefab);
                    return null;
                }

                await prepareAsync(page, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                page.OpenInternal();
                page.ShowInternal();
                page.SetInteractionEnabledInternal(false);

                await RunEnterTransitionAsync(page, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                pageEntered = true;

                if (previousPage != null)
                {
                    previousPage.HideInternal();
                }

                page.SetInteractionEnabledInternal(true);
                page.FocusDefaultInternal();
                AddStackEntry(page, prefab);
                committed = true;
                return page;
            }
            catch
            {
                await RollbackPushAsync(prefab, page, pageEntered);
                throw;
            }
            finally
            {
                if (!committed && previousPage != null)
                {
                    if (previousWasVisible && !previousPage.IsVisible)
                    {
                        TryCleanup(previousPage.ShowInternal);
                    }

                    previousPage.SetInteractionEnabledInternal(previousWasVisible);
                    if (previousWasVisible)
                    {
                        previousPage.FocusInternal(previousEntry.SavedFocusTarget ?? previousPage.DefaultFocusTarget);
                    }
                }

                IsBusy = false;
            }
        }

        private async UniTask RollbackPushAsync(GameObject prefab, Panel page, bool pageEntered)
        {
            if (page != null)
            {
                if (pageEntered)
                {
                    await TryCleanupAsync(() => RunExitTransitionAsync(page, CancellationToken.None));
                }

                if (page.IsVisible)
                {
                    TryCleanup(page.HideInternal);
                }

                if (page.IsOpen)
                {
                    TryCleanup(page.CloseInternal);
                }

                TryCleanup(() => Destroy(page.gameObject));
            }

            if (prefab != null)
            {
                TryCleanup(() => Loader.Release(prefab));
            }
        }

        public async UniTask<bool> BackAsync()
        {
            if (!CanGoBack)
            {
                return false;
            }

            IsBusy = true;
            StackEntry currentEntry = GetStackEntry(StackCount - 1);
            StackEntry previousEntry = GetStackEntry(StackCount - 2);
            GameObject currentFocus = EventSystem.current == null
                ? currentEntry.Page.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? currentEntry.Page.DefaultFocusTarget;
            currentEntry.SavedFocusTarget = currentFocus;
            bool currentPageHidden = false;
            bool currentPageClosed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                currentEntry.Page.SetInteractionEnabledInternal(false);
                previousEntry.Page.ShowInternal();
                previousEntry.Page.SetInteractionEnabledInternal(false);

                await RunExitTransitionAsync(currentEntry.Page, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                currentEntry.Page.HideInternal();
                currentPageHidden = true;
                currentEntry.Page.CloseInternal();
                currentPageClosed = true;

                Loader.Release(currentEntry.Prefab);
                Destroy(currentEntry.Page.gameObject);
                RemoveStackEntryAt(StackCount - 1);

                previousEntry.Page.SetInteractionEnabledInternal(true);
                previousEntry.Page.FocusInternal(previousEntry.SavedFocusTarget ?? previousEntry.Page.DefaultFocusTarget);
                return true;
            }
            catch
            {
                if (!currentPageClosed)
                {
                    RollbackBack(currentEntry, previousEntry, currentFocus, currentPageHidden);
                }
                else
                {
                    RemoveStackEntryAt(StackCount - 1);
                    TryCleanup(() => Destroy(currentEntry.Page.gameObject));
                    previousEntry.Page.SetInteractionEnabledInternal(true);
                    previousEntry.Page.FocusInternal(previousEntry.SavedFocusTarget ?? previousEntry.Page.DefaultFocusTarget);
                }

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void RollbackBack(
            StackEntry currentEntry,
            StackEntry previousEntry,
            GameObject currentFocus,
            bool currentPageHidden)
        {
            if (previousEntry.Page.IsVisible)
            {
                TryCleanup(previousEntry.Page.HideInternal);
            }
            else
            {
                previousEntry.Page.RestoreHiddenInternal();
            }

            previousEntry.Page.SetInteractionEnabledInternal(false);

            if (currentPageHidden || !currentEntry.Page.IsVisible)
            {
                TryCleanup(currentEntry.Page.ShowInternal);
            }

            currentEntry.Page.SetInteractionEnabledInternal(true);
            currentEntry.Page.FocusInternal(currentFocus ?? currentEntry.Page.DefaultFocusTarget);
        }

        public async UniTask<bool> CloseAsync()
        {
            if (IsDisposed || IsBusy || StackCount == 0)
            {
                return false;
            }

            IsBusy = true;
            StackEntry topEntry = GetStackEntry(StackCount - 1);
            GameObject topFocus = EventSystem.current == null
                ? topEntry.Page.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? topEntry.Page.DefaultFocusTarget;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                topEntry.Page.SetInteractionEnabledInternal(false);
                try
                {
                    await RunExitTransitionAsync(topEntry.Page, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    topEntry.Page.SetInteractionEnabledInternal(true);
                    topEntry.Page.FocusInternal(topFocus);
                    throw;
                }

                Exception firstException = null;
                for (int index = StackCount - 1; index >= 0; index--)
                {
                    StackEntry entry = GetStackEntry(index);
                    if (entry.Page != null)
                    {
                        if (entry.Page.IsVisible)
                        {
                            CaptureCleanupException(entry.Page.HideInternal, ref firstException);
                        }

                        if (entry.Page.IsOpen)
                        {
                            CaptureCleanupException(entry.Page.CloseInternal, ref firstException);
                        }

                        CaptureCleanupException(() => Destroy(entry.Page.gameObject), ref firstException);
                    }

                    if (entry.Prefab != null)
                    {
                        CaptureCleanupException(() => Loader.Release(entry.Prefab), ref firstException);
                    }
                }

                ClearStackEntries();
                if (firstException != null)
                {
                    throw firstException;
                }

                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected override void CleanupStackOnDestroy()
        {
            for (int index = StackCount - 1; index >= 0; index--)
            {
                StackEntry entry = GetStackEntry(index);
                if (entry.Page != null)
                {
                    if (entry.Page.IsVisible)
                    {
                        TryCleanup(entry.Page.HideInternal);
                    }

                    if (entry.Page.IsOpen)
                    {
                        TryCleanup(entry.Page.CloseInternal);
                    }

                    TryCleanup(() => Destroy(entry.Page.gameObject));
                }

                if (Loader != null && entry.Prefab != null)
                {
                    TryCleanup(() => Loader.Release(entry.Prefab));
                }
            }
        }
    }
}
