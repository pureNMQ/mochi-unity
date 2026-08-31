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
    public class PageContainerHistoryTests
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
        public IEnumerator PushSecondPage_HidesPreviousPageAfterLoadAndKeepsItsState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(new GameObject("FirstPagePrefab"));
                firstPrefab.SetActive(false);
                firstPrefab.AddComponent<HistoryPage>();

                GameObject secondPrefab = Track(new GameObject("SecondPagePrefab"));
                secondPrefab.SetActive(false);
                secondPrefab.AddComponent<HistoryPage>();

                KeyedPanelLoader loader = new KeyedPanelLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                HistoryPage firstPage = await container.PushAsync<HistoryPage>("first-page");
                UniTask<HistoryPage> secondPush = container.PushAsync<HistoryPage>("second-page");

                await UniTask.Yield();

                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.State, Is.EqualTo(1));
                Assert.That(firstPage.Events, Is.EqualTo(new[] { "OnPrepare", "OnOpen", "OnShow" }));

                loader.CompleteSecondPage();
                HistoryPage secondPage = await secondPush;

                Assert.That(secondPage, Is.Not.Null);
                Assert.That(container.Current, Is.SameAs(secondPage));
                Assert.That(container.Count, Is.EqualTo(2));
                Assert.That(firstPage, Is.Not.Null);
                Assert.That(firstPage.State, Is.EqualTo(1));
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(firstPage.gameObject.activeSelf, Is.False);
                Assert.That(firstPage.Events, Is.EqualTo(new[] { "OnPrepare", "OnOpen", "OnShow", "OnHide" }));
                Assert.That(secondPage.Events, Is.EqualTo(new[] { "OnPrepare", "OnOpen", "OnShow" }));
            });
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class KeyedPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;
            private readonly UniTaskCompletionSource<GameObject> secondCompletionSource =
                new UniTaskCompletionSource<GameObject>();

            public KeyedPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first-page")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "second-page")
                {
                    return secondCompletionSource.Task;
                }

                throw new ArgumentException($"Unexpected test key: {key}");
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void CompleteSecondPage()
            {
                secondCompletionSource.TrySetResult(secondPrefab);
            }
        }

        private sealed class HistoryPage : Panel
        {
            public List<string> Events { get; } = new List<string>();
            public int State { get; private set; }

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                Assert.That(gameObject.activeSelf, Is.False);
                Events.Add("OnPrepare");
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Assert.That(gameObject.activeSelf, Is.False);
                State++;
                Events.Add("OnOpen");
            }

            protected override void OnShow()
            {
                Assert.That(gameObject.activeSelf, Is.True);
                Events.Add("OnShow");
            }

            protected override void OnHide()
            {
                Assert.That(gameObject.activeSelf, Is.True);
                Events.Add("OnHide");
            }
        }
    }
}
