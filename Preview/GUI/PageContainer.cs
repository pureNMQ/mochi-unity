using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public class PageContainer : StackContainer
    {
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
            Panel previousPage = previousEntry?.Panel;
            bool previousWasVisible = previousPage != null && previousPage.IsVisible;
            GameObject prefab = null;
            TPage page = null;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                if (previousEntry != null)
                {
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

                if (previousPage != null)
                {
                    previousPage.HideInternal();
                }

                page.SetInteractionEnabledInternal(true);
                page.FocusDefaultInternal();
                AddStackEntry(page, prefab);
                return page;
            }
            catch
            {
                await RollbackPushAsync(prefab, page);
                if (previousPage != null)
                {
                    if (previousWasVisible)
                    {
                        previousPage.ShowInternal();
                    }

                    previousPage.SetInteractionEnabledInternal(previousWasVisible);
                }

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async UniTask RollbackPushAsync(GameObject prefab, Panel page)
        {
            if (page != null)
            {
                if (page.IsVisible)
                {
                    page.HideInternal();
                }

                if (page.IsOpen)
                {
                    page.CloseInternal();
                }

                Destroy(page.gameObject);
            }

            if (prefab != null)
            {
                Loader.Release(prefab);
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
                ? currentEntry.Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? currentEntry.Panel.DefaultFocusTarget;
            bool currentPageHidden = false;
            bool currentPageClosed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                currentEntry.Panel.SetInteractionEnabledInternal(false);
                previousEntry.Panel.ShowInternal();
                previousEntry.Panel.SetInteractionEnabledInternal(false);

                await RunExitTransitionAsync(currentEntry.Panel, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                currentEntry.Panel.HideInternal();
                currentPageHidden = true;
                currentEntry.Panel.CloseInternal();
                currentPageClosed = true;

                Loader.Release(currentEntry.Prefab);
                Destroy(currentEntry.Panel.gameObject);
                RemoveStackEntryAt(StackCount - 1);

                previousEntry.Panel.SetInteractionEnabledInternal(true);
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
                    Destroy(currentEntry.Panel.gameObject);
                    previousEntry.Panel.SetInteractionEnabledInternal(true);
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
            if (previousEntry.Panel.IsVisible)
            {
                previousEntry.Panel.HideInternal();
            }
            else
            {
                previousEntry.Panel.RestoreHiddenInternal();
            }

            previousEntry.Panel.SetInteractionEnabledInternal(false);

            if (currentPageHidden || !currentEntry.Panel.IsVisible)
            {
                currentEntry.Panel.ShowInternal();
            }
            else
            {
                currentEntry.Panel.FocusInternal(currentFocus ?? currentEntry.Panel.DefaultFocusTarget);
            }

            currentEntry.Panel.SetInteractionEnabledInternal(true);
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
                ? topEntry.Panel.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? topEntry.Panel.DefaultFocusTarget;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                topEntry.Panel.SetInteractionEnabledInternal(false);
                try
                {
                    await RunExitTransitionAsync(topEntry.Panel, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    topEntry.Panel.SetInteractionEnabledInternal(true);
                    topEntry.Panel.FocusInternal(topFocus);
                    throw;
                }

                for (int index = StackCount - 1; index >= 0; index--)
                {
                    StackEntry entry = GetStackEntry(index);
                    if (entry.Panel != null)
                    {
                        if (entry.Panel.IsVisible)
                        {
                            entry.Panel.HideInternal();
                        }

                        if (entry.Panel.IsOpen)
                        {
                            entry.Panel.CloseInternal();
                        }

                        Destroy(entry.Panel.gameObject);
                    }

                    if (entry.Prefab != null)
                    {
                        Loader.Release(entry.Prefab);
                    }
                }

                ClearStackEntries();
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
                if (entry.Panel != null)
                {
                    if (entry.Panel.IsVisible)
                    {
                        entry.Panel.HideInternal();
                    }

                    if (entry.Panel.IsOpen)
                    {
                        entry.Panel.CloseInternal();
                    }

                    Destroy(entry.Panel.gameObject);
                }

                if (Loader != null && entry.Prefab != null)
                {
                    Loader.Release(entry.Prefab);
                }
            }
        }
    }
}
