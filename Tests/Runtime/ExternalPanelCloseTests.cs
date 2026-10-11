using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    //面板未经容器自行关闭（外部直接 Close）用例：容器经 Closed 事件在其后清理——
    //与容器驱动路径（先清理后关闭）互为倒序；栈容器只抽出被关闭条目，其余条目顺序不变
    public class ExternalPanelCloseTests
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
        public IEnumerator Page_MidStackSelfClose_ExtractsOnlyThatEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("first", CreatePrefab("FirstPrefab"));
                loader.Register("second", CreatePrefab("SecondPrefab"));
                loader.Register("third", CreatePrefab("ThirdPrefab"));
                container.Initialize(loader);

                PlainPage first = await container.PushAsync<PlainPage>("first");
                PlainPage second = await container.PushAsync<PlainPage>("second");
                PlainPage third = await container.PushAsync<PlainPage>("third");

                //中段页面自行关闭：仅抽出该条目，上方条目原样保留、顺序不变
                second.Close();

                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(container.Current, Is.SameAs(third));
                Assert.That(third.IsVisible, Is.True);
                Assert.That(third.CanvasGroup.interactable, Is.True);
                Assert.That(first.IsVisible, Is.False);

                //延迟自毁完成后释放句柄，其余条目不受影响
                await UniTask.Yield();
                Assert.That(second == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));

                //导航继续正常：从新栈顶返回
                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.Current, Is.SameAs(first));
            });
        }

        [UnityTest]
        public IEnumerator Page_TopSelfClose_RevealsNewTop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject eventSystemObject = Track(new GameObject("EventSystem"));
                eventSystemObject.AddComponent<EventSystem>();
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("first", CreatePrefab("FirstPrefab"));
                loader.Register("second", CreatePrefab("SecondPrefab"));
                container.Initialize(loader);

                PlainPage first = await container.PushAsync<PlainPage>("first");
                PlainPage second = await container.PushAsync<PlainPage>("second");

                //栈顶自行关闭：容器恢复新顶显示（显示即启用输入与焦点）
                second.Close();

                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(first.IsVisible, Is.True);
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.True);

                await UniTask.Yield();
                Assert.That(second == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Page_SelfCloseDuringHiddenPeriod_RestoresNewTopLogically()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("first", CreatePrefab("FirstPrefab"));
                loader.Register("second", CreatePrefab("SecondPrefab"));
                container.Initialize(loader);
                container.Show();

                PlainPage first = await container.PushAsync<PlainPage>("first");
                container.Hide();
                PlainPage second = await container.PushAsync<PlainPage>("second");

                //隐藏期自行关闭：条目抽出，新顶恢复逻辑显示（视觉随容器整树）
                second.Close();

                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(first.IsVisible, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.True);

                container.Show();
                Assert.That(first.IsVisible, Is.True);
                Assert.That(first.gameObject.activeInHierarchy, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Modal_TopSelfClose_NotifiesNoResultAndRestoresNextTop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject eventSystemObject = Track(new GameObject("EventSystem"));
                eventSystemObject.AddComponent<EventSystem>();
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("first", CreatePrefab("FirstModalPrefab"));
                loader.Register("second", CreatePrefab("SecondModalPrefab"));
                container.Initialize(loader);
                SetBackdropDuration(container, 0f);

                List<string> notifications = new List<string>();
                PlainModal first = await container.PushAsync<PlainModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add($"first:{hasResult}")));
                PlainModal second = await container.PushAsync<PlainModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add($"second:{hasResult}")));

                //栈顶 Modal 自行关闭：外部中止按无结果交付，剩余栈顶恢复显示与输入
                second.Close();

                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(notifications, Is.EqualTo(new[] { "second:False" }));
                Assert.That(first.IsVisible, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.True);
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                await UniTask.Yield();
                Assert.That(second == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));

                //最后一个 Modal 也自行关闭：通知无结果，Backdrop 复位
                first.Close();
                Assert.That(container.StackCount, Is.EqualTo(0));
                Assert.That(notifications, Is.EqualTo(new[] { "second:False", "first:False" }));
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(0f));
                Assert.That(GetBackdropRaycasts(container), Is.False);
            });
        }

        [UnityTest]
        public IEnumerator Modal_MidStackSelfClose_ExtractsOnlyThatEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = Track(new GameObject("ModalContainer")).AddComponent<ModalContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("first", CreatePrefab("FirstModalPrefab"));
                loader.Register("second", CreatePrefab("SecondModalPrefab"));
                loader.Register("third", CreatePrefab("ThirdModalPrefab"));
                container.Initialize(loader);
                SetBackdropDuration(container, 0f);

                List<string> notifications = new List<string>();
                PlainModal first = await container.PushAsync<PlainModal, string>(
                    "first",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add($"first:{hasResult}")));
                PlainModal second = await container.PushAsync<PlainModal, string>(
                    "second",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add($"second:{hasResult}")));
                PlainModal third = await container.PushAsync<PlainModal, string>(
                    "third",
                    new ModalResultCallback<string>((hasResult, result) => notifications.Add($"third:{hasResult}")));

                //中段 Modal 自行关闭：仅抽出该条目；栈顶保持可见可交互，Backdrop 不动
                second.Close();

                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(container.Current, Is.SameAs(third));
                Assert.That(notifications, Is.EqualTo(new[] { "second:False" }));
                Assert.That(third.IsVisible, Is.True);
                Assert.That(third.CanvasGroup.interactable, Is.True);
                Assert.That(GetBackdropAlpha(container), Is.EqualTo(1f));

                await UniTask.Yield();
                Assert.That(second == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        private GameObject CreatePrefab(string name)
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<PlainPage>();
            prefab.AddComponent<PlainModal>();
            return prefab;
        }

        private static void SetBackdropDuration(ModalContainer container, float duration)
        {
            typeof(ModalContainer)
                .GetField("backdropFadeDuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
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

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public int ReleaseCount { get; private set; }

            public void Register(string key, GameObject prefab)
            {
                prefabs[key] = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[key]);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        //同一预制体同时挂 Page 与 Modal 组件：GetComponent<TPanel> 按类型定位，互不干扰
        private sealed class PlainPage : Panel
        {
        }

        private sealed class PlainModal : ModalPanel<string>
        {
        }
    }
}
