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
    public class PageContainerDestroyTests
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
        public IEnumerator DestroyIdleContainer_DestroysPagesAndReleasesEveryPrefabOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab"));
                RecordingPanelLoader loader = new RecordingPanelLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                LifecyclePage firstPage = await container.PushAsync<LifecyclePage>("first-page");
                LifecyclePage secondPage = await container.PushAsync<LifecyclePage>("second-page");
                List<string> firstEvents = firstPage.Events;
                List<string> secondEvents = secondPage.Events;

                UnityEngine.Object.DestroyImmediate(containerObject);
                await UniTask.Yield();

                Assert.That(firstPage == null, Is.True);
                Assert.That(secondPage == null, Is.True);
                Assert.That(loader.ReleasedPrefabs, Is.EquivalentTo(new[] { firstPrefab, secondPrefab }));
                Assert.That(firstEvents, Does.Contain("OnClose"));
                Assert.That(secondEvents, Does.Contain("OnClose"));
            });
        }

        [UnityTest]
        public IEnumerator DestroyWhilePushIsLoading_CancelsPushAndReleasesExistingPages()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab"));
                DelayedPanelLoader loader = new DelayedPanelLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                LifecyclePage firstPage = await container.PushAsync<LifecyclePage>("first-page");
                UniTask<LifecyclePage> secondPush = container.PushAsync<LifecyclePage>("second-page");
                await UniTask.Yield();

                UnityEngine.Object.DestroyImmediate(containerObject);
                Assert.That(loader.ReceivedToken.IsCancellationRequested, Is.True);
                loader.CompleteSecondPage();

                Exception caught = null;
                try
                {
                    await secondPush;
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                await UniTask.Yield();

                Assert.That(caught, Is.TypeOf<OperationCanceledException>());
                Assert.That(firstPage == null, Is.True);
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { firstPrefab }));
            });
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<LifecyclePage>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class RecordingPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;

            public RecordingPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(key == "first-page" ? firstPrefab : secondPrefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleasedPrefabs.Add(loadedPrefab);
            }
        }

        private sealed class DelayedPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;
            private readonly UniTaskCompletionSource<GameObject> secondCompletionSource =
                new UniTaskCompletionSource<GameObject>();

            public DelayedPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public CancellationToken ReceivedToken { get; private set; }
            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first-page")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                ReceivedToken = cancellationToken;
                return secondCompletionSource.Task.AttachExternalCancellation(cancellationToken);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleasedPrefabs.Add(loadedPrefab);
            }

            public void CompleteSecondPage()
            {
                secondCompletionSource.TrySetResult(secondPrefab);
            }
        }

        private sealed class LifecyclePage : Panel
        {
            public List<string> Events { get; } = new List<string>();

            protected override void OnOpen()
            {
                Events.Add("OnOpen");
            }

            protected override void OnShow()
            {
                Events.Add("OnShow");
            }

            protected override void OnHide()
            {
                Events.Add("OnHide");
            }

            protected override void OnClose()
            {
                Events.Add("OnClose");
            }
        }
    }
}
