using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    /// <summary>
    /// R3/R7：Panel 生命周期内部幂等与 IPanel 契约验证。
    /// 只测 Panel 本身，不推入容器；internal 生命周期方法经反射调用
    /// （测试程序集无 InternalsVisibleTo，沿用 ModalContainerLifecycleTests 的模式）。
    /// </summary>
    public class PanelLifecycleIdempotencyTests
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
        public IEnumerator ShowInternal_WhenAlreadyVisible_IsNoOp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "ShowInternal");
                Assert.That(panel.IsVisible, Is.True);
                Assert.That(panel.gameObject.activeSelf, Is.True);
                Assert.That(panel.ShowCount, Is.EqualTo(1));

                //重复 Show：不再触发 OnShow，也不激活/迁移任何状态（旧实现会在此恢复焦点）
                InvokeLifecycle(panel, "ShowInternal");
                Assert.That(panel.ShowCount, Is.EqualTo(1));
                Assert.That(panel.IsVisible, Is.True);
                Assert.That(panel.gameObject.activeSelf, Is.True);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator HideInternal_WhenAlreadyHidden_IsNoOp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel(false);
                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.HideCount, Is.Zero);
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel.gameObject.activeSelf, Is.False);

                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.HideCount, Is.Zero);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator HideInternal_AfterShown_HidesOnlyOncePerTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "ShowInternal");
                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.ShowCount, Is.EqualTo(1));
                Assert.That(panel.HideCount, Is.EqualTo(1));
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel.gameObject.activeSelf, Is.False);

                //重复 Hide：不重复保存焦点、不重复执行 OnHide
                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.HideCount, Is.EqualTo(1));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator OpenInternal_WhenAlreadyOpen_IsNoOp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "OpenInternal");
                Assert.That(panel.IsOpen, Is.True);
                Assert.That(panel.OpenCount, Is.EqualTo(1));

                InvokeLifecycle(panel, "OpenInternal");
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.IsOpen, Is.True);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator CloseInternal_WhenAlreadyClosed_IsNoOp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "OpenInternal");
                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.IsOpen, Is.False);
                Assert.That(panel.CloseCount, Is.EqualTo(1));

                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                Assert.That(panel.IsOpen, Is.False);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator CloseInternal_WhenNeverOpened_RunsCleanupOnceThenIsNoOp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                //容器（PageContainer/ModalContainer）自身从不 Open，却复用 OnClose 做栈清理；
                //从未打开的面板首次 Close 仍执行一次清理，之后重复 Close 为空操作
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                Assert.That(panel.IsOpen, Is.False);

                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator ShowHideMigration_FiresLifecycleOncePerTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel(false);
                //隐藏→显示
                InvokeLifecycle(panel, "ShowInternal");
                Assert.That(panel.ShowCount, Is.EqualTo(1));
                Assert.That(panel.HideCount, Is.Zero);
                Assert.That(panel.IsVisible, Is.True);
                //显示→隐藏
                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.HideCount, Is.EqualTo(1));
                Assert.That(panel.IsVisible, Is.False);
                //隐藏→显示→隐藏：每次迁移各触发一次
                InvokeLifecycle(panel, "ShowInternal");
                InvokeLifecycle(panel, "HideInternal");
                Assert.That(panel.ShowCount, Is.EqualTo(2));
                Assert.That(panel.HideCount, Is.EqualTo(2));
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel.gameObject.activeSelf, Is.False);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator OpenCloseCycle_FiresLifecycleOncePerTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "OpenInternal");
                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.CloseCount, Is.EqualTo(1));

                //重新打开后再关闭：每个打开周期各执行一次 OnOpen/OnClose
                InvokeLifecycle(panel, "OpenInternal");
                InvokeLifecycle(panel, "CloseInternal");
                Assert.That(panel.OpenCount, Is.EqualTo(2));
                Assert.That(panel.CloseCount, Is.EqualTo(2));
                Assert.That(panel.IsOpen, Is.False);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator IPanelContract_ExposesStateAndInteractability()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CountingPanel panel = CreatePanel();
                InvokeLifecycle(panel, "OpenInternal");
                InvokeLifecycle(panel, "ShowInternal");

                //经公共契约访问：状态查询与交互控制；生命周期入口不在公共契约上（C2）
                IPanel contract = panel;
                Assert.That(contract.IsOpen, Is.True);
                Assert.That(contract.IsVisible, Is.True);

                contract.SetInteractable(false);
                Assert.That(panel.CanvasGroup.interactable, Is.False);
                Assert.That(panel.CanvasGroup.blocksRaycasts, Is.False);

                contract.SetInteractable(true);
                Assert.That(panel.CanvasGroup.interactable, Is.True);
                Assert.That(panel.CanvasGroup.blocksRaycasts, Is.True);
                await UniTask.Yield();
            });
        }

        private CountingPanel CreatePanel(bool active = true)
        {
            GameObject panelObject = Track(new GameObject("CountingPanel"));
            //与既有测试的预制体搭建方式一致：挂 CanvasGroup 供 SetInteractable 验证
            panelObject.AddComponent<CanvasGroup>();
            if (!active)
            {
                panelObject.SetActive(false);
            }

            return panelObject.AddComponent<CountingPanel>();
        }

        private static void InvokeLifecycle(Panel panel, string methodName)
        {
            MethodInfo method = typeof(Panel).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Panel.{methodName} 应以 internal 方法形式存在");
            method.Invoke(panel, null);
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class CountingPanel : Panel
        {
            public int OpenCount { get; private set; }
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override void OnOpen()
            {
                OpenCount++;
            }

            protected override void OnShow()
            {
                ShowCount++;
            }

            protected override void OnHide()
            {
                HideCount++;
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }
    }
}
