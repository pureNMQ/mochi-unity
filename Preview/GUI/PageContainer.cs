using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                currentEntry.Panel.SetInteractionEnabledInternal(false);
                previousEntry.Panel.ShowInternal();
                previousEntry.Panel.SetInteractionEnabledInternal(false);

                await RunExitTransitionAsync(currentEntry.Panel, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                currentEntry.Panel.HideInternal();
                currentEntry.Panel.CloseInternal();

                Loader.Release(currentEntry.Prefab);
                Destroy(currentEntry.Panel.gameObject);
                RemoveStackEntryAt(StackCount - 1);

                previousEntry.Panel.SetInteractionEnabledInternal(true);
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public bool Close()
        {
            if (IsDisposed || IsBusy || StackCount == 0)
            {
                return false;
            }

            IsBusy = true;
            try
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
