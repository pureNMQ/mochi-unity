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
    //容器显隐契约：容器 Hide 不传播子面板，重显按最终栈立即呈现，根 Layer 创建即显示
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
        public IEnumerator Page_ContainerHide_DoesNotPropagateToChildren()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                Assert.That(firstPage.ShowCount, Is.EqualTo(1));

                container.Show();
                Assert.That(container.IsVisible, Is.True);

                //容器 Hide 只停用自身整树，子面板逻辑状态不动
                container.Hide();
                Assert.That(container.gameObject.activeSelf, Is.False);
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.HideCount, Is.Zero);

                //重显不重播子面板过渡
                container.Show();
                Assert.That(container.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.ShowCount, Is.EqualTo(1));
                Assert.That(firstPage.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Page_HiddenPushAndBack_CompleteLogic_ShowPresentsFinalTop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);
                container.Show();

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                container.Hide();

                //容器隐藏期 Push 正常推进：新页照常 Show，前页照常 Hide
                RecordingPage secondPage = await container.PushAsync<RecordingPage>("second");
                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.ShowCount, Is.EqualTo(1));
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(firstPage.HideCount, Is.EqualTo(1));

                //隐藏期 Back：出栈、释放照常；前页恢复显示
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(secondPage.Transition.ExitCount, Is.EqualTo(1));
                await UniTask.DelayFrame(1);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EquivalentTo(new[] { secondPrefab }));
                Assert.That(secondPage == null, Is.True);
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.ShowCount, Is.EqualTo(2));

                container.Show();
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Page_HideDuringHeldEnter_CancelsEnterAndReplaysOnNextShow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab<RecordingPage>("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab<RecordingPage>("SecondPrefab"));
                secondPrefab.GetComponent<RecordingPage>().Transition.HoldEnter = true;
                KeyedLoader loader = new KeyedLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                RecordingPage firstPage = await container.PushAsync<RecordingPage>("first");
                RecordingPage secondPage = await container.PushAsync<RecordingPage>("second");
                Assert.That(container.IsBusy, Is.False);
                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(1));

                //面板级 Hide 取消在途入场并播退场，退场完成即停用
                secondPage.Hide();
                Assert.That(secondPage.IsVisible, Is.False);
                Assert.That(secondPage.HideCount, Is.EqualTo(1));
                Assert.That(secondPage.Transition.ExitCount, Is.EqualTo(1));
                Assert.That(secondPage.gameObject.activeSelf, Is.False);

                //再次显示播新入场，不受被取消的旧入场影响
                secondPage.Show();
                Assert.That(secondPage.IsVisible, Is.True);
                Assert.That(secondPage.Transition.EnterCount, Is.EqualTo(2));
                Assert.That(secondPage.CanvasGroup.interactable, Is.True);

                secondPage.Transition.ReleaseEnter();
                await UniTask.Yield();
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

                //根 Layer 创建即逻辑显示，首个 Push 带视觉，业务无需手动 Show
                Assert.That(navigator.Pages.IsVisible, Is.True);
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
                container.Show();

                List<string> notifications = new List<string>();
                RecordingModal modal = await container.PushAsync<RecordingModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add(hasResult + ":" + result)));
                Assert.That(modal.ShowCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                //容器隐藏不影响子面板逻辑状态
                container.Hide();
                Assert.That(modal.IsVisible, Is.True);
                Assert.That(modal.HideCount, Is.Zero);

                //隐藏期完成：不等容器显示，按确定的结果在清理完成后立即通知
                Assert.That(await modal.CompleteResultAsync("ok"), Is.True);

                Assert.That(notifications, Is.EqualTo(new[] { "True:ok" }));
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.IsBusy, Is.False);
                await UniTask.DelayFrame(1);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(0f));
                Assert.That(GetBackdropRaycasts(container), Is.False);
                Assert.That(modal.ShowCount, Is.EqualTo(1));

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
                container.Show();

                List<string> notifications = new List<string>();
                RecordingModal firstModal = await container.PushAsync<RecordingModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add("first:" + hasResult)));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                container.Hide();
                RecordingModal secondModal = await container.PushAsync<RecordingModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add("second:" + hasResult)));

                //隐藏期 Push 照常 Show，已有 Backdrop 不重复淡入
                Assert.That(secondModal.IsVisible, Is.True);
                Assert.That(secondModal.ShowCount, Is.EqualTo(1));
                Assert.That(secondModal.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                //重显按最终栈立即呈现：隐藏层补显示但不重播入场
                container.Show();

                Assert.That(firstModal.IsVisible, Is.True);
                Assert.That(secondModal.IsVisible, Is.True);
                //Modal 多层展示：前层未被隐藏，重显不重复 OnShow
                Assert.That(firstModal.ShowCount, Is.EqualTo(1));
                Assert.That(firstModal.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(secondModal.ShowCount, Is.EqualTo(1));
                Assert.That(secondModal.Transition.EnterCount, Is.EqualTo(1));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));
                Assert.That(GetBackdropRaycasts(container), Is.True);
                Assert.That(
                    GetSiblingNames(container.transform),
                    Is.EqualTo(new[] { "FirstModalPrefab", "ModalBackdrop", "SecondModalPrefab" }));
                Assert.That(secondModal.CanvasGroup.interactable, Is.True);
                Assert.That(firstModal.CanvasGroup.interactable, Is.False);

                //再次隐藏后完成栈顶 Modal：剩余栈顶恢复显示与输入，通知在清理后交付
                container.Hide();
                Assert.That(await secondModal.CompleteResultAsync("done"), Is.True);

                Assert.That(notifications, Is.EqualTo(new[] { "second:True" }));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(firstModal.IsVisible, Is.True);
                Assert.That(firstModal.CanvasGroup.interactable, Is.True);
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

        //计数型过渡：记录 Enter/Exit 次数，可门控挂起供中断时序断言
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
