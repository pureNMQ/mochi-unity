using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class GUINavigatorTests
    {
        [UnityTest]
        public IEnumerator InitializeAndClose_UseSerializedSinglePageContainer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject root = new GameObject("GUIRoot");
                try
                {
                    PageContainer container = new GameObject("PageContainer").AddComponent<PageContainer>();
                    container.transform.SetParent(root.transform, false);
                    GUINavigator navigator = root.AddComponent<GUINavigator>();
                    FieldInfo field = typeof(GUINavigator).GetField("pageContainer", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(field, Is.Not.Null);
                    field.SetValue(navigator, container);

                    GameObject prefab = new GameObject("PagePrefab");
                    prefab.SetActive(false);
                    prefab.AddComponent<CanvasGroup>();
                    prefab.AddComponent<TestPage>();
                    FakeLoader loader = new FakeLoader(prefab);

                    navigator.Initialize(loader);
                    TestPage page = await navigator.PageContainer.PushAsync<TestPage>("page");

                    Assert.That(page, Is.Not.Null);
                    Assert.That(navigator.PageContainer, Is.SameAs(container));
                    Assert.That(await navigator.BackAsync(), Is.False);
                    Assert.That(await navigator.CloseAsync(), Is.True);
                    Assert.That(loader.ReleaseCount, Is.EqualTo(1));

                    UnityEngine.Object.DestroyImmediate(prefab);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
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
