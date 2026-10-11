using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    //R4/F4 新契约用例：Back 确定关闭、不回滚——条目先移出栈（所有权转移）、
    //同步清理（实例销毁/资源释放）、异常仍传播、前页恢复交互/显示
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
        public IEnumerator Back_WhenPreviousOnShowThrows_DestroysFailedPageWithoutRollback()
        {
            return UniTask.ToCoroutine(() => RunBackFailureTest(FailureStage.AOnShow));
        }

        [UnityTest]
        public IEnumerator Back_WhenExitThrows_CommitsCloseAndLeavesErrorToTransitionComponent()
        {
            return UniTask.ToCoroutine(() => RunBackExitFailureTest());
        }

        [UnityTest]
        public IEnumerator Back_WhenOnHideThrows_DestroysFailedPageWithoutRollback()
        {
            return UniTask.ToCoroutine(() => RunBackFailureTest(FailureStage.BOnHide));
        }

        [UnityTest]
        public IEnumerator Back_WhenOnCloseThrows_DestroysFailedPageWithoutRollback()
        {
            return UniTask.ToCoroutine(() => RunBackFailureTest(FailureStage.BOnClose));
        }

        private async UniTask RunBackFailureTest(FailureStage failureStage)
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
            GameObject secondFocus = secondPage.transform.Find("AlternateFocus").gameObject;
            eventSystem.SetSelectedGameObject(secondFocus);

            Exception caught = null;
            try
            {
                await container.BackAsync();
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            //Destroy 延迟到帧末，等待一帧后断言失败页已被销毁
            await UniTask.Yield();

            //新契约：Back 确定关闭不回滚——生命周期异常类型与消息保留并继续向外传播
            Assert.That(caught, Is.TypeOf<BackFailureException>());
            Assert.That(caught.Message, Is.EqualTo(failureStage.ToString()));
            //Busy 复位；失败页条目已移出栈，仅剩前页
            Assert.That(container.IsBusy, Is.False);
            Assert.That(container.StackCount, Is.EqualTo(1));
            Assert.That(container.Current, Is.SameAs(firstPage));
            Assert.That(container.CanGoBack, Is.False);
            //失败页被销毁且 Prefab 引用恰好释放一次（不回滚、不重复 Release）
            Assert.That(secondPage == null, Is.True);
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));

            if (failureStage == FailureStage.AOnShow)
            {
                //前页 OnShow 抛错属生命周期契约破坏、容器不防御：
                //状态先于回调提交，显示与交互已生效，焦点已恢复到前页记忆
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(GetDefaultFocusTarget(firstPage)));
            }
            else
            {
                //前页恢复显示与交互，焦点显式恢复到前页自身焦点记忆
                Assert.That(firstPage.IsVisible, Is.True);
                Assert.That(firstPage.gameObject.activeSelf, Is.True);
                Assert.That(firstPage.CanvasGroup.interactable, Is.True);
                Assert.That(firstPage.CanvasGroup.blocksRaycasts, Is.True);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(GetDefaultFocusTarget(firstPage)));
            }
        }

        //新契约：退场过渡 fire-and-forget，Exit 抛错归组件——Close 已同步提交（自毁自释放），
        //Back 正常返回，错误经 UniTask 未观察通道记录日志
        private async UniTask RunBackExitFailureTest()
        {
            GameObject eventSystemObject = Track(new GameObject("EventSystem"));
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

            GameObject containerObject = Track(new GameObject("PageContainer"));
            PageContainer container = containerObject.AddComponent<PageContainer>();

            GameObject firstPrefab = Track(CreatePagePrefab("FirstPagePrefab", FailureStage.None));
            GameObject secondPrefab = Track(CreatePagePrefab("SecondPagePrefab", FailureStage.BExit));
            RollbackPanelLoader loader = new RollbackPanelLoader(firstPrefab, secondPrefab);
            container.Initialize(loader);

            BackRollbackPage firstPage = await container.PushAsync<BackRollbackPage>("first-page");
            BackRollbackPage secondPage = await container.PushAsync<BackRollbackPage>("second-page");
            GameObject secondFocus = secondPage.transform.Find("AlternateFocus").gameObject;
            eventSystem.SetSelectedGameObject(secondFocus);

            LogAssert.Expect(LogType.Exception, new Regex("BackFailureException: BExit"));
            Assert.That(await container.BackAsync(), Is.True);

            Assert.That(container.IsBusy, Is.False);
            Assert.That(container.StackCount, Is.EqualTo(1));
            Assert.That(container.Current, Is.SameAs(firstPage));
            //关闭已提交：实例销毁、句柄释放恰好一次
            await UniTask.Yield();
            Assert.That(secondPage == null, Is.True);
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            //前页恢复显示、交互与焦点
            Assert.That(firstPage.IsVisible, Is.True);
            Assert.That(firstPage.CanvasGroup.interactable, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(GetDefaultFocusTarget(firstPage)));
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

        private static void SetDefaultFocusTarget(BackRollbackPage page, GameObject target)
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

                throw new ArgumentException("Unexpected test key: " + key);
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
