using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class PageContainerTypedAndCloseTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [UnityTest]
        public IEnumerator TypedPush_PreparesWithDataBeforeOpen()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject prefab = Track(CreateTypedPrefab("TypedPagePrefab"));
                RecordingLoader loader = new RecordingLoader(prefab);
                container.Initialize(loader);

                TypedPage page = await container.PushAsync<TypedPage, PageData>(
                    "typed-page",
                    new PageData("Page A", 7));

                Assert.That(page, Is.Not.Null);
                Assert.That(page.SourcePage, Is.EqualTo("Page A"));
                Assert.That(page.Counter, Is.EqualTo(7));
                Assert.That(page.Events, Is.EqualTo(new[] { "Prepare", "Open", "Show", "Enter" }));
            });
        }

        [UnityTest]
        public IEnumerator Close_ClosesWholeStackTopFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPrefab"));
                GameObject thirdPrefab = Track(CreatePagePrefab("ThirdPrefab"));
                RecordingLoader loader = new RecordingLoader(firstPrefab, secondPrefab, thirdPrefab);
                container.Initialize(loader);

                ClosePage first = await container.PushAsync<ClosePage>("first");
                ClosePage second = await container.PushAsync<ClosePage>("second");
                ClosePage third = await container.PushAsync<ClosePage>("third");
                List<string> firstEvents = first.Events;
                List<string> secondEvents = second.Events;
                List<string> thirdEvents = third.Events;

                bool result = container.Close();

                Assert.That(result, Is.True);
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.Current, Is.Null);
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { thirdPrefab, secondPrefab, firstPrefab }));
                Assert.That(firstEvents, Does.Not.Contain("Exit"));
                Assert.That(secondEvents, Does.Not.Contain("Exit"));
                Assert.That(thirdEvents, Does.Not.Contain("Exit"));
                Assert.That(firstEvents, Does.Contain("Close"));
                Assert.That(secondEvents, Does.Contain("Close"));
                Assert.That(thirdEvents, Does.Contain("Close"));
                Assert.That(container.Close(), Is.False);
            });
        }

        [UnityTest]
        public IEnumerator Close_WhilePushIsBusyReturnsFalseWithoutChangingStack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject prefab = Track(CreatePagePrefab("PagePrefab"));
                DelayedLoader loader = new DelayedLoader(prefab);
                container.Initialize(loader);

                UniTask<ClosePage> push = container.PushAsync<ClosePage>("page");
                await UniTask.Yield();

                Assert.That(container.Close(), Is.False);
                loader.Complete();
                ClosePage page = await push;

                Assert.That(page, Is.Not.Null);
                Assert.That(container.StackCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator BackAsync_WhilePushIsBusyReturnsFalseWithoutChangingStack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPrefab"));
                DelayedSecondLoader loader = new DelayedSecondLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                await container.PushAsync<ClosePage>("first");
                UniTask<ClosePage> push = container.PushAsync<ClosePage>("second");
                await UniTask.Yield();

                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(container.StackCount, Is.EqualTo(1));

                loader.Complete();
                Assert.That(await push, Is.Not.Null);
                Assert.That(container.StackCount, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator Close_WhenCleanupThrowsContinuesReleasingRemainingPagesAndThrowsFirstException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreateCleanupPrefab("FirstPrefab", false));
                GameObject secondPrefab = Track(CreateCleanupPrefab("SecondPrefab", true));
                GameObject thirdPrefab = Track(CreateCleanupPrefab("ThirdPrefab", false));
                RecordingLoader loader = new RecordingLoader(firstPrefab, secondPrefab, thirdPrefab);
                container.Initialize(loader);

                CleanupPage first = await container.PushAsync<CleanupPage>("first");
                CleanupPage second = await container.PushAsync<CleanupPage>("second");
                CleanupPage third = await container.PushAsync<CleanupPage>("third");

                Exception caught = null;
                try
                {
                    container.Close();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.TypeOf<CleanupFailureException>());
                Assert.That(caught.Message, Is.EqualTo("SecondPrefab(Clone)"));
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { thirdPrefab, secondPrefab, firstPrefab }));
                Assert.That(first.Events, Does.Contain("Close"));
                Assert.That(second.Events, Does.Contain("Close"));
                Assert.That(third.Events, Does.Contain("Close"));
            });
        }

        private GameObject CreateTypedPrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TypedPage>();
            return prefab;
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<ClosePage>();
            return prefab;
        }

        private GameObject CreateCleanupPrefab(string name, bool throwOnClose)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            CleanupPage page = prefab.AddComponent<CleanupPage>();
            page.ThrowOnClose = throwOnClose;
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private readonly struct PageData
        {
            public PageData(string sourcePage, int counter)
            {
                SourcePage = sourcePage;
                Counter = counter;
            }

            public string SourcePage { get; }
            public int Counter { get; }
        }

        private sealed class TypedPage : Panel<PageData>, IPanelTransition
        {
            public List<string> Events { get; } = new List<string>();
            public string SourcePage { get; private set; }
            public int Counter { get; private set; }

            protected override UniTask OnPrepareAsync(PageData data, CancellationToken cancellationToken)
            {
                Events.Add("Prepare");
                SourcePage = data.SourcePage;
                Counter = data.Counter;
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Events.Add("Open");
            }

            protected override void OnShow()
            {
                Events.Add("Show");
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                Events.Add("Enter");
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                return UniTask.CompletedTask;
            }
        }

        private sealed class ClosePage : Panel, IPanelTransition
        {
            public List<string> Events { get; } = new List<string>();

            protected override void OnClose()
            {
                Events.Add("Close");
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                return UniTask.CompletedTask;
            }
        }

        private sealed class CleanupPage : Panel, IPanelTransition
        {
            [SerializeField] private bool throwOnClose;

            public List<string> Events { get; } = new List<string>();
            public bool ThrowOnClose
            {
                get => throwOnClose;
                set => throwOnClose = value;
            }

            protected override void OnClose()
            {
                Events.Add("Close");
                if (ThrowOnClose)
                {
                    throw new CleanupFailureException(gameObject.name);
                }
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class CleanupFailureException : Exception
        {
            public CleanupFailureException(string pageName)
                : base(pageName)
            {
            }
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly GameObject[] prefabs;
            private int index;

            public RecordingLoader(params GameObject[] prefabs)
            {
                this.prefabs = prefabs;
            }

            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[index++]);
            }

            public void Release(GameObject prefab)
            {
                ReleasedPrefabs.Add(prefab);
            }
        }

        private sealed class DelayedLoader : IPanelLoader
        {
            private readonly GameObject prefab;
            private readonly UniTaskCompletionSource<GameObject> completion = new UniTaskCompletionSource<GameObject>();

            public DelayedLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return completion.Task;
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void Complete()
            {
                completion.TrySetResult(prefab);
            }
        }

        private sealed class DelayedSecondLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;
            private readonly UniTaskCompletionSource<GameObject> completion = new UniTaskCompletionSource<GameObject>();
            private int loadCount;

            public DelayedSecondLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                loadCount++;
                return loadCount == 1
                    ? UniTask.FromResult(firstPrefab)
                    : completion.Task;
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void Complete()
            {
                completion.TrySetResult(secondPrefab);
            }
        }
    }
}
