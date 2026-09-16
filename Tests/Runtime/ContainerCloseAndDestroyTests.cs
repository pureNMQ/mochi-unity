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

                InvokeClose(container);

                //正常 Close：清空栈并恰好释放一次，容器本身继续可用；等一帧确保延迟销毁生效
                await UniTask.Yield();
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
                Assert.That(loader.ReleasedPrefabs, Is.EquivalentTo(new[] { secondPrefab, firstPrefab, firstPrefab }));
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

                InvokeClose(container);

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
                //被拒绝的重入 Push 无副作用：未触发加载；等一帧确保延迟销毁生效
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                await UniTask.Yield();
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
                //被拒绝的重入 Push 无副作用：未触发加载；等一帧确保延迟销毁生效
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                //重入发生在 Close 内，通知登记先于它执行且只交付一次
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
            });
        }

        [UnityTest]
        public IEnumerator Destroy_WhileBackExitPending_ReleasesExactlyOncePerEntry()
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

                //新契约：Back 同步完成，条目已移出栈；退场动画被门控挂起，实例延迟自毁
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.EqualTo(1));

                //销毁强杀：剩余条目由清栈释放；已脱离栈的门控实例随父级销毁，
                //退场等待被销毁令牌取消，句柄由 OnDestroy 兜底释放——每条目恰好一次
                UnityEngine.Object.DestroyImmediate(container.gameObject);
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(loader.ReleasedPrefabs, Is.EquivalentTo(new[] { firstPrefab, secondPrefab }));
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

                //Back 先至：同步收尾先把条目移出栈并销毁（所有权转移），释放随帧末自释放完成
                Assert.That(await container.BackAsync(), Is.True);
                await UniTask.Yield();
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
        public IEnumerator Page_CloseWhileBackExitPending_CompletesBackThenClearsRemainder()
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

                //新契约：Back 同步完成，退场动画被门控挂起（实例延迟自毁），不再有 Busy 窗口
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.IsBusy, Is.False);

                //容器 Close 清空剩余条目并复位；已脱离栈的门控实例不重复处理
                InvokeClose(container);

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                //正常 Close 后容器本身继续可用：新 Push 正常受理
                RecordingPage repushed = await container.PushAsync<RecordingPage>("first");
                Assert.That(repushed, Is.Not.Null);
                Assert.That(loader.LoadCount, Is.EqualTo(3));

                //释放门控：延迟自毁完成收尾，已移出条目恰好释放一次
                secondPage.ReleaseExit();
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(secondPage == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Modal_CloseWhileBackExitPending_CompletesBackThenClearsRemainder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject firstPrefab = Track(CreateModalPrefab<GatedExitModal>("FirstModalPrefab"));
                GameObject secondPrefab = Track(CreateModalPrefab<RecordingModal>("SecondModalPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                List<string> notifications = new List<string>();
                GatedExitModal firstModal = await container.PushAsync<GatedExitModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));
                RecordingModal secondModal = await container.PushAsync<RecordingModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));

                //新契约：Back 同步完成并按条目交付通知（不等退场动画与延迟销毁）
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.IsBusy, Is.False);
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));

                //容器 Close 清空剩余条目并复位 Backdrop，通知在清理后交付（恰好一次）
                InvokeClose(container);

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(notifications, Is.EqualTo(new[] { "False:", "False:" }));
                Assert.That(container.transform.Find("ModalBackdrop").GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f));

                //释放门控：延迟自毁完成收尾，两条目各自恰好释放一次
                firstModal.ReleaseExit();
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
                Assert.That(await container.BackAsync(), Is.False);
            });
        }

        private static void InvokeClose(Panel container)
        {
            container.Close();
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

            //退出过渡挂起在门控上，用于构造退场动画未完的延迟自毁窗口
            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                return gate.Task.AttachExternalCancellation(cancellationToken);
            }

            public void ReleaseExit()
            {
                gate.TrySetResult(true);
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

            public void ReleaseExit()
            {
                gate.TrySetResult(true);
            }
        }
    }
}
