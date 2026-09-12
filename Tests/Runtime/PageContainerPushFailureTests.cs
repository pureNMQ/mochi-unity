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
    /// <summary>
    /// F1/R2 契约用例：committed 之前的各失败点统一经唯一 finally 收尾，
    /// 本次实际取得的资源恰好释放一次，前页的可见性、交互与焦点得到恢复。
    /// </summary>
    public class PageContainerPushFailureTests
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
        public IEnumerator Push_SecondPage_WhenPrepareCanceled_PropagatesAndReleasesOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject eventSystemObject = Track(new GameObject("EventSystem"));
                EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(CreateFocusPagePrefab("FirstPagePrefab", false));
                GameObject failingPrefab = Track(CreateFocusPagePrefab("FailingPagePrefab", true));
                CountingPanelLoader loader = new CountingPanelLoader();
                loader.Register("first-page", firstPrefab);
                loader.Register("failing-page", failingPrefab);
                container.Initialize(loader);

                FocusPage firstPage = await container.PushAsync<FocusPage>("first-page");
                GameObject previousFocus = firstPage.transform.Find("AlternateFocus").gameObject;
                eventSystem.SetSelectedGameObject(previousFocus);

                Exception caught = null;
                try
                {
                    await container.PushAsync<FocusPage>("failing-page");
                }
                catch (Exception exception)
                {
                    caught = exception;
                }

                await UniTask.Yield();

                Assert.That(caught, Is.TypeOf<OperationCanceledException>());
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
                //失败收尾显式恢复前页焦点（仍可见前页的 ShowInternal 为空操作，需补偿）
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(previousFocus));
                Assert.That(container.transform.childCount, Is.EqualTo(1));
                //取消发生在加载成功之后：本次取得的加载引用恰好释放一次
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { failingPrefab }));
            });
        }

        [UnityTest]
        public IEnumerator Push_WhenLoaderReturnsNullAfterDisablingPreviousPage_RestoresPreviousPage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(CreateFocusPagePrefab("FirstPagePrefab", false));
                CountingPanelLoader loader = new CountingPanelLoader();
                loader.Register("first-page", firstPrefab);
                loader.Register("missing-page", null);
                container.Initialize(loader);

                FocusPage firstPage = await container.PushAsync<FocusPage>("first-page");

                FocusPage failedPage = await container.PushAsync<FocusPage>("missing-page");

                Assert.That(failedPage, Is.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(container.StackCount, Is.EqualTo(1));
                //加载失败时不存在可释放的成功加载引用
                Assert.That(loader.ReleaseCount, Is.EqualTo(0));
                //前页在加载期间被禁用交互，失败收尾必须恢复
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Push_WhenPrefabLacksPageComponent_ReleasesExactlyOnceAndRestoresPreviousPage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(CreateFocusPagePrefab("FirstPagePrefab", false));
                GameObject invalidPrefab = Track(new GameObject("InvalidPagePrefab"));
                invalidPrefab.SetActive(false);
                CountingPanelLoader loader = new CountingPanelLoader();
                loader.Register("first-page", firstPrefab);
                loader.Register("invalid-page", invalidPrefab);
                container.Initialize(loader);

                FocusPage firstPage = await container.PushAsync<FocusPage>("first-page");

                FocusPage failedPage = await container.PushAsync<FocusPage>("invalid-page");

                await UniTask.Yield();

                Assert.That(failedPage, Is.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(container.StackCount, Is.EqualTo(1));
                //统一收尾对本次实例恰好释放一次，不再由检测分支提前释放
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { invalidPrefab }));
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
                Assert.That(container.transform.childCount, Is.EqualTo(1));
            });
        }

        private GameObject CreateFocusPagePrefab(string name, bool cancelOnPrepare)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            FocusPage page = prefab.AddComponent<FocusPage>();
            page.CancelOnPrepare = cancelOnPrepare;

            GameObject defaultFocus = new GameObject("DefaultFocus");
            defaultFocus.transform.SetParent(prefab.transform, false);
            SetDefaultFocusTarget(page, defaultFocus);

            GameObject alternateFocus = new GameObject("AlternateFocus");
            alternateFocus.transform.SetParent(prefab.transform, false);
            return prefab;
        }

        private static void SetDefaultFocusTarget(FocusPage page, GameObject target)
        {
            FieldInfo field = typeof(Panel).GetField("defaultFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(page, target);
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class CountingPanelLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabsByKey =
                new Dictionary<string, GameObject>();

            public int ReleaseCount { get; private set; }
            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public void Register(string key, GameObject prefab)
            {
                prefabsByKey[key] = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(
                    prefabsByKey.TryGetValue(key, out GameObject prefab) ? prefab : null);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
                ReleasedPrefabs.Add(loadedPrefab);
            }
        }

        private sealed class FocusPage : Panel, IPanelTransition
        {
            //序列化字段：Instantiate 只拷贝序列化状态，开关必须经它传递到实例
            [SerializeField] private bool cancelOnPrepare;

            public bool CancelOnPrepare
            {
                get => cancelOnPrepare;
                set => cancelOnPrepare = value;
            }

            public List<string> Events { get; } = new List<string>();

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                Events.Add("OnPrepare");
                if (CancelOnPrepare)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Events.Add("OnOpen");
            }

            protected override void OnShow()
            {
                Events.Add("OnShow");
            }

            protected override void OnHide()
            {
                Events.Add("OnHide");
            }

            protected override void OnClose()
            {
                Events.Add("OnClose");
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
    }
}
