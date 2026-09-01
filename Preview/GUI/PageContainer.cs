using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public class PageContainer : PanelContainer
    {
        private readonly List<PageStackItem> pages = new List<PageStackItem>();
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private IPanelLoader loader;
        private bool isDisposed;

        public int Count => pages.Count;
        public IPanel Current => pages.Count > 0 ? pages[pages.Count - 1].Page : null;
        public bool CanGoBack => !IsBusy && pages.Count > 1;
        public bool IsBusy { get; private set; }

        public void Initialize(IPanelLoader panelLoader)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(PageContainer));
            }

            loader = panelLoader ?? throw new ArgumentNullException(nameof(panelLoader));
        }

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
            if (isDisposed)
            {
                return null;
            }

            if (loader == null)
            {
                throw new InvalidOperationException("PageContainer must be initialized before pushing a page.");
            }

            if (IsBusy)
            {
                return null;
            }

            IsBusy = true;
            PageStackItem previousEntry = pages.Count > 0 ? pages[pages.Count - 1] : null;
            Panel previousPage = previousEntry?.Page;
            bool previousWasVisible = previousPage != null && previousPage.IsVisible;
            GameObject prefab = null;
            TPage page = null;
            bool pageEntered = false;
            bool committed = false;
            CancellationToken cancellationToken = lifetimeCancellation.Token;

            try
            {
                if (previousEntry != null)
                {
                    previousEntry.SavedFocusTarget = EventSystem.current == null
                        ? null
                        : EventSystem.current.currentSelectedGameObject;
                    previousPage.SetInteractionEnabledInternal(false);
                }

                prefab = await loader.LoadAsync(key, cancellationToken);
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
                    loader.Release(prefab);
                    prefab = null;
                    return null;
                }

                await prepareAsync(page, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                page.OpenInternal();
                page.ShowInternal();
                page.SetInteractionEnabledInternal(false);

                await page.EnterInternalAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                pageEntered = true;

                if (previousPage != null)
                {
                    previousPage.HideInternal();
                }

                page.SetInteractionEnabledInternal(true);
                page.FocusDefaultInternal();
                pages.Add(new PageStackItem(page, prefab));
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
                    await TryCleanupAsync(() => page.ExitInternalAsync(CancellationToken.None));
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
                TryCleanup(() => loader.Release(prefab));
            }
        }

        public async UniTask<bool> BackAsync()
        {
            if (!CanGoBack)
            {
                return false;
            }

            IsBusy = true;
            PageStackItem currentEntry = pages[pages.Count - 1];
            PageStackItem previousEntry = pages[pages.Count - 2];
            GameObject currentFocus = EventSystem.current == null
                ? currentEntry.Page.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? currentEntry.Page.DefaultFocusTarget;
            currentEntry.SavedFocusTarget = currentFocus;
            bool currentPageHidden = false;
            bool currentPageClosed = false;
            CancellationToken cancellationToken = lifetimeCancellation.Token;

            try
            {
                currentEntry.Page.SetInteractionEnabledInternal(false);
                previousEntry.Page.ShowInternal();
                previousEntry.Page.SetInteractionEnabledInternal(false);

                await currentEntry.Page.ExitInternalAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                currentEntry.Page.HideInternal();
                currentPageHidden = true;
                currentEntry.Page.CloseInternal();
                currentPageClosed = true;

                loader.Release(currentEntry.Prefab);
                Destroy(currentEntry.Page.gameObject);
                pages.RemoveAt(pages.Count - 1);

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
                    pages.RemoveAt(pages.Count - 1);
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
            PageStackItem currentEntry,
            PageStackItem previousEntry,
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
            if (isDisposed || IsBusy || pages.Count == 0)
            {
                return false;
            }

            IsBusy = true;
            PageStackItem topEntry = pages[pages.Count - 1];
            GameObject topFocus = EventSystem.current == null
                ? topEntry.Page.DefaultFocusTarget
                : EventSystem.current.currentSelectedGameObject ?? topEntry.Page.DefaultFocusTarget;
            CancellationToken cancellationToken = lifetimeCancellation.Token;

            try
            {
                topEntry.Page.SetInteractionEnabledInternal(false);
                try
                {
                    await topEntry.Page.ExitInternalAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    topEntry.Page.SetInteractionEnabledInternal(true);
                    topEntry.Page.FocusInternal(topFocus);
                    throw;
                }

                Exception firstException = null;
                for (int index = pages.Count - 1; index >= 0; index--)
                {
                    PageStackItem entry = pages[index];
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
                        CaptureCleanupException(() => loader.Release(entry.Prefab), ref firstException);
                    }
                }

                pages.Clear();
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

        private void OnDestroy()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            lifetimeCancellation.Cancel();
            IsBusy = false;

            for (int index = pages.Count - 1; index >= 0; index--)
            {
                PageStackItem entry = pages[index];
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

                if (loader != null && entry.Prefab != null)
                {
                    TryCleanup(() => loader.Release(entry.Prefab));
                }
            }

            pages.Clear();
            lifetimeCancellation.Dispose();
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

        private sealed class PageStackItem
        {
            public PageStackItem(Panel page, GameObject prefab)
            {
                Page = page;
                Prefab = prefab;
            }

            public Panel Page { get; }
            public GameObject Prefab { get; }
            public GameObject SavedFocusTarget { get; set; }
        }
    }
}
