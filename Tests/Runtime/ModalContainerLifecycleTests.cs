using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
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
        public IEnumerator PopCountAndClose_NotifyRemovedModalsTopFirstAndKeepResultDefault()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out RecordingLoader loader);
                List<string> notifications = new List<string>();
                NoDataModal root = await Push(container, "root", notifications, "root");
                NoDataModal middle = await Push(container, "middle", notifications, "middle");
                NoDataModal top = await Push(container, "top", notifications, "top");

                Assert.That(await container.PopAsync(2), Is.True);
                Assert.That(container.Current, Is.SameAs(root));
                Assert.That(container.Count, Is.EqualTo(1));
                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "callback:top:False:",
                    "event:top:False:",
                    "callback:middle:False:",
                    "event:middle:False:"
                }));
                Assert.That(middle.Handle.IsValid, Is.False);
                Assert.That(top.Handle.IsValid, Is.False);

                Assert.That(await container.CloseAsync(), Is.True);
                Assert.That(container.Count, Is.Zero);
                Assert.That(notifications, Is.EqualTo(new[]
                {
                    "callback:top:False:",
                    "event:top:False:",
                    "callback:middle:False:",
                    "event:middle:False:",
                    "callback:root:False:",
                    "event:root:False:"
                }));
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
                Assert.That(second.Handle.IsValid, Is.False);

                Assert.That(await first.CancelResultAsync(), Is.True);
                Assert.That(container.Count, Is.Zero);
                Assert.That(firstCallbackCount, Is.EqualTo(1));
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

                for (int frame = 0; frame < 120 && container.Count > 0; frame++)
                {
                    await UniTask.Yield();
                }

                Assert.That(modal.Handle.IsValid, Is.False);
                Assert.That(container.Count, Is.Zero);
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.Null);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Pop_WhenExitThrows_RestoresStackAndInteraction()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer containerObject = CreateContainer(
                    out RecordingLoader loader,
                    out GameObject firstPrefab,
                    out GameObject secondPrefab);
                firstPrefab.GetComponent<ExitFailureModal>().ShouldFailExit = false;
                secondPrefab.GetComponent<ExitFailureModal>().ShouldFailExit = true;
                ExitFailureModal first = await containerObject.PushAsync<ExitFailureModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => { }));
                ExitFailureModal second = await containerObject.PushAsync<ExitFailureModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => { }));

                Exception caught = null;
                try
                {
                    await containerObject.PopAsync();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.TypeOf<ExitFailureException>());
                Assert.That(containerObject.IsBusy, Is.False);
                Assert.That(containerObject.Count, Is.EqualTo(2));
                Assert.That(containerObject.Current, Is.SameAs(second));
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(second.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.False);
                Assert.That(second.CanvasGroup.interactable, Is.True);
                Assert.That(loader.ReleaseCount, Is.Zero);

                second.ShouldFailExit = false;
                Assert.That(await containerObject.PopAsync(), Is.True);
                Assert.That(containerObject.Count, Is.EqualTo(1));
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
                Assert.That(await container.PopAsync(), Is.False);
                Assert.That(await container.PushAsync<NoDataModal, string>("modal"), Is.Null);

                loader.Complete();
                Assert.That(await firstPush, Is.Not.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.Count, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator CleanupFailure_CommitsRemovalNotifiesAndThrowsFirstException()
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
                    await container.PopAsync();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.TypeOf<CleanupFailureException>());
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.Count, Is.Zero);
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
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
            protected override void OnHide()
            {
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
