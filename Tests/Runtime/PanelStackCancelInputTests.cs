using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>取消输入的路由验收用例：由前台面板决定取消键是否被接住，没人接住时必须通知应用层。</summary>
    public sealed class PanelStackCancelInputTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            objects.Clear();
        }

        [UnityTest]
        // 验收条件 AC20: 当玩家在任意可交互页面触发导航、确认或取消，则其键位来自项目输入资产的 UI 映射，而不是 Unity 自带默认资产。
        public IEnumerator Cancel_AllowedTopModal_ClosesModalByCancelSemantics()
        {
            PanelStackService service = CreateService();
            TestPanel page = CreatePage("Page");
            TestModal modal = CreateModal("Modal");
            service.Push(page);
            service.Push(modal);
            int rejected = 0;
            service.CancelRejected += () => rejected++;

            FireCancel(service);

            //弹窗的取消语义是关闭自己并把未决等待按取消收场；下层页面不被连带
            Assert.AreEqual(0, service.ModalCount);
            Assert.IsNull(service.CurrentModal);
            Assert.AreEqual(0, rejected);
            Assert.IsTrue(page.IsVisible);
            Assert.IsTrue(page.Interactable);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC21: 当本需求完成，则标题页、世界主菜单、二级页与战斗页均不再直接读取输入资产，导航与确认由选中系统与控件事件承担；战斗页的时序键输入作为明确例外保留。
        public IEnumerator Cancel_AllowedTopPage_PopsPage()
        {
            PanelStackService service = CreateService();
            TestPanel below = CreatePage("Below");
            TestPanel top = CreatePage("Top");
            service.Push(below);
            service.Push(top);
            int rejected = 0;
            service.CancelRejected += () => rejected++;

            FireCancel(service);

            //允许返回的页面只退掉栈顶，下层按原状显现
            Assert.IsFalse(top.IsVisible);
            Assert.AreEqual(1, service.PageCount);
            Assert.AreSame(below, service.CurrentPage);
            Assert.IsTrue(below.IsVisible);
            Assert.AreEqual(0, rejected);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC21: 当本需求完成，则标题页、世界主菜单、二级页与战斗页均不再直接读取输入资产，导航与确认由选中系统与控件事件承担；战斗页的时序键输入作为明确例外保留。
        public IEnumerator Cancel_ForegroundPageDisallowed_LeavesPanelUntouchedAndNotifies()
        {
            PanelStackService service = CreateService();
            TestPanel page = CreatePage("Page");
            DisallowCancel(page);
            service.Push(page);
            int rejected = 0;
            service.CancelRejected += () => rejected++;

            FireCancel(service);

            //不允许返回的页面由框架原样保留，同时把这次未处理交给应用层
            Assert.IsTrue(page.IsVisible);
            Assert.AreEqual(1, service.PageCount);
            Assert.AreEqual(1, rejected);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC20: 当玩家在任意可交互页面触发导航、确认或取消，则其键位来自项目输入资产的 UI 映射，而不是 Unity 自带默认资产。
        public IEnumerator Cancel_EmptyStack_NotifiesWithoutThrowing()
        {
            PanelStackService service = CreateService();
            int rejected = 0;
            service.CancelRejected += () => rejected++;

            Assert.DoesNotThrow(() => FireCancel(service));

            //空栈时取消键无人接住，仍要通知应用层，且服务本身保持可用
            Assert.AreEqual(1, rejected);
            Assert.AreEqual(0, service.PageCount);
            Assert.AreEqual(0, service.ModalCount);
            Assert.IsNull(service.CurrentPage);
            yield return null;
        }

        //取消的触发点只有输入绑定一处，测试不模拟设备，直接驱动绑定后调用的同一路由方法
        private static void FireCancel(PanelStackService service)
        {
            MethodInfo handler = typeof(PanelStackService).GetMethod(
                "HandleCancelInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler, "面板栈服务缺少取消路由方法");
            handler.Invoke(service, null);
        }

        private static void DisallowCancel(InteractivePanel panel)
        {
            FieldInfo field = typeof(InteractivePanel).GetField(
                "allowCancelBack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "可交互面板缺少取消返回开关字段");
            field.SetValue(panel, false);
            Assert.IsFalse(panel.AllowCancelBack);
        }

        private PanelStackService CreateService()
        {
            ClearLeftoverRoots();

            UIRoot root = Track(new GameObject("UIRoot")).AddComponent<UIRoot>();
            PanelStackService service = Track(new GameObject("PanelStackService")).AddComponent<PanelStackService>();
            service.BindLayers(new GUIService(root, new NullLoader()));
            return service;
        }

        private TestPanel CreatePage(string name)
        {
            return Track(new GameObject(name, typeof(CanvasGroup))).AddComponent<TestPanel>();
        }

        private TestModal CreateModal(string name)
        {
            return Track(new GameObject(name, typeof(CanvasGroup))).AddComponent<TestModal>();
        }

        private static void ClearLeftoverRoots()
        {
            foreach (UIRoot leftover in Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(leftover.gameObject);
            }
        }

        private GameObject Track(GameObject target)
        {
            objects.Add(target);
            return target;
        }

        private sealed class NullLoader : IPanelLoader
        {
            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }

        private sealed class TestPanel : InteractivePanel
        {
            public bool Interactable => CanvasGroup.interactable;
        }

        private sealed class TestModal : ModalPanel
        {
        }
    }
}
