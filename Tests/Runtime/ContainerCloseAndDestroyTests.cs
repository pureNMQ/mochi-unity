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
    //R4/F4 新契约用例：共用清栈与销毁/关闭接管——
    //正常 Close 后销毁=空操作、销毁中重入被拒、Back 与销毁竞争不重复 Release、Busy 关闭立即接管
    public class ContainerCloseAndDestroyTests
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
        public IEnumerator Page_CloseThenDestroy_ReleasesOnceAndContainerReusableAfterClose()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                await container.PushAsync<RecordingPage>("first");
                await container.PushAsync<RecordingPage>("second");

                InvokeCloseInternal(container);

                //正常 Close：清空栈并恰好释放一次，容器本身继续可用
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                RecordingPage repushed = await container.PushAsync<RecordingPage>("first");
                Assert.That(repushed, Is.Not.Null);

                //正常 Close 不标记 IsDisposed：关闭后再销毁仍执行完整时序，但清栈只处理重新入栈的条目
                UnityEngine.Object.DestroyImmediate(container.gameObject);
                await UniTask.Yield();

                Assert.That(loader.LoadCount, Is.EqualTo(3));
                //释放恰好：Close 释放 second、first，销毁清栈只处理重新入栈的 first，无重复释放
                Assert.That(loader.ReleaseCount, Is.EqualTo(3));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { secondPrefab, firstPrefab, firstPrefab }));
            });
        }

        [UnityTest]
        public IEnumerator Modal_CloseThenDestroy_NotifiesOnceReleasesOnceAndResetsBackdrop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject prefab = Track(CreateModalPrefab<RecordingModal>("ModalPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);
                container.Initialize(loader);

                List<string> notifications = new List<string>();
                await container.PushAsync<RecordingModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));

                InvokeCloseInternal(container);

                //正常 Close：无结果通知在清理完成且 Busy 解除后交付，Backdrop 复位
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(container.transform.Find("ModalBackdrop").GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f));

                UnityEngine.Object.DestroyImmediate(container.gameObject);
                await UniTask.Yield();

                //销毁清栈为空操作：通知不重复、Release 不重复
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.LoadCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Destroy_ReentrantNavigationFromPageOnClose_IsRejectedWithoutSideEffects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject prefab = Track(CreatePagePrefab<ReentrantPage>("ReentrantPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);
                container.Initialize(loader);

                ReentrantPage page = await container.PushAsync<ReentrantPage>("first");
                page.Owner = container;

                UnityEngine.Object.DestroyImmediate(container.gameObject);

                //F4 时序：销毁入口最先标记 IsDisposed，清栈回调里的重入导航被立即拒绝
                Assert.That(await page.ReentrantPush, Is.Null);
                Assert.That(await page.ReentrantBack, Is.False);
                //被拒绝的重入 Push 无副作用：未触发加载，原条目恰好释放一次
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(container.StackCount, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator Destroy_ReentrantNavigationFromModalOnClose_IsRejectedAndNotifiesOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject prefab = Track(CreateModalPrefab<ReentrantModal>("ReentrantModalPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);
                container.Initialize(loader);

                List<string> notifications = new List<string>();
                ReentrantModal modal = await container.PushAsync<ReentrantModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));
                modal.Owner = container;

                UnityEngine.Object.DestroyImmediate(container.gameObject);

                //清栈回调里的重入导航被拒绝
                Assert.That(await modal.ReentrantPush, Is.Null);
                Assert.That(await modal.ReentrantBack, Is.False);
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                //重入发生在 CloseInternal 内，通知登记先于它执行且只交付一次
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
            });
        }

        [UnityTest]
        public IEnumerator Destroy_WhileBackAwaitingExit_TakesOverStackWithoutDoubleRelease()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<GatedExitPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                GatedExitPage secondPage = await container.PushAsync<GatedExitPage>("second");

                //Back 挂起于退出过渡：只引用当前条目，所有权未转移
                UniTask<bool> backTask = container.BackAsync();
                await UniTask.Yield();
                Assert.That(container.IsBusy, Is.True);

                //销毁先至：接管整栈，全部条目由销毁清栈释放
                UnityEngine.Object.DestroyImmediate(container.gameObject);
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { secondPrefab, firstPrefab }));

                //Back 异步收尾看到 IsDisposed 后跳过条目清理，以取消异常结束，不重复释放
                Exception caught = null;
                try
                {
                    await backTask;
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.TypeOf<OperationCanceledException>());
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(firstPage == null, Is.True);
                Assert.That(secondPage == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Back_CompletesBeforeDestroy_ReleasesExactlyOncePerEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                await container.PushAsync<RecordingPage>("first");
                await container.PushAsync<RecordingPage>("second");

                //Back 先至：同步收尾先把条目移出栈并释放，所有权不再属于栈
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(container.StackCount, Is.EqualTo(1));

                UnityEngine.Object.DestroyImmediate(container.gameObject);
                await UniTask.Yield();

                //销毁清栈拿不到已移出的条目：每个条目恰好释放一次
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { secondPrefab, firstPrefab }));
            });
        }

        [UnityTest]
        public IEnumerator Page_CloseWhileBackInFlight_TakesOverImmediately()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<GatedExitPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                GatedExitPage secondPage = await container.PushAsync<GatedExitPage>("second");

                UniTask<bool> backTask = container.BackAsync();
                await UniTask.Yield();
                Assert.That(container.IsBusy, Is.True);

                //父容器关闭路径经由 CloseInternal 触发子容器 OnClose；Busy 时立即接管，不排队/挂起
                InvokeCloseInternal(container);

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                //接管后容器不可用：新导航被拒绝且无副作用
                Assert.That(await container.PushAsync<RecordingPage>("first"), Is.Null);
                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(loader.LoadCount, Is.EqualTo(2));

                //在途 Back 收尾看到不可用状态后跳过条目清理，以取消异常结束
                Exception caught = null;
                try
                {
                    await backTask;
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.TypeOf<OperationCanceledException>());
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                await UniTask.Yield();
                Assert.That(firstPage == null, Is.True);
                Assert.That(secondPage == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Modal_CloseWhileBackInFlight_TakesOverImmediatelyAndNotifiesOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject prefab = Track(CreateModalPrefab<GatedExitModal>("ModalPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);
                container.Initialize(loader);

                List<string> notifications = new List<string>();
                GatedExitModal modal = await container.PushAsync<GatedExitModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));

                UniTask<bool> backTask = container.BackAsync();
                await UniTask.Yield();
                Assert.That(container.IsBusy, Is.True);

                //Busy 时关闭立即接管：清栈释放、复位 Backdrop、清理完成后交付通知（恰好一次）
                InvokeCloseInternal(container);

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                Assert.That(container.transform.Find("ModalBackdrop").GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f));

                Exception caught = null;
                try
                {
                    await backTask;
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                //在途 Back 收尾跳过登记与清理：通知不重复、资源不重复释放
                Assert.That(caught, Is.TypeOf<OperationCanceledException>());
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(await container.BackAsync(), Is.False);
            });
        }

        private static void InvokeCloseInternal(Panel container)
        {
            typeof(Panel)
                .GetMethod("CloseInternal", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(container, null);
        }

        private GameObject CreatePagePrefab<TPage>(string name) where TPage : Panel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TPage>();
            return prefab;
        }

        private GameObject CreateModalPrefab<TModal>(string name) where TModal : ModalPanel
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

        private sealed class KeyedLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;
            private int loadCount;

            public KeyedLoader(GameObject firstPrefab, GameObject secondPrefab = null)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public int LoadCount => loadCount;
            public int ReleaseCount { get; private set; }
            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                loadCount++;
                if (key == "first")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "second")
                {
                    return UniTask.FromResult(secondPrefab);
                }

                throw new ArgumentException("Unexpected test key: " + key);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
                ReleasedPrefabs.Add(loadedPrefab);
            }
        }

        private sealed class RecordingPage : Panel
        {
        }

        private sealed class RecordingModal : ModalPanel<string>
        {
        }

        private sealed class ReentrantPage : Panel
        {
            //容器在 OnDestroy 过程中 GetComponentInParent 解析不到，改用入栈后注入的引用
            public PageContainer Owner { get; set; }
            public UniTask<ReentrantPage> ReentrantPush { get; private set; }
            public UniTask<bool> ReentrantBack { get; private set; }

            protected override void OnClose()
            {
                //销毁清栈回调中的重入导航：IsDisposed 已前置，必须被立即拒绝
                ReentrantPush = Owner.PushAsync<ReentrantPage>("second");
                ReentrantBack = Owner.BackAsync();
            }
        }

        private sealed class ReentrantModal : ModalPanel<string>
        {
            //容器在 OnDestroy 过程中 GetComponentInParent 解析不到，改用入栈后注入的引用
            public ModalContainer Owner { get; set; }
            public UniTask<ReentrantModal> ReentrantPush { get; private set; }
            public UniTask<bool> ReentrantBack { get; private set; }

            protected override void OnClose()
            {
                ReentrantPush = Owner.PushAsync<ReentrantModal, string>("second", null);
                ReentrantBack = Owner.BackAsync();
            }
        }

        private sealed class GatedExitPage : Panel, IPanelTransition
        {
            private readonly UniTaskCompletionSource<bool> gate = new UniTaskCompletionSource<bool>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            //退出过渡挂起在门控上，用于构造 Back 在途与销毁/关闭竞争
            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                return gate.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        private sealed class GatedExitModal : ModalPanel<string>, IPanelTransition
        {
            private readonly UniTaskCompletionSource<bool> gate = new UniTaskCompletionSource<bool>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                return gate.Task.AttachExternalCancellation(cancellationToken);
            }
        }
    }
}
