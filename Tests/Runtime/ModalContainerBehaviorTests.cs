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
    public class ModalContainerBehaviorTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [Test]
        public void PublicApi_DoesNotExposeModalHandleOrPopTo()
        {
            Assert.That(typeof(ModalPanel).GetProperty("Handle"), Is.Null);
            Assert.That(typeof(ModalContainer).GetMethod("PopToAsync"), Is.Null);
            Assert.That(
                typeof(ModalContainer).Assembly.GetType("Mochi.Unity.Preview.GUI.ModalHandle"),
                Is.Null);
        }

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
        public IEnumerator TypedModal_InvokesPushCallbackAndEventWithResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out GameObject prefab);
                List<string> notifications = new List<string>();
                TestModal modal = await container.PushAsync<TestModal, ModalData, string>(
                    "typed-modal",
                    new ModalData("source"),
                    new ModalResultCallback<string>((hasResult, result) =>
                    {
                        notifications.Add($"push:{hasResult}:{result}");
                    }));
                modal.ResultReturned += (hasResult, result) =>
                    notifications.Add($"event:{hasResult}:{result}");

                Assert.That(modal.Source, Is.EqualTo("source"));

                Assert.That(await modal.CompleteResultAsync("accepted"), Is.True);

                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "push:True:accepted",
                    "event:True:accepted"
                }));
                Assert.That(container.Count, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator NestedModals_KeepLowerVisibleAndPopKeepsDestination()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out GameObject prefab);
                int cancellationCount = 0;
                TestModal first = await container.PushAsync<TestModal, ModalData, string>(
                    "first",
                    new ModalData("first"),
                    new ModalResultCallback<string>((hasResult, result) =>
                    {
                        if (!hasResult)
                        {
                            cancellationCount++;
                        }
                    }));
                TestModal second = await container.PushAsync<TestModal, ModalData, string>(
                    "second",
                    new ModalData("second"));

                Assert.That(container.Current, Is.SameAs(second));
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.False);
                Assert.That(first.CanvasGroup.blocksRaycasts, Is.False);
                Assert.That(second.CanvasGroup.interactable, Is.True);
                Assert.That(second.CanvasGroup.blocksRaycasts, Is.True);
                Assert.That(await container.PopAsync(), Is.True);

                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(container.Count, Is.EqualTo(1));
                Assert.That(cancellationCount, Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator Pop_InvokesCancellationWithDefaultAndInvalidCountsAreRejected()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out GameObject prefab);
                bool hasResult = true;
                string result = "unexpected";
                TestModal modal = await container.PushAsync<TestModal, ModalData, string>(
                    "modal",
                    new ModalData("modal"),
                    new ModalResultCallback<string>((valid, value) =>
                    {
                        hasResult = valid;
                        result = value;
                    }));

                Assert.That(await container.PopAsync(0), Is.False);
                Assert.That(await container.PopAsync(2), Is.False);
                Assert.That(await container.PopAsync(), Is.True);
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.Null);
            });
        }

        private ModalContainer CreateContainer(out GameObject prefab)
        {
            GameObject containerObject = Track(new GameObject("ModalContainer"));
            ModalContainer container = containerObject.AddComponent<ModalContainer>();
            prefab = Track(new GameObject("ModalPrefab"));
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TestModal>();
            container.Initialize(new FakeLoader(prefab));
            return container;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class ModalData
        {
            public ModalData(string source)
            {
                Source = source;
            }

            public string Source { get; }
        }

        private sealed class TestModal : ModalPanel<ModalData, string>
        {
            public string Source { get; private set; }
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            protected override UniTask OnPrepareAsync(ModalData data, CancellationToken cancellationToken)
            {
                Source = data.Source;
                return UniTask.CompletedTask;
            }

            public UniTask<bool> CompleteResultAsync(string result)
            {
                return CompleteAsync(result);
            }
        }

        private sealed class FakeLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public FakeLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }
    }
}
