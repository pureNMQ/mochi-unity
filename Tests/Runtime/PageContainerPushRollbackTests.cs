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
    public class PageContainerPushRollbackTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            ThrowingPage.LastCreated = null;
            foreach (GameObject target in objectsToDestroy)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [UnityTest]
        public IEnumerator Push_WhenOnOpenThrows_RestoresPreviousPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunPushRollbackTest(FailureStage.OnOpen));
        }

        [UnityTest]
        public IEnumerator Push_WhenOnShowThrows_RestoresPreviousPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunPushRollbackTest(FailureStage.OnShow));
        }

        [UnityTest]
        public IEnumerator Push_WhenEnterThrows_RestoresPreviousPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunPushRollbackTest(FailureStage.Enter));
        }

        private async UniTask RunPushRollbackTest(FailureStage failureStage)
        {
            GameObject eventSystemObject = Track(new GameObject("EventSystem"));
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

            GameObject containerObject = Track(new GameObject("PageContainer"));
            PageContainer container = containerObject.AddComponent<PageContainer>();

            GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab", FailureStage.None));
            GameObject failingPrefab = Track(CreatePagePrefab("FailingPagePrefab", failureStage));
            RollbackPanelLoader loader = new RollbackPanelLoader(firstPrefab, failingPrefab);
            container.Initialize(loader);

            ThrowingPage firstPage = await container.PushAsync<ThrowingPage>("first-page");
            GameObject originalFocus = firstPage.DefaultFocusTarget;
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(originalFocus));

            Exception caught = null;
            try
            {
                await container.PushAsync<ThrowingPage>("failing-page");
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            ThrowingPage failedPage = ThrowingPage.LastCreated;
            List<string> failedPageEvents = failedPage == null ? null : failedPage.Events;
            await UniTask.Yield();

            Assert.That(caught, Is.TypeOf<PushFailureException>());
            Assert.That(caught.Message, Is.EqualTo(failureStage.ToString()));
            Assert.That(container.IsBusy, Is.False);
            Assert.That(container.Current, Is.SameAs(firstPage));
            Assert.That(container.Count, Is.EqualTo(1));
            Assert.That(firstPage.IsVisible, Is.True);
            Assert.That(firstPage.gameObject.activeSelf, Is.True);
            Assert.That(firstPage.CanvasGroup.interactable, Is.True);
            Assert.That(firstPage.CanvasGroup.blocksRaycasts, Is.True);
            Assert.That(firstPage.Events[firstPage.Events.Count - 1], Is.EqualTo("OnShow"));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(originalFocus));
            Assert.That(container.transform.childCount, Is.EqualTo(1));
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.ReleasedPrefab, Is.SameAs(failingPrefab));
            Assert.That(failedPageEvents, Is.Not.Null);
            Assert.That(failedPageEvents, Is.EqualTo(ExpectedFailedPageEvents(failureStage)));
        }

        private static string[] ExpectedFailedPageEvents(FailureStage failureStage)
        {
            switch (failureStage)
            {
                case FailureStage.OnOpen:
                    return new[] { "OnPrepare", "OnOpen" };
                case FailureStage.OnShow:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "OnClose" };
                case FailureStage.Enter:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "Enter", "OnHide", "OnClose" };
                default:
                    throw new ArgumentOutOfRangeException(nameof(failureStage), failureStage, null);
            }
        }

        private GameObject CreatePagePrefab(string name, FailureStage failureStage)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            ThrowingPage page = prefab.AddComponent<ThrowingPage>();
            page.Failure = failureStage;

            GameObject defaultFocus = new GameObject("DefaultFocus");
            defaultFocus.transform.SetParent(prefab.transform, false);
            SetDefaultFocusTarget(page, defaultFocus);
            return prefab;
        }

        private static void SetDefaultFocusTarget(ThrowingPage page, GameObject target)
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

        private enum FailureStage
        {
            None,
            OnOpen,
            OnShow,
            Enter
        }

        private sealed class PushFailureException : Exception
        {
            public PushFailureException(FailureStage stage)
                : base(stage.ToString())
            {
            }
        }

        private sealed class RollbackPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject failingPrefab;

            public RollbackPanelLoader(GameObject firstPrefab, GameObject failingPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.failingPrefab = failingPrefab;
            }

            public int ReleaseCount { get; private set; }
            public GameObject ReleasedPrefab { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                if (key == "first-page")
                {
                    return UniTask.FromResult(firstPrefab);
                }

                if (key == "failing-page")
                {
                    return UniTask.FromResult(failingPrefab);
                }

                throw new ArgumentException($"Unexpected test key: {key}");
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
                ReleasedPrefab = loadedPrefab;
            }
        }

        private sealed class ThrowingPage : Panel, IPanelTransition
        {
            [SerializeField] private FailureStage failure;

            public static ThrowingPage LastCreated { get; set; }
            public List<string> Events { get; } = new List<string>();
            public FailureStage Failure
            {
                get => failure;
                set => failure = value;
            }

            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                LastCreated = this;
                Events.Add("OnPrepare");
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Events.Add("OnOpen");
                ThrowIf(FailureStage.OnOpen);
            }

            protected override void OnShow()
            {
                Events.Add("OnShow");
                ThrowIf(FailureStage.OnShow);
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
                ThrowIf(FailureStage.Enter);
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                return UniTask.CompletedTask;
            }

            private void ThrowIf(FailureStage stage)
            {
                if (Failure == stage)
                {
                    throw new PushFailureException(stage);
                }
            }
        }
    }
}
