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
                secondPrefab.GetComponent<TransitionPage>().HoldEnter();
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
                await UniTask.Yield();
                TransitionPage[] childPages = container.GetComponentsInChildren<TransitionPage>(true);
                TransitionPage enteringPage = Array.Find(childPages, candidate => candidate != firstPage);
                Assert.That(enteringPage, Is.Not.Null);
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(enteringPage.IsVisible, Is.True);
                Assert.That(enteringPage.CanvasGroup.interactable, Is.False);
                Assert.That(enteringPage.CanvasGroup.blocksRaycasts, Is.False);

                enteringPage.CompleteEnter();
                TransitionPage secondPage = await secondPush;

                Assert.That(secondPage, Is.Not.Null);
                Assert.That(secondPage.EnterCount, Is.EqualTo(1));
                Assert.That(secondPage.ExitCount, Is.Zero);
                Assert.That(firstPage.IsVisible, Is.False);
                Assert.That(secondPage.CanvasGroup.interactable, Is.True);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(GetDefaultFocusTarget(secondPage)));
            });
        }

        [UnityTest]
        public IEnumerator AttachedTransitionTakesPrecedenceWhenPanelAlsoImplementsTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("PageContainer"));
                PageContainer container = containerObject.AddComponent<PageContainer>();

                GameObject prefab = Track(new GameObject("ConflictPagePrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<CanvasGroup>();
                prefab.AddComponent<ConflictPage>();
                prefab.AddComponent<AttachedTransition>();

                container.Initialize(new SinglePanelLoader(prefab));

                ConflictPage page = await container.PushAsync<ConflictPage>("conflict-page");
                AttachedTransition transition = page.GetComponent<AttachedTransition>();

                Assert.That(page.PanelEnterCount, Is.Zero);
                Assert.That(transition.EnterCount, Is.EqualTo(1));

                Assert.That(await container.CloseAsync(), Is.True);
                Assert.That(page.PanelExitCount, Is.Zero);
                Assert.That(transition.ExitCount, Is.EqualTo(1));
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

        private sealed class SinglePanelLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public SinglePanelLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
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
            private readonly UniTaskCompletionSource enterCompletion = new UniTaskCompletionSource();
            [SerializeField] private bool holdEnter;

            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                return holdEnter ? enterCompletion.Task : UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }

            public void CompleteEnter()
            {
                enterCompletion.TrySetResult();
            }

            public void HoldEnter()
            {
                holdEnter = true;
            }
        }

        private sealed class ConflictPage : Panel, IPanelTransition
        {
            public int PanelEnterCount { get; private set; }
            public int PanelExitCount { get; private set; }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                PanelEnterCount++;
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                PanelExitCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class AttachedTransition : MonoBehaviour, IPanelTransition
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }

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
