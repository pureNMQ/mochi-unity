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
    public class PageContainerBackRollbackTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                BackRollbackPage[] pages = target.GetComponentsInChildren<BackRollbackPage>(true);
                foreach (BackRollbackPage page in pages)
                {
                    page.Failure = FailureStage.None;
                }

                UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [UnityTest]
        public IEnumerator Back_WhenPreviousOnShowThrows_RestoresTopPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunBackRollbackTest(FailureStage.AOnShow));
        }

        [UnityTest]
        public IEnumerator Back_WhenExitThrows_RestoresTopPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunBackRollbackTest(FailureStage.BExit));
        }

        [UnityTest]
        public IEnumerator Back_WhenOnHideThrows_RestoresTopPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunBackRollbackTest(FailureStage.BOnHide));
        }

        [UnityTest]
        public IEnumerator Back_WhenOnCloseThrows_RestoresTopPageAndPropagatesOriginalException()
        {
            return UniTask.ToCoroutine(() => RunBackRollbackTest(FailureStage.BOnClose));
        }

        private async UniTask RunBackRollbackTest(FailureStage failureStage)
        {
            GameObject eventSystemObject = Track(new GameObject("EventSystem"));
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

            GameObject containerObject = Track(new GameObject("PageContainer"));
            PageContainer container = containerObject.AddComponent<PageContainer>();

            GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab", failureStage == FailureStage.AOnShow ? failureStage : FailureStage.None));
            GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab", failureStage));
            RollbackPanelLoader loader = new RollbackPanelLoader(firstPrefab, secondPrefab);
            container.Initialize(loader);

            BackRollbackPage firstPage = await container.PushAsync<BackRollbackPage>("first-page");
            BackRollbackPage secondPage = await container.PushAsync<BackRollbackPage>("second-page");
            GameObject originalFocus = secondPage.transform.Find("AlternateFocus").gameObject;
            eventSystem.SetSelectedGameObject(originalFocus);
            List<string> secondPageEvents = secondPage.Events;

            Exception caught = null;
            try
            {
                await container.BackAsync();
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            await UniTask.Yield();

            Assert.That(caught, Is.TypeOf<BackFailureException>());
            Assert.That(caught.Message, Is.EqualTo(failureStage.ToString()));
            Assert.That(container.IsBusy, Is.False);
            Assert.That(container.Current, Is.SameAs(secondPage));
            Assert.That(container.Count, Is.EqualTo(2));
            Assert.That(container.CanGoBack, Is.True);
            Assert.That(firstPage.IsVisible, Is.False);
            Assert.That(firstPage.gameObject.activeSelf, Is.False);
            Assert.That(firstPage.CanvasGroup.interactable, Is.False);
            Assert.That(firstPage.CanvasGroup.blocksRaycasts, Is.False);
            Assert.That(secondPage.IsOpen, Is.True);
            Assert.That(secondPage.IsVisible, Is.True);
            Assert.That(secondPage.gameObject.activeSelf, Is.True);
            Assert.That(secondPage.CanvasGroup.interactable, Is.True);
            Assert.That(secondPage.CanvasGroup.blocksRaycasts, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(originalFocus));
            Assert.That(loader.ReleaseCount, Is.Zero);
            Assert.That(secondPageEvents, Is.EqualTo(ExpectedSecondPageEvents(failureStage)));
        }

        private GameObject CreatePagePrefab(string name, FailureStage failureStage)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            BackRollbackPage page = prefab.AddComponent<BackRollbackPage>();
            page.Failure = failureStage;

            GameObject defaultFocus = new GameObject("DefaultFocus");
            defaultFocus.transform.SetParent(prefab.transform, false);
            SetDefaultFocusTarget(page, defaultFocus);

            GameObject alternateFocus = new GameObject("AlternateFocus");
            alternateFocus.transform.SetParent(prefab.transform, false);
            return prefab;
        }

        private static string[] ExpectedSecondPageEvents(FailureStage failureStage)
        {
            switch (failureStage)
            {
                case FailureStage.AOnShow:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "Enter" };
                case FailureStage.BExit:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "Enter", "Exit" };
                case FailureStage.BOnHide:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "Enter", "Exit", "OnHide" };
                case FailureStage.BOnClose:
                    return new[] { "OnPrepare", "OnOpen", "OnShow", "Enter", "Exit", "OnHide", "OnClose", "OnShow" };
                default:
                    throw new ArgumentOutOfRangeException(nameof(failureStage), failureStage, null);
            }
        }

        private static void SetDefaultFocusTarget(BackRollbackPage page, GameObject target)
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
            AOnShow,
            BExit,
            BOnHide,
            BOnClose
        }

        private sealed class BackFailureException : Exception
        {
            public BackFailureException(FailureStage stage)
                : base(stage.ToString())
            {
            }
        }

        private sealed class RollbackPanelLoader : IPanelLoader
        {
            private readonly GameObject firstPrefab;
            private readonly GameObject secondPrefab;

            public RollbackPanelLoader(GameObject firstPrefab, GameObject secondPrefab)
            {
                this.firstPrefab = firstPrefab;
                this.secondPrefab = secondPrefab;
            }

            public int ReleaseCount { get; private set; }

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
            }
        }

        private sealed class BackRollbackPage : Panel, IPanelTransition
        {
            [SerializeField] private FailureStage failure;
            private int showCount;

            public List<string> Events { get; } = new List<string>();
            public FailureStage Failure
            {
                get => failure;
                set => failure = value;
            }

            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

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
                showCount++;
                if (Failure == FailureStage.AOnShow && showCount > 1)
                {
                    throw new BackFailureException(FailureStage.AOnShow);
                }
            }

            protected override void OnHide()
            {
                Events.Add("OnHide");
                if (Failure == FailureStage.BOnHide)
                {
                    throw new BackFailureException(FailureStage.BOnHide);
                }
            }

            protected override void OnClose()
            {
                Events.Add("OnClose");
                if (Failure == FailureStage.BOnClose)
                {
                    throw new BackFailureException(FailureStage.BOnClose);
                }
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                Events.Add("Enter");
                return UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                Events.Add("Exit");
                if (Failure == FailureStage.BExit)
                {
                    throw new BackFailureException(FailureStage.BExit);
                }

                return UniTask.CompletedTask;
            }
        }
    }
}
