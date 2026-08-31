using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class PageContainerFirstPushTests
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
        public IEnumerator PushFirstPage_OpensActivatesAndShowsPage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject prefab = Track(new GameObject("FirstPagePrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<TestPage>();

                container.Initialize(new FakePanelLoader(prefab));

                TestPage page = await container.PushAsync<TestPage>("first-page");

                Assert.That(page, Is.Not.Null);
                Assert.That(page.gameObject.activeSelf, Is.True);
                Assert.That(page.transform.parent, Is.SameAs(container.transform));
                Assert.That(page.Events, Is.EqualTo(new[] { "OnPrepare", "OnOpen", "OnShow" }));
                Assert.That(container.Current, Is.SameAs(page));
                Assert.That(container.Count, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator PushFirstPage_WhenLoaderReturnsNull_ReturnsNullWithoutChangingStack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();
                FakePanelLoader loader = new FakePanelLoader(null);
                container.Initialize(loader);

                TestPage page = await container.PushAsync<TestPage>("missing-page");

                Assert.That(page, Is.Null);
                Assert.That(container.Current, Is.Null);
                Assert.That(container.Count, Is.Zero);
                Assert.That(loader.ReleaseCount, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator PushFirstPage_WhenPrefabLacksPageComponent_DestroysInstanceAndReleasesPrefab()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject prefab = Track(new GameObject("InvalidPagePrefab"));
                prefab.SetActive(false);
                FakePanelLoader loader = new FakePanelLoader(prefab);
                container.Initialize(loader);

                TestPage page = await container.PushAsync<TestPage>("invalid-page");

                Assert.That(page, Is.Null);
                Assert.That(container.Current, Is.Null);
                Assert.That(container.Count, Is.Zero);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));

                await UniTask.NextFrame();
                Assert.That(container.transform.childCount, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator PushSecondPageWhileFirstIsLoading_ReturnsNullAndReleasesBusy()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject prefab = Track(new GameObject("FirstPagePrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<TestPage>();
                DelayedPanelLoader loader = new DelayedPanelLoader(prefab);
                container.Initialize(loader);

                UniTask<TestPage> firstPush = container.PushAsync<TestPage>("first-page");
                await UniTask.Yield();
                UniTask<TestPage> secondPush = container.PushAsync<TestPage>("second-page");

                AssertBusyIfExposed(container, true);
                loader.Complete();

                TestPage firstPage = await firstPush;
                TestPage secondPage = await secondPush;

                Assert.That(firstPage, Is.Not.Null);
                Assert.That(secondPage, Is.Null);
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                AssertBusyIfExposed(container, false);
            });
        }

        [UnityTest]
        public IEnumerator PushFirstPage_WhenLoaderThrows_ClearsBusy()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();
                container.Initialize(new ThrowingPanelLoader());

                try
                {
                    await container.PushAsync<TestPage>("failed-page");
                    Assert.Fail("Expected the loader failure to propagate.");
                }
                catch (InvalidOperationException)
                {
                }

                AssertBusyIfExposed(container, false);
            });
        }

        [UnityTest]
        public IEnumerator DifferentPageContainers_CanPushThroughSharedLoader()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject firstContainerObject = Track(new GameObject("FirstPageContainer"));
                GameObject secondContainerObject = Track(new GameObject("SecondPageContainer"));
                PageContainer firstContainer = firstContainerObject.AddComponent<PageContainer>();
                PageContainer secondContainer = secondContainerObject.AddComponent<PageContainer>();

                GameObject prefab = Track(new GameObject("SharedPagePrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<TestPage>();
                DelayedPanelLoader loader = new DelayedPanelLoader(prefab);
                firstContainer.Initialize(loader);
                secondContainer.Initialize(loader);

                UniTask<TestPage> firstPush = firstContainer.PushAsync<TestPage>("shared-page");
                UniTask<TestPage> secondPush = secondContainer.PushAsync<TestPage>("shared-page");
                loader.Complete();

                (TestPage firstPage, TestPage secondPage) = await UniTask.WhenAll(firstPush, secondPush);

                Assert.That(firstPage, Is.Not.Null);
                Assert.That(secondPage, Is.Not.Null);
                Assert.That(loader.LoadCount, Is.EqualTo(2));
            });
        }

        private static void AssertBusyIfExposed(PageContainer container, bool expected)
        {
            PropertyInfo property = typeof(PageContainer).GetProperty("IsBusy");
            if (property != null)
            {
                Assert.That(property.GetValue(container), Is.EqualTo(expected));
            }
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class FakePanelLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public FakePanelLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        private sealed class DelayedPanelLoader : IPanelLoader
        {
            private readonly GameObject prefab;
            private readonly UniTaskCompletionSource<GameObject> completionSource =
                new UniTaskCompletionSource<GameObject>();

            public DelayedPanelLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int LoadCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                LoadCount++;
                return completionSource.Task;
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void Complete()
            {
                completionSource.TrySetResult(prefab);
            }
        }

        private sealed class ThrowingPanelLoader : IPanelLoader
        {
            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromException<GameObject>(new InvalidOperationException("Expected test failure."));
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }

        private sealed class TestPage : Panel
        {
            public List<string> Events { get; } = new List<string>();

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                Assert.That(gameObject.activeSelf, Is.False);
                Events.Add("OnPrepare");
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Assert.That(gameObject.activeSelf, Is.False);
                Events.Add("OnOpen");
            }

            protected override void OnShow()
            {
                Assert.That(gameObject.activeSelf, Is.True);
                Events.Add("OnShow");
            }
        }
    }
}
