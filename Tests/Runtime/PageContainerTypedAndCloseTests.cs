using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    public class PageContainerTypedAndCloseTests
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
        public IEnumerator TypedPush_PreparesWithDataBeforeOpen()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject prefab = Track(CreateTypedPrefab("TypedPagePrefab"));
                RecordingLoader loader = new RecordingLoader(prefab);
                container.Initialize(loader);

                TypedPage page = await container.PushAsync<TypedPage, PageData>(
                    "typed-page",
                    new PageData("Page A", 7));

                Assert.That(page, Is.Not.Null);
                Assert.That(page.SourcePage, Is.EqualTo("Page A"));
                Assert.That(page.Counter, Is.EqualTo(7));
                Assert.That(page.Events, Is.EqualTo(new[] { "Prepare", "Open", "Show", "Enter" }));
            });
        }

        [UnityTest]
        public IEnumerator Close_ClosesWholeStackTopFirst()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPrefab"));
                GameObject thirdPrefab = Track(CreatePagePrefab("ThirdPrefab"));
                RecordingLoader loader = new RecordingLoader(firstPrefab, secondPrefab, thirdPrefab);
                container.Initialize(loader);

                ClosePage first = await container.PushAsync<ClosePage>("first");
                ClosePage second = await container.PushAsync<ClosePage>("second");
                ClosePage third = await container.PushAsync<ClosePage>("third");
                List<string> firstEvents = first.Events;
                List<string> secondEvents = second.Events;
                List<string> thirdEvents = third.Events;

                container.Close();

                Assert.That(container.StackCount, Is.Zero);
                Assert.That(container.Current, Is.Null);
                //等一帧确保延迟销毁生效；销毁顺序不保证，按等价集合断言
                await UniTask.Yield();
                Assert.That(loader.ReleasedPrefabs, Is.EquivalentTo(new[] { thirdPrefab, secondPrefab, firstPrefab }));
                //容器 Close 强杀清栈：顶层页从未被隐藏，关闭不播退场
                Assert.That(thirdEvents, Is.EqualTo(new[] { "Close" }));
                //下层页仅有 Push 时被隐藏的一次 Exit，关闭只补 OnClose
                Assert.That(firstEvents, Is.EqualTo(new[] { "Exit", "Close" }));
                Assert.That(secondEvents, Is.EqualTo(new[] { "Exit", "Close" }));
                container.Close();
                Assert.That(container.StackCount, Is.Zero);
                await UniTask.Yield();
                Assert.That(loader.ReleasedPrefabs.Count, Is.EqualTo(3));
            });
        }

        [UnityTest]
        public IEnumerator BackAsync_WhilePushIsBusyReturnsFalseWithoutChangingStack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PageContainer container = Track(new GameObject("PageContainer")).AddComponent<PageContainer>();
                GameObject firstPrefab = Track(CreatePagePrefab("FirstPrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPrefab"));
                DelayedSecondLoader loader = new DelayedSecondLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                await container.PushAsync<ClosePage>("first");
                UniTask<ClosePage> push = container.PushAsync<ClosePage>("second");
                await UniTask.Yield();

                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(container.StackCount, Is.EqualTo(1));

                loader.Complete();
                Assert.That(await push, Is.Not.Null);
                Assert.That(container.StackCount, Is.EqualTo(2));
            });
        }

        private GameObject CreateTypedPrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TypedPage>();
            return prefab;
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<ClosePage>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private readonly struct PageData
        {
            public PageData(string sourcePage, int counter)
            {
                SourcePage = sourcePage;
                Counter = counter;
            }

            public string SourcePage { get; }
            public int Counter { get; }
        }

        private sealed class TypedPage : Panel<PageData>, IPanelTransition
        {
            public List<string> Events { get; } = new List<string>();
            public string SourcePage { get; private set; }
            public int Counter { get; private set; }

            protected override UniTask OnPrepareAsync(PageData data, CancellationToken cancellationToken)
            {
                Events.Add("Prepare");
                SourcePage = data.SourcePage;
                Counter = data.Counter;
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Events.Add("Open");
            }

            protected override void OnShow()
            {
                Events.Add("Show");
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                Events.Add("Enter");
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                return UniTask.CompletedTask;
            }
        }

        private sealed class ClosePage : Panel, IPanelTransition
        {
            public List<string> Events { get; } = new List<string>();

            protected override void OnClose()
            {
                Events.Add("Close");
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                return UniTask.CompletedTask;
            }
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly GameObject[] prefabs;
            private int index;

            public RecordingLoader(params GameObject[] prefabs)
            {
                this.prefabs = prefabs;
            }

            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[index++]);
            }

            public void Release(GameObject prefab)
            {
                ReleasedPrefabs.Add(prefab);
            }
        }

        private sealed class DelayedSecondLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;
            private readonly UniTaskCompletionSource<GameObject> completion = new UniTaskCompletionSource<GameObject>();
            private int loadCount;

            public DelayedSecondLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                loadCount++;
                return loadCount == 1
                    ? UniTask.FromResult(firstPrefab)
                    : completion.Task;
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void Complete()
            {
                completion.TrySetResult(secondPrefab);
            }
        }
    }
}
