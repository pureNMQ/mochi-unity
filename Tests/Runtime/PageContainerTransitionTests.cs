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
    public class PageContainerTransitionTests
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
        public IEnumerator PushSecondPage_EntersOnlyNewPage_BlocksPreviousInteractionAndFocusesDefaultTarget()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject eventSystemObject = Track(new GameObject("EventSystem"));
                EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab"));
                GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab"));
                GameObject defaultFocusTarget = new GameObject("SecondPageDefaultFocus");
                defaultFocusTarget.transform.SetParent(secondPrefab.transform, false);
                SetDefaultFocusTarget(secondPrefab.GetComponent<TransitionPage>(), defaultFocusTarget);

                KeyedPanelLoader loader = new KeyedPanelLoader(firstPrefab, secondPrefab);
                container.Initialize(loader);

                TransitionPage firstPage = await container.PushAsync<TransitionPage>("first-page");
                UniTask<TransitionPage> secondPush = container.PushAsync<TransitionPage>("second-page");

                await UniTask.Yield();

                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.False);
                Assert.That(firstPage.CanvasGroup.blocksRaycasts, Is.False);
                Assert.That(firstPage.ExitCount, Is.Zero);

                loader.CompleteSecondPage();
                TransitionPage secondPage = await secondPush;

                Assert.That(secondPage, Is.Not.Null);
                Assert.That(secondPage.EnterCount, Is.EqualTo(1));
                Assert.That(secondPage.ExitCount, Is.Zero);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(GetDefaultFocusTarget(secondPage)));
            });
        }

        private GameObject CreatePagePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TransitionPage>();
            return prefab;
        }

        private static void SetDefaultFocusTarget(TransitionPage page, GameObject target)
        {
            FieldInfo field = typeof(Panel).GetField("defaultFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Panel should expose a serialized default focus target.");
            field.SetValue(page, target);
        }

        private static GameObject GetDefaultFocusTarget(Panel page)
        {
            FieldInfo field = typeof(Panel).GetField("defaultFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Panel should expose a serialized default focus target.");
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
            private readonly UniTaskCompletionSource<GameObject> secondCompletionSource =
                new UniTaskCompletionSource<GameObject>();

            public KeyedPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first-page")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "second-page")
                {
                    return secondCompletionSource.Task;
                }

                throw new ArgumentException($"Unexpected test key: {key}");
            }

            public void Release(GameObject loadedPrefab)
            {
            }

            public void CompleteSecondPage()
            {
                secondCompletionSource.TrySetResult(secondPrefab);
            }
        }

        private sealed class TransitionPage : Panel, IPanelTransition
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }
        }
    }
}
