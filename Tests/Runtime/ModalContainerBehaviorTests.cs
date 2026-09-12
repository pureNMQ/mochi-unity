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
    public class ModalContainerBehaviorTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [Test]
        public void PublicApi_DoesNotExposeModalHandleOrPopTo()
        {
            Assert.That(typeof(ModalPanel).GetProperty("Handle"), Is.Null);
            Assert.That(typeof(ModalContainer).GetMethod("PopToAsync"), Is.Null);
            //R5：PopAsync(int) 已收窄删除，公开 API 仅保留 BackAsync() 单次栈顶
            Assert.That(
                typeof(ModalContainer).GetMethod(
                    "PopAsync",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
                Is.Null);
            Assert.That(
                typeof(ModalContainer).Assembly.GetType("Mochi.Unity.Preview.GUI.ModalHandle"),
                Is.Null);
        }

        [Test]
        public void ResultCallbackBinding_IsStronglyTypedOnGenericPanelOnly()
        {
            //R6：非泛型 ModalPanel 不再提供 Delegate 版 BindResultCallback
            Assert.That(
                typeof(ModalPanel).GetMember(
                    "BindResultCallback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                Is.Empty);

            //泛型 ModalPanel<TResult> 仅提供强类型签名，类型不匹配在编译期发现
            System.Reflection.MethodInfo callbackBinding = typeof(ModalPanel<string>).GetMethod(
                "BindResultCallback",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(callbackBinding, Is.Not.Null);
            Assert.That(
                callbackBinding.GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(ModalResultCallback<string>)));
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
                Assert.That(container.StackCount, Is.Zero);
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
                Assert.That(await container.BackAsync(), Is.True);

                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(cancellationCount, Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator Back_RejectsEmptyStackAndBusyRequests_CancelNotifiesDefaultResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                //R5：count 参数已删除，BackAsync 一次只移除当前栈顶；空栈/Busy/已销毁拒绝语义保留
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = Track(new GameObject("ModalPrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<CanvasGroup>();
                prefab.AddComponent<TestModal>();
                DelayedLoader loader = new DelayedLoader(prefab);
                container.Initialize(loader);

                int callbackCount = 0;
                bool hasResult = true;
                string result = "unexpected";

                //空栈拒绝：不产生通知
                Assert.That(await container.BackAsync(), Is.False);

                UniTask<TestModal> pushTask = container.PushAsync<TestModal, ModalData, string>(
                    "modal",
                    new ModalData("modal"),
                    new ModalResultCallback<string>((valid, value) =>
                    {
                        callbackCount++;
                        hasResult = valid;
                        result = value;
                    }));
                await UniTask.Yield();
                //Busy 请求拒绝：入栈未完成期间 Back 不生效且不产生通知
                Assert.That(container.IsBusy, Is.True);
                Assert.That(await container.BackAsync(), Is.False);

                loader.Complete();
                TestModal modal = await pushTask;
                Assert.That(container.Current, Is.SameAs(modal));
                Assert.That(callbackCount, Is.EqualTo(0));

                //CancelAsync(modal)：默认结果语义 hasResult=false、result=default
                Assert.That(await modal.CancelResultAsync(), Is.True);
                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.Null);
                Assert.That(container.StackCount, Is.Zero);

                //栈已空后再次 Back 仍被拒绝且不通知
                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(callbackCount, Is.EqualTo(1));
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

            public UniTask<bool> CancelResultAsync()
            {
                return CancelAsync();
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
