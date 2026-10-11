using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI.Tests
{
    public class GUINavigatorTests
    {
        [UnityTest]
        public IEnumerator Initialize_CreatesRuntimePageAndModalContainers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject root = new GameObject("GUIRoot");
                GameObject prefab = null;
                try
                {
                    GUINavigator navigator = root.AddComponent<GUINavigator>();
                    PageContainer container = navigator.Pages;
                    ModalContainer modalContainer = navigator.Modals;
                    Canvas canvas = root.GetComponentInChildren<Canvas>();

                    Assert.That(GUINavigator.Instance, Is.SameAs(navigator));
                    Assert.That(canvas, Is.Not.Null);
                    Assert.That(canvas.transform.parent, Is.SameAs(root.transform));
                    Assert.That(container, Is.Not.Null);
                    Assert.That(container.transform.parent, Is.SameAs(canvas.transform));
                    Assert.That(modalContainer, Is.Not.Null);
                    Assert.That(modalContainer.transform.parent, Is.SameAs(canvas.transform));

                    prefab = new GameObject("PagePrefab");
                    prefab.SetActive(false);
                    prefab.AddComponent<CanvasGroup>();
                    prefab.AddComponent<TestPage>();
                    FakeLoader loader = new FakeLoader(prefab);

                    navigator.Initialize(loader);
                    TestPage page = await navigator.Pages.PushAsync<TestPage>("page");

                    Assert.That(page, Is.Not.Null);
                    Assert.That(navigator.Pages, Is.SameAs(container));
                    Assert.That(navigator.Modals, Is.SameAs(modalContainer));
                    navigator.Pages.Close();
                    //等一帧确保延迟销毁生效
                    await UniTask.Yield();
                    Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(prefab);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            });
        }

        [UnityTest]
        public IEnumerator DuplicateNavigator_DoesNotReplaceSingleton()
        {
            GameObject firstRoot = new GameObject("FirstGUIRoot");
            GameObject duplicateRoot = new GameObject("DuplicateGUIRoot");
            try
            {
                GUINavigator first = firstRoot.AddComponent<GUINavigator>();
                GUINavigator duplicate = duplicateRoot.AddComponent<GUINavigator>();

                Assert.That(GUINavigator.Instance, Is.SameAs(first));
                Assert.That(first.Pages, Is.Not.Null);
                Assert.That(duplicate.Pages, Is.Null);

                yield return null;

                Assert.That(duplicateRoot == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstRoot);
                UnityEngine.Object.DestroyImmediate(duplicateRoot);
            }
        }

        [UnityTest]
        public IEnumerator LazyCreation_EventSystemSurvivesOwnerSceneUnload()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GUINavigator leftoverNavigator = Object.FindFirstObjectByType<GUINavigator>();
                if (leftoverNavigator != null)
                {
                    Object.DestroyImmediate(leftoverNavigator.gameObject);
                }

                foreach (EventSystem leftoverEventSystem in Object.FindObjectsByType<EventSystem>(
                             FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(leftoverEventSystem.gameObject);
                }

                Scene ownerScene = SceneManager.CreateScene("LazyEventSystemOwnerScene");
                SceneManager.SetActiveScene(ownerScene);
                try
                {
                    _ = GUINavigator.Instance;
                    EventSystem eventSystem = EventSystem.current;
                    Assert.That(eventSystem, Is.Not.Null);

                    AsyncOperation unload = SceneManager.UnloadSceneAsync(ownerScene);
                    while (!unload.isDone)
                    {
                        await UniTask.Yield();
                    }

                    await UniTask.Yield();
                    //兜底 EventSystem 若留在创建时刻的 Active Scene，会随该场景卸载销毁，
                    //此后 GUINavigator 仍在但 UI 输入静默失效
                    Assert.That(eventSystem != null, Is.True);
                    Assert.That(EventSystem.current != null, Is.True);
                }
                finally
                {
                    if (ownerScene.isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(ownerScene);
                    }

                    GUINavigator lazyNavigator = Object.FindFirstObjectByType<GUINavigator>();
                    if (lazyNavigator != null)
                    {
                        Object.DestroyImmediate(lazyNavigator.gameObject);
                    }

                    EventSystem lazyEventSystem = Object.FindFirstObjectByType<EventSystem>();
                    if (lazyEventSystem != null)
                    {
                        Object.DestroyImmediate(lazyEventSystem.gameObject);
                    }
                }
            });
        }

        private sealed class FakeLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public FakeLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        private sealed class TestPage : Panel
        {
        }
    }
}
