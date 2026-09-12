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
    //C1 新契约用例：容器显隐生命周期传播与隐藏期导航——
    //隐藏期 Push/Back 无 OnShow 无动画但逻辑正常、Show 按最终栈呈现、Hide 中断在途视觉不停逻辑、
    //根容器 Initialize 后首 Push 带视觉、Modal 隐藏期完成仍清理后通知
    public class ContainerLifecyclePropagationTests
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
        public IEnumerator Page_HideThenPushAndBack_SkipsVisualsButCompletesLogic()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                Assert.That(firstPage.ShowCount, Is.EqualTo(1));
                Assert.That(firstPage.Transition.EnterCount, Is.EqualTo(1));

                container.Hide();

                //容器 Hide：自身逻辑隐藏，向栈内 Panel 传播 Hide 请求（已显示条目执行 OnHide）
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(firstPage.gameObject.activeSelf, Is.False);
                Assert.That(firstPage.HideCount, Is.EqualTo(1));

                //隐藏期 Push：加载、实例化、Prepare、Open、入栈正常；新页保持逻辑隐藏，
                //不触发 OnShow、不播放入场过渡、不启用输入
                RecordingPage secondPage = await container.PushAsync<RecordingPage>("second");
                Assert.That(secondPage, Is.Not.Null);
                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(loader.ReleaseCount, Is.Zero);
                Assert.That(secondPage.IsVisible, Is.False);
                Assert.That(secondPage.gameObject.activeSelf, Is.False);
                Assert.That(secondPage.ShowCount, Is.Zero);
                Assert.That(secondPage.Transition.EnterCount, Is.Zero);
                Assert.That(secondPage.CanvasGroup.interactable, Is.False);

                //隐藏期 Back：当前条目照常出栈、销毁、释放；无 Exit 过渡、不 Show 前页
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { secondPrefab }));
                Assert.That(secondPage.Transition.ExitCount, Is.Zero);
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(firstPage.ShowCount, Is.EqualTo(1));
                Assert.That(firstPage.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(firstPage.CanvasGroup.interactable, Is.False);

                //销毁在帧末生效，等一帧后确认实例已销毁
                await UniTask.DelayFrame(1);
                Assert.That(secondPage == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Page_ShowAfterHiddenNavigation_PresentsOnlyFinalTopThenEnablesInput()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                GameObject thirdPrefab = Track(CreatePagePrefab<RecordingPage>("ThirdPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab, thirdPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                container.Hide();
                RecordingPage secondPage = await container.PushAsync<RecordingPage>("second");
                RecordingPage thirdPage = await container.PushAsync<RecordingPage>("third");
                Assert.That(container.StackCount, Is.EqualTo(3));

                //隐藏期 Back：C 出栈，最终栈为 A、B
                Assert.That(await container.BackAsync(), Is.True);

                //中间态 C 不补播：无 OnShow、无 Enter/Exit
                Assert.That(thirdPage.ShowCount, Is.Zero);
                Assert.That(thirdPage.Transition.EnterCount, Is.Zero);
                Assert.That(thirdPage.Transition.ExitCount, Is.Zero);

                //Show 按最终栈呈现：只呈现最终 B（OnShow 与入场各一次，不重放隐藏期中间态）
                secondPage.Transition.HoldEnter = true;
                UniTask showTask = container.ShowAsync();
                await UniTask.Yield();

                //OnShow 为同步通知；转场期间输入保持禁用；前页保持隐藏不重复 OnShow
                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.ShowCount, Is.EqualTo(1));
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(secondPage.CanvasGroup.interactable, Is.False);
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(firstPage.ShowCount, Is.EqualTo(1));
                Assert.That(firstPage.HideCount, Is.EqualTo(1));

                secondPage.Transition.ReleaseEnter();
                await showTask;

                //动画结束后再启用应接收输入的条目
                Assert.That(secondPage.CanvasGroup.interactable, Is.True);
                Assert.That(container.IsBusy, Is.False);
            });
        }

        [UnityTest]
        public IEnumerator Page_HideDuringEnterTransition_StopsVisualAndCompletesNavigation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                //门控入场沿用 GatedExitPage 模式：挂起在预制体组件上，克隆实例随实例化携带
                secondPrefab.GetComponent<RecordingPage>().Transition.HoldEnter = true;
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");

                UniTask<RecordingPage> pushTask = container.PushAsync<RecordingPage>("second");
                await UniTask.Yield();
                Assert.That(container.IsBusy, Is.True);

                RecordingPage secondPage =
                    Array.Find(container.GetComponentsInChildren<RecordingPage>(true), page => page != firstPage);
                Assert.That(secondPage, Is.Not.Null);
                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(1));

                //Hide 中断在途入场：视觉被停止，导航逻辑按隐藏期规则继续完成，无防御性异常
                container.Hide();
                //取消续体经 PlayerLoop 恢复，等操作真正收尾（Busy 解除）而不是固定帧数
                await UniTask.WaitUntil(() => !container.IsBusy).Timeout(TimeSpan.FromSeconds(5));

                //Push 已完成：页面入栈并收回逻辑隐藏（中断前已 Show，执行 OnHide 收回），不启用输入
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(secondPage.IsVisible, Is.False);
                Assert.That(secondPage.ShowCount, Is.EqualTo(1));
                Assert.That(secondPage.HideCount, Is.EqualTo(1));
                Assert.That(secondPage.CanvasGroup.interactable, Is.False);
                Assert.That(firstPage.IsVisible, Is.False);

                RecordingPage pushed = await pushTask;
                Assert.That(pushed, Is.SameAs(secondPage));

                //恢复显示：呈现最终栈，被中断的页面在此补齐入场；动画结束后启用输入
                UniTask showTask = container.ShowAsync();
                await UniTask.Yield();
                Assert.That(secondPage.CanvasGroup.interactable, Is.False);
                secondPage.Transition.ReleaseEnter();
                await showTask;

                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.ShowCount, Is.EqualTo(2));
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(2));
                Assert.That(secondPage.CanvasGroup.interactable, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Navigator_InitializedRootContainers_FirstPushHasVisuals()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject root = Track(new GameObject("GUIRoot"));
                GUINavigator navigator = root.AddComponent<GUINavigator>();
                GameObject prefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);

                navigator.Initialize(loader);

                //C1 核对断言：根容器 Initialize 成功即逻辑显示（视觉跳过默认复位），
                //首个 Push 正常执行 OnShow 与入场过渡并启用输入，业务无需手动 Show
                RecordingPage page = await navigator.Pages.PushAsync<RecordingPage>("first");

                Assert.That(page, Is.Not.Null);
                Assert.That(page.IsVisible, Is.True);
                Assert.That(page.ShowCount, Is.EqualTo(1));
                Assert.That(page.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(page.CanvasGroup.interactable, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Modal_CompleteWhileHidden_NotifiesAfterCleanupWithoutWaitingForShow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject prefab = Track(CreateModalPrefab<RecordingModal>("ModalPrefab"));
                KeyedLoader loader = new KeyedLoader(prefab);
                container.Initialize(loader);
                SetBackdropDuration(container, 0f);

                List<string> notifications = new List<string>();
                RecordingModal modal = await container.PushAsync<RecordingModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));
                Assert.That(modal.ShowCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                container.Hide();
                Assert.That(modal.IsVisible, Is.False);
                Assert.That(modal.HideCount, Is.EqualTo(1));

                //隐藏期完成：不等容器显示，按确定的结果在清理完成后立即通知
                Assert.That(await modal.CompleteResultAsync("ok"), Is.True);

                Assert.That(notifications, Is.EqualTo(new[] { "True:ok" }));
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                //清理收尾复位 Backdrop；不补 OnShow
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(0f));
                Assert.That(GetBackdropRaycasts(container), Is.False);
                Assert.That(modal.ShowCount, Is.EqualTo(1));

                await UniTask.DelayFrame(1);
                Assert.That(modal == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Modal_HiddenPushThenShow_RestoresLayersAndBackdrop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                GameObject firstPrefab = Track(CreateModalPrefab<RecordingModal>("FirstModalPrefab"));
                GameObject secondPrefab = Track(CreateModalPrefab<RecordingModal>("SecondModalPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);
                SetBackdropDuration(container, 0f);

                List<string> notifications = new List<string>();
                RecordingModal firstModal = await container.PushAsync<RecordingModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add("first:" + hasResult)));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                container.Hide();
                RecordingModal secondModal = await container.PushAsync<RecordingModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add("second:" + hasResult)));

                //隐藏期 Push：无 OnShow、无入场，Backdrop 不重复淡入淡出（保持在场状态）
                Assert.That(secondModal.IsVisible, Is.False);
                Assert.That(secondModal.ShowCount, Is.Zero);
                Assert.That(secondModal.Transition.EnterCount, Is.Zero);
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                //Show 按多层展示规则恢复所需层并同步 Backdrop；动画结束后仅栈顶层启用输入
                await container.ShowAsync();

                Assert.That(firstModal.IsVisible, Is.True);
                Assert.That(secondModal.IsVisible, Is.True);
                Assert.That(firstModal.ShowCount, Is.EqualTo(2));
                Assert.That(firstModal.Transition.EnterCount, Is.EqualTo(2));
                Assert.That(secondModal.ShowCount, Is.EqualTo(1));
                Assert.That(secondModal.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));
                Assert.That(GetBackdropRaycasts(container), Is.True);
                Assert.That(
                    GetSiblingNames(container.transform),
                    Is.EqualTo(new[] { "FirstModalPrefab", "ModalBackdrop", "SecondModalPrefab" }));
                Assert.That(secondModal.CanvasGroup.interactable, Is.True);
                Assert.That(firstModal.CanvasGroup.interactable, Is.False);

                //再次隐藏后完成栈顶 Modal：不 Show 前层、不启用其输入，通知仍在清理后交付
                container.Hide();
                Assert.That(await secondModal.CompleteResultAsync("done"), Is.True);

                //first 仍在栈中不交付通知；second 按确定结果在清理后交付
                Assert.That(notifications, Is.EqualTo(new[] { "second:True" }));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(firstModal.IsVisible, Is.False);
                Assert.That(firstModal.CanvasGroup.interactable, Is.False);
            });
        }

        private static void SetBackdropDuration(ModalContainer container, float duration)
        {
            typeof(ModalContainer)
                .GetField("backdropFadeDuration", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(container, duration);
        }

        private static float GetBackdropAlpha(ModalContainer container)
        {
            return container.transform.Find("ModalBackdrop").GetComponent<CanvasGroup>().alpha;
        }

        private static bool GetBackdropRaycasts(ModalContainer container)
        {
            return container.transform.Find("ModalBackdrop").GetComponent<CanvasGroup>().blocksRaycasts;
        }

        private static string[] GetSiblingNames(Transform root)
        {
            string[] names = new string[root.childCount];
            for (int index = 0; index < root.childCount; index++)
            {
                //实例名会带 (Clone) 后缀，比较时统一剥掉，断言保持可读的语义名
                names[index] = root.GetChild(index).name.Replace("(Clone)", string.Empty);
            }

            return names;
        }

        private GameObject CreatePagePrefab<TPage>(string name) where TPage : Panel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TPage>();
            prefab.AddComponent<CounterTransition>();
            return prefab;
        }

        private GameObject CreateModalPrefab<TModal>(string name) where TModal : ModalPanel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TModal>();
            prefab.AddComponent<CounterTransition>();
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
            private readonly GameObject thirdPrefab;

            public KeyedLoader(GameObject firstPrefab, GameObject secondPrefab = null, GameObject thirdPrefab = null)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
                this.thirdPrefab = thirdPrefab;
            }

            public int ReleaseCount { get; private set; }
            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "second")
                {
                    return UniTask.FromResult(secondPrefab);
                }

                if (key == "third")
                {
                    return UniTask.FromResult(thirdPrefab);
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
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }

            public CounterTransition Transition => GetComponent<CounterTransition>();

            protected override void OnShow()
            {
                ShowCount++;
            }

            protected override void OnHide()
            {
                HideCount++;
            }
        }

        private sealed class RecordingModal : ModalPanel<string>
        {
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }

            public CounterTransition Transition => GetComponent<CounterTransition>();

            public UniTask<bool> CompleteResultAsync(string result)
            {
                return CompleteAsync(result);
            }

            protected override void OnShow()
            {
                ShowCount++;
            }

            protected override void OnHide()
            {
                HideCount++;
            }
        }

        //计数型过渡组件：记录 Enter/Exit 调用次数，可门控挂起入场（供隐藏中断与动画后启用输入断言）
        private sealed class CounterTransition : MonoBehaviour, IPanelTransition
        {
            private readonly UniTaskCompletionSource enterGate = new UniTaskCompletionSource();
            [SerializeField] private bool holdEnter;

            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }

            public bool HoldEnter
            {
                get => holdEnter;
                set => holdEnter = value;
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                //门控挂起经取消令牌关联：Hide 停止视觉时立即以取消结束（组件契约保证干净停止）
                return holdEnter
                    ? enterGate.Task.AttachExternalCancellation(cancellationToken)
                    : UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }

            public void ReleaseEnter()
            {
                enterGate.TrySetResult();
            }
        }
    }
}
