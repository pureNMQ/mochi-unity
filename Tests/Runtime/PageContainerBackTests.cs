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
    public class PageContainerBackTests
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
        public IEnumerator BackAsync_ClosesTopPageReleasesPrefabAndRestoresPreviousFocus()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject eventSystemObject = Track(new GameObject("EventSystem"));
                EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab"));
                KeyedPanelLoader loader = new KeyedPanelLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                BackPage firstPage = await container.PushAsync<BackPage>("first-page");
                GameObject originalFocus = firstPage.transform.Find("AlternateFocus").gameObject;
                eventSystem.SetSelectedGameObject(originalFocus);

                BackPage secondPage = await container.PushAsync<BackPage>("second-page");
                GameObject secondFocus = GetDefaultFocusTarget(secondPage);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(secondFocus));
                Assert.That(container.CanGoBack, Is.True);
                List<string> secondPageLifecycle = secondPage.Events;

                bool backResult = await container.BackAsync();
                await UniTask.Yield();

                Assert.That(backResult, Is.True);
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(container.Count, Is.EqualTo(1));
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.Events, Does.Contain("OnShow"));
                Assert.That(secondPage == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefab, Is.SameAs(secondPrefab));
                Assert.That(secondPageLifecycle, Is.EqualTo(new[]
                {
                    "OnPrepare", "OnOpen", "OnShow", "Enter", "Exit", "OnHide", "OnClose"
                }));
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(originalFocus));

                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(container.Current, Is.SameAs(firstPage));
                Assert.That(container.Count, Is.EqualTo(1));
                Assert.That(container.CanGoBack, Is.False);
            });
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            BackPage page = prefab.AddComponent<BackPage>();

            GameObject defaultFocus = new GameObject("DefaultFocus");
            defaultFocus.transform.SetParent(prefab.transform, false);
            SetDefaultFocusTarget(page, defaultFocus);

            GameObject alternateFocus = new GameObject("AlternateFocus");
            alternateFocus.transform.SetParent(prefab.transform, false);
            return prefab;
        }

        private static void SetDefaultFocusTarget(BackPage page, GameObject target)
        {
            FieldInfo field = typeof(Panel).GetField("defaultFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(page, target);
        }

        private static GameObject GetDefaultFocusTarget(Panel page)
        {
            FieldInfo field = typeof(Panel).GetField("defaultFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (GameObject)field.GetValue(page);
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class KeyedPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;

            public KeyedPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public int ReleaseCount { get; private set; }
            public GameObject ReleasedPrefab { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first-page")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "second-page")
                {
                    return UniTask.FromResult(secondPrefab);
                }

                throw new ArgumentException($"Unexpected test key: {key}");
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
                ReleasedPrefab = loadedPrefab;
            }
        }

        private sealed class BackPage : Panel, IPanelTransition
        {
            public List<string> Events { get; } = new List<string>();

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                Events.Add("OnPrepare");
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
