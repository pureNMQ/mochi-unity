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
        public Panel Current => pages.Count > 0 ? pages[pages.Count - 1].Page : null;
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

        public async UniTask<TPage> PushAsync<TPage>(string key) where TPage : Panel
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
            Panel previousPage = null;
            bool restorePreviousInteraction = false;
            PageStackItem previousEntry = null;
            GameObject prefab = null;
            TPage page = null;
            bool previousPageHidden = false;
            bool pageOpened = false;
            bool pageShown = false;
            bool pageEntered = false;
            CancellationToken cancellationToken = lifetimeCancellation.Token;
            try
            {
                previousEntry = pages.Count > 0 ? pages[pages.Count - 1] : null;
                previousPage = previousEntry?.Page;
                restorePreviousInteraction = previousPage != null && previousPage.IsVisible;
                if (previousEntry != null)
                {
                    previousEntry.SavedFocusTarget = EventSystem.current == null
                        ? null
                        : EventSystem.current.currentSelectedGameObject;
                }

                if (previousPage != null)
                {
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

                await page.PrepareInternalAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (previousPage != null)
                {
                    previousPage.HideInternal();
                }
                previousPageHidden = previousPage != null;
                page.OpenInternal();
                pageOpened = true;
                page.ShowInternal();
                pageShown = true;
                page.SetInteractionEnabledInternal(true);
                await page.EnterInternalAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                pageEntered = true;
                page.FocusDefaultInternal();

                pages.Add(new PageStackItem(page, prefab));
                return page;
            }
            catch
            {
                await RollbackPushAsync(
                    previousEntry,
                    previousPage,
                    prefab,
                    page,
                    previousPageHidden,
                    pageOpened,
                    pageShown,
                    pageEntered);
                throw;
            }
            finally
            {
                if (restorePreviousInteraction && previousPage != null)
                {
                    previousPage.SetInteractionEnabledInternal(true);
                }

                IsBusy = false;
            }
        }

        private async UniTask RollbackPushAsync(
            PageStackItem previousEntry,
            Panel previousPage,
            GameObject prefab,
            Panel page,
            bool previousPageHidden,
            bool pageOpened,
            bool pageShown,
            bool pageEntered)
        {
            if (page != null)
            {
                if (pageEntered)
                {
                    await TryRollbackExitAsync(page);
                }

                if (pageShown)
                {
                    TryRollback(() => page.HideInternal());
                }

                if (pageOpened)
                {
                    TryRollback(() => page.CloseInternal());
                }

                TryRollback(() => Destroy(page.gameObject));
            }

            if (prefab != null)
            {
                TryRollback(() => loader.Release(prefab));
            }

            if (previousPageHidden && previousPage != null)
            {
                TryRollback(() => previousPage.ShowInternal());
                previousPage.SetInteractionEnabledInternal(true);
                previousPage.FocusInternal(previousEntry?.SavedFocusTarget ?? previousPage.DefaultFocusTarget);
            }
        }

        private static async UniTask TryRollbackExitAsync(Panel page)
        {
            try
            {
                await page.ExitInternalAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void TryRollback(Action action)
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
            bool previousPageShown = false;
            bool currentPageHidden = false;
            CancellationToken cancellationToken = lifetimeCancellation.Token;
            try
            {
                currentEntry.Page.SetInteractionEnabledInternal(false);
                previousEntry.Page.ShowInternal();
                previousPageShown = true;
                previousEntry.Page.SetInteractionEnabledInternal(false);

                await currentEntry.Page.ExitInternalAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                currentEntry.Page.HideInternal();
                currentPageHidden = true;
                currentEntry.Page.CloseInternal();

                loader.Release(currentEntry.Prefab);
                Destroy(currentEntry.Page.gameObject);
                pages.RemoveAt(pages.Count - 1);

                previousEntry.Page.SetInteractionEnabledInternal(true);
                previousEntry.Page.FocusInternal(previousEntry.SavedFocusTarget ?? previousEntry.Page.DefaultFocusTarget);
                return true;
            }
            catch
            {
                await RollbackBackAsync(
                    currentEntry,
                    previousEntry,
                    currentFocus,
                    previousPageShown,
                    currentPageHidden);
                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async UniTask RollbackBackAsync(
            PageStackItem currentEntry,
            PageStackItem previousEntry,
            GameObject currentFocus,
            bool previousPageShown,
            bool currentPageHidden)
        {
            if (isDisposed || currentEntry == null || previousEntry == null || currentEntry.Page == null || previousEntry.Page == null)
            {
                return;
            }

            if (previousPageShown)
            {
                TryRollback(() => previousEntry.Page.HideInternal());
            }
            else
            {
                previousEntry.Page.RestoreHiddenInternal();
            }

            previousEntry.Page.RestoreHiddenInternal();
            previousEntry.Page.SetInteractionEnabledInternal(false);

            if (currentPageHidden && !currentEntry.Page.IsVisible)
            {
                TryRollback(() => currentEntry.Page.ShowInternal());
            }

            currentEntry.Page.SetInteractionEnabledInternal(true);
            currentEntry.Page.FocusInternal(currentFocus ?? currentEntry.Page.DefaultFocusTarget);
            await UniTask.CompletedTask;
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
                        TryRollback(() => entry.Page.HideInternal());
                    }

                    if (entry.Page.IsOpen)
                    {
                        TryRollback(() => entry.Page.CloseInternal());
                    }

                    TryRollback(() => Destroy(entry.Page.gameObject));
                }

                if (loader != null && entry.Prefab != null)
                {
                    TryRollback(() => loader.Release(entry.Prefab));
                }
            }

            pages.Clear();
            lifetimeCancellation.Dispose();
        }

        private sealed class PageStackItem
        {
            public readonly Panel Page;
            public readonly GameObject Prefab;
            public GameObject SavedFocusTarget;

            public PageStackItem(Panel page, GameObject prefab)
            {
                Page = page;
                Prefab = prefab;
            }
        }
    }
}
