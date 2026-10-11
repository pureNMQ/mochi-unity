using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    public class ModalContainerLifecycleTests
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
        public IEnumerator Back_NotifyOnlyCurrentThenCloseNotifiesRestTopFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader);
                List<string> notifications = new List<string>();
                NoDataModal root = await Push(container, "root", notifications, "root");
                NoDataModal middle = await Push(container, "middle", notifications, "middle");
                NoDataModal top = await Push(container, "top", notifications, "top");

                //R5：Back 一次只移除当前栈顶，只通知当前条目
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.Current, Is.SameAs(middle));
                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(root.IsVisible, Is.True);
                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "callback:top:False:",
                    "event:top:False:"
                }));
                container.Close();
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "callback:top:False:",
                    "event:top:False:",
                    "callback:middle:False:",
                    "event:middle:False:",
                    "callback:root:False:",
                    "event:root:False:"
                }));
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(3));
            });
        }

        [UnityTest]
        public IEnumerator CompleteAndCancel_OnlyTopModalCanCommitResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader);
                int firstCallbackCount = 0;
                int secondCallbackCount = 0;
                NoDataModal first = await container.PushAsync<NoDataModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => firstCallbackCount++));
                NoDataModal second = await container.PushAsync<NoDataModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => secondCallbackCount++));

                Assert.That(await first.CompleteResultAsync("invalid"), Is.False);
                Assert.That(container.Current, Is.SameAs(second));
                Assert.That(firstCallbackCount, Is.Zero);
                Assert.That(secondCallbackCount, Is.Zero);

                Assert.That(await second.CompleteResultAsync("accepted"), Is.True);
                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(firstCallbackCount, Is.Zero);
                Assert.That(secondCallbackCount, Is.EqualTo(1));
                Assert.That(await first.CancelResultAsync(), Is.True);
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(firstCallbackCount, Is.EqualTo(1));
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator ResultCallbackRunsAfterBusyClearsAndCanPushAnotherModal()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader);
                bool busyAtCallback = true;
                UniTask<NoDataModal> nextPush = default;
                NoDataModal current = await container.PushAsync<NoDataModal, string>(
                    "current",
                    new ModalResultCallback<string>((hasResult, result) =>
                    {
                        busyAtCallback = container.IsBusy;
                        nextPush = container.PushAsync<NoDataModal, string>("next");
                    }));

                Assert.That(await current.CompleteResultAsync("accepted"), Is.True);
                NoDataModal next = await nextPush;

                Assert.That(busyAtCallback, Is.False);
                Assert.That(next, Is.Not.Null);
                Assert.That(container.Current, Is.SameAs(next));
                Assert.That(container.StackCount, Is.EqualTo(1));
                container.Close();
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator BackdropClick_WhenEnabled_CancelsOnlyCurrentModal()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader, true);
                bool hasResult = true;
                string result = "unexpected";
                NoDataModal modal = await container.PushAsync<NoDataModal, string>(
                    "modal",
                    new ModalResultCallback<string>((valid, value) =>
                    {
                        hasResult = valid;
                        result = value;
                    }));
                Assert.That(modal.CloseOnBackdropClick, Is.True);

                IPointerClickHandler backdrop = container.transform
                    .Find("ModalBackdrop")
                    .GetComponent<IPointerClickHandler>();
                backdrop.OnPointerClick(new PointerEventData(EventSystem.current));

                await UniTask.WaitUntil(() => container.StackCount == 0)
                    .Timeout(TimeSpan.FromSeconds(2));

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.Null);
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Back_WhenExitThrows_StillCleansUpWithoutRollback()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer containerObject = CreateContainer(
                    out RecordingLoader loader,
                    out GameObject firstPrefab,
                    out GameObject secondPrefab);
                firstPrefab.GetComponent<ExitFailureModal>().ShouldFailExit = false;
                secondPrefab.GetComponent<ExitFailureModal>().ShouldFailExit = true;
                List<string> notifications = new List<string>();
                ExitFailureModal first = await containerObject.PushAsync<ExitFailureModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) =>
                        notifications.Add($"first:{hasResult}:{result}")));
                ExitFailureModal second = await containerObject.PushAsync<ExitFailureModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) =>
                        notifications.Add($"second:{hasResult}:{result}")));

                //新契约：退场过渡 fire-and-forget，Exit 抛错归组件——Close 已同步提交，
                //Back 正常返回，错误经 UniTask 未观察通道记录日志
                LogAssert.Expect(LogType.Exception, new Regex("ExitFailureException"));
                Assert.That(await containerObject.BackAsync(), Is.True);

                Assert.That(containerObject.IsBusy, Is.False);
                Assert.That(containerObject.StackCount, Is.EqualTo(1));
                Assert.That(containerObject.Current, Is.SameAs(first));
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.True);
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                //清理完成后按条目交付结果，Back 语义为 hasResult=false
                Assert.That(notifications, Is.EqualTo(new[] { "second:False:" }));

                //容器仍可用：继续 Back 移除剩余栈顶
                Assert.That(await containerObject.BackAsync(), Is.True);
                Assert.That(containerObject.StackCount, Is.EqualTo(0));
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator BusyPush_RejectsConcurrentOperationsWithoutChangingStack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = Track(CreatePrefab<NoDataModal>("ModalPrefab"));
                DelayedLoader loader = new DelayedLoader(prefab);
                container.Initialize(loader);

                UniTask<NoDataModal> firstPush = container.PushAsync<NoDataModal, string>("modal");
                await UniTask.Yield();
                Assert.That(container.IsBusy, Is.True);
                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(await container.PushAsync<NoDataModal, string>("modal"), Is.Null);

                loader.Complete();
                Assert.That(await firstPush, Is.Not.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator CleanupFailure_LifecycleThrowStopsCleanupAndPropagates()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = Track(CreatePrefab<FailOnHideModal>("FailingModalPrefab"));
                RecordingLoader loader = new RecordingLoader(prefab);
                container.Initialize(loader);
                List<string> notifications = new List<string>();
                await container.PushAsync<FailOnHideModal, string>(
                    "modal",
                    new ModalResultCallback<string>((hasResult, result) =>
                        notifications.Add($"{hasResult}:{result}")));

                Exception caught = null;
                try
                {
                    await container.BackAsync();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                //F5 可信原则：OnHide 抛错违反 Panel 生命周期契约，容器不防御、不收集错误；
                //新契约：容器清理先行（条目已解除）、Close 状态提交与自毁由 finally 保证完成，
                //异常原样传播；nextTop 恢复步骤被跳过（无下一层时不涉及）
                Assert.That(caught, Is.TypeOf<CleanupFailureException>());
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.EqualTo(0));
                //通知在清理完成后按条目交付
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator DestroyContainer_NotifiesEveryOpenModalAsCancelled()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader);
                List<string> notifications = new List<string>();
                NoDataModal first = await Push(container, "first", notifications, "first");
                NoDataModal second = await Push(container, "second", notifications, "second");

                UnityEngine.Object.DestroyImmediate(container.gameObject);
                await UniTask.Yield();

                Assert.That(first == null, Is.True);
                Assert.That(second == null, Is.True);
                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "callback:second:False:",
                    "event:second:False:",
                    "callback:first:False:",
                    "event:first:False:"
                }));
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            });
        }

        private async UniTask<NoDataModal> Push(
            ModalContainer container,
            string key,
            List<string> notifications,
            string label)
        {
            NoDataModal modal = await container.PushAsync<NoDataModal, string>(
                key,
                new ModalResultCallback<string>((hasResult, result) =>
                    notifications.Add($"callback:{label}:{hasResult}:{result}")));
            modal.ResultReturned += (hasResult, result) =>
                notifications.Add($"event:{label}:{hasResult}:{result}");
            return modal;
        }

        private ModalContainer CreateContainer(
            out RecordingLoader loader,
            bool closeOnBackdropClick = false)
        {
            GameObject prefab = Track(CreatePrefab<NoDataModal>("ModalPrefab"));
            if (closeOnBackdropClick)
            {
                SetCloseOnBackdropClick(prefab.GetComponent<NoDataModal>(), true);
            }

            GameObject containerObject = Track(new GameObject("ModalContainer"));
            ModalContainer container = containerObject.AddComponent<ModalContainer>();
            loader = new RecordingLoader(prefab);
            container.Initialize(loader);
            return container;
        }

        private ModalContainer CreateContainer(
            out RecordingLoader loader,
            out GameObject firstPrefab,
            out GameObject secondPrefab)
        {
            firstPrefab = Track(CreatePrefab<ExitFailureModal>("FirstModalPrefab"));
            secondPrefab = Track(CreatePrefab<ExitFailureModal>("SecondModalPrefab"));
            GameObject containerObject = Track(new GameObject("ModalContainer"));
            ModalContainer container = containerObject.AddComponent<ModalContainer>();
            loader = new RecordingLoader(firstPrefab, secondPrefab);
            container.Initialize(loader);
            return container;
        }

        private GameObject CreatePrefab<TModal>(string name) where TModal : ModalPanel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TModal>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private static void SetCloseOnBackdropClick(ModalPanel modal, bool enabled)
        {
            FieldInfo field = typeof(ModalPanel).GetField(
                "closeOnBackdropClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(modal, enabled);
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;

            public RecordingLoader(GameObject prefab)
                : this(prefab, null)
            {
            }

            public RecordingLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(secondPrefab == null || key == "first" ? firstPrefab : secondPrefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
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

        private sealed class NoDataModal : ModalPanel<string>
        {
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            public UniTask<bool> CompleteResultAsync(string result)
            {
                return CompleteAsync(result);
            }

            public UniTask<bool> CancelResultAsync()
            {
                return CancelAsync();
            }
        }

        private sealed class ExitFailureModal : ModalPanel<string>, IPanelTransition
        {
            [SerializeField] private bool shouldFailExit;

            public bool ShouldFailExit
            {
                get => shouldFailExit;
                set => shouldFailExit = value;
            }
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                if (ShouldFailExit)
                {
                    throw new ExitFailureException();
                }

                return UniTask.CompletedTask;
            }
        }

        private sealed class FailOnHideModal : ModalPanel<string>
        {
            private bool hasThrown;

            protected override void OnHide()
            {
                //只抛一次：TearDown 销毁容器时会再次触发清理路径的 OnHide，
                //若再次抛错会以未处理日志形式污染测试结果
                if (hasThrown)
                {
                    return;
                }

                hasThrown = true;
                throw new CleanupFailureException();
            }
        }

        private sealed class ExitFailureException : Exception
        {
        }

        private sealed class CleanupFailureException : Exception
        {
        }
    }
}
