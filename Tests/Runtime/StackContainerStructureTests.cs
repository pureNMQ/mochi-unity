using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    public class StackContainerStructureTests
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

        [Test]
        public void StackContainer_IsAbstractAndOwnsSharedStackEntry()
        {
            Assert.That(typeof(StackContainer).IsAbstract, Is.True);
            Assert.That(typeof(PageContainer).IsSubclassOf(typeof(StackContainer)), Is.True);
            Assert.That(typeof(ModalContainer).IsSubclassOf(typeof(StackContainer)), Is.True);
            Type stackEntryType = typeof(StackContainer).GetNestedType("StackEntry", BindingFlags.NonPublic);
            Assert.That(stackEntryType, Is.Not.Null);
            Assert.That(stackEntryType.GetProperty("Panel"), Is.Not.Null);
            Assert.That(stackEntryType.GetProperty("Page"), Is.Null);
        }

        [Test]
        public void IsBusy_HasPublicGetterAndProtectedSetter()
        {
            PropertyInfo property = typeof(PanelContainer).GetProperty(
                "IsBusy",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.GetMethod.IsPublic, Is.True);
            Assert.That(property.GetSetMethod(), Is.Null);
            Assert.That(property.GetSetMethod(true).IsFamily, Is.True);
        }

        [UnityTest]
        public IEnumerator Initialize_RejectsNullAndDuplicateLoader()
        {
            GameObject containerObject = Track(new GameObject("PageContainer"));
            PageContainer container = containerObject.AddComponent<PageContainer>();
            RecordingLoader loader = new RecordingLoader();

            Assert.Throws<ArgumentNullException>(() => container.Initialize(null));
            container.Initialize(loader);
            Assert.Throws<InvalidOperationException>(() => container.Initialize(loader));

            yield return null;
        }

        [UnityTest]
        public IEnumerator TypeMismatchWithThrowingRelease_LeavesBusyClearAndReleasesOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject pageContainerObject = Track(new GameObject("PageContainer"));
                PageContainer pageContainer = pageContainerObject.AddComponent<PageContainer>();
                GameObject pagePrefab = Track(new GameObject("PagePrefab"));
                pagePrefab.SetActive(false);
                ThrowingReleaseLoader pageLoader = new ThrowingReleaseLoader(pagePrefab);
                pageContainer.Initialize(pageLoader);

                Exception pageException = null;
                try
                {
                    await pageContainer.PushAsync<TestPage>("page");
                }
                catch (Exception exception)
                {
                    pageException = exception;
                }

                Assert.That(pageException, Is.TypeOf<ReleaseFailureException>());
                Assert.That(pageContainer.IsBusy, Is.False);
                Assert.That(pageLoader.ReleaseCount, Is.EqualTo(1));

                GameObject modalContainerObject = Track(new GameObject("ModalContainer"));
                ModalContainer modalContainer = modalContainerObject.AddComponent<ModalContainer>();
                GameObject modalPrefab = Track(new GameObject("ModalPrefab"));
                modalPrefab.SetActive(false);
                ThrowingReleaseLoader modalLoader = new ThrowingReleaseLoader(modalPrefab);
                modalContainer.Initialize(modalLoader);

                Exception modalException = null;
                try
                {
                    await modalContainer.PushAsync<TestModal>("modal");
                }
                catch (Exception exception)
                {
                    modalException = exception;
                }

                Assert.That(modalException, Is.TypeOf<ReleaseFailureException>());
                Assert.That(modalContainer.IsBusy, Is.False);
                Assert.That(modalLoader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator BusyState_IsIndependentAcrossContainers()
        {
            GameObject firstContainerObject = Track(new GameObject("FirstPageContainer"));
            GameObject secondContainerObject = Track(new GameObject("SecondPageContainer"));
            PageContainer firstContainer = firstContainerObject.AddComponent<PageContainer>();
            PageContainer secondContainer = secondContainerObject.AddComponent<PageContainer>();
            GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab"));
            GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab"));
            DelayedLoader firstLoader = new DelayedLoader(firstPrefab);
            DelayedLoader secondLoader = new DelayedLoader(secondPrefab);
            firstContainer.Initialize(firstLoader);
            secondContainer.Initialize(secondLoader);

            UniTask<TestPage> firstPush = firstContainer.PushAsync<TestPage>("first");
            UniTask<TestPage> secondPush = secondContainer.PushAsync<TestPage>("second");
            yield return null;

            Assert.That(firstContainer.IsBusy, Is.True);
            Assert.That(secondContainer.IsBusy, Is.True);

            firstLoader.Complete();
            secondLoader.Complete();
            yield return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await firstPush, Is.Not.Null);
                Assert.That(await secondPush, Is.Not.Null);
            });
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            prefab.AddComponent<TestPage>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }

        private sealed class ThrowingReleaseLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public ThrowingReleaseLoader(GameObject prefab)
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
                throw new ReleaseFailureException();
            }
        }

        private sealed class DelayedLoader : IPanelLoader
        {
            private readonly GameObject prefab;
            private readonly UniTaskCompletionSource<GameObject> completionSource =
                new UniTaskCompletionSource<GameObject>();

            public DelayedLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return completionSource.Task.AttachExternalCancellation(cancellationToken);
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void Complete()
            {
                completionSource.TrySetResult(prefab);
            }
        }

        private sealed class TestPage : Panel
        {
        }

        private sealed class TestModal : ModalPanel
        {
        }

        private sealed class ReleaseFailureException : Exception
        {
        }
    }
}
