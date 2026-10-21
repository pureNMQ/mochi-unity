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

using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>PanelStackService 的两栈收拢、遮蔽隔离与复原验收用例。</summary>
    public sealed class PanelStackServiceTests
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
        // 验收条件 AC1A: PanelStackService 为两栈唯一公开入口且 NavigationService 名称退役。
        public IEnumerator SingleEntryOwnsBothStacks_AndNavigationServiceNameRetired()
        {
            PanelStackService service = CreateService(out RectTransform pageLayer, out RectTransform modalLayer);
            TestPanel page = CreatePage("Page");
            TestModal modal = CreateModal("Modal");

            service.Push(page);
            service.Push(modal);

            Assert.AreEqual(1, service.PageCount);
            Assert.AreEqual(1, service.ModalCount);
            Assert.AreSame(page, service.CurrentPage);
            Assert.AreSame(modal, service.CurrentModal);

            //两栈都不对外暴露，调用方只能经同一入口操作
            Assert.IsNull(typeof(PanelStackService).GetProperty("Stack"));
            MethodInfo push = typeof(PanelStackService).GetMethod("Push");
            Assert.IsNotNull(push);
            Assert.AreEqual(typeof(InteractivePanel), push.GetParameters()[0].ParameterType);

            //旧栈持有者类型退役，ModalService 仅保留语义门面
            Assert.IsNull(typeof(PanelStackService).Assembly.GetType("Mochi.Unity.GUI.NavigationService"));
            Assert.IsNull(typeof(PanelStackService).Assembly.GetType("Mochi.Unity.GUI.PageService"));
            Assert.IsNotNull(typeof(PanelStackService).Assembly.GetType("Mochi.Unity.GUI.ModalService"));

            //服务把两类面板分别挂到绑定层，调用方无需自行处理挂载
            Assert.AreSame(pageLayer, page.transform.parent);
            Assert.AreSame(modalLayer, modal.transform.parent);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC5A: 关闭栈顶或中段 Page 分别等价 pop 或抽出条目且其余状态不变。
        public IEnumerator CloseTopPage_PopsAndCloseMiddlePageExtractsWithoutChangingOthers()
        {
            PanelStackService service = CreateService(out _, out _);
            TestPanel first = CreatePage("First");
            TestPanel middle = CreatePage("Middle");
            TestPanel top = CreatePage("Top");
            service.Push(first);
            service.Push(middle);
            service.Push(top);

            //同栈 Page 盖 Page：只有显示中的栈顶可见，被盖页保持实例存活
            Assert.AreSame(top, service.CurrentPage);
            Assert.IsFalse(first.IsVisible);
            Assert.IsFalse(middle.IsVisible);
            Assert.IsTrue(top.IsVisible);
            Assert.IsTrue(middle.gameObject.activeSelf);

            //关掉中段：抽出该条目，其余顺序与显示状态一律不变
            Assert.IsTrue(service.Close(middle));
            Assert.AreEqual(2, service.PageCount);
            Assert.AreSame(top, service.CurrentPage);
            Assert.IsFalse(first.IsVisible);
            Assert.IsTrue(top.IsVisible);

            //关掉栈顶：等价 pop，下层按原状显现
            Assert.IsTrue(service.Close(top));
            Assert.AreEqual(1, service.PageCount);
            Assert.AreSame(first, service.CurrentPage);
            Assert.IsTrue(first.IsVisible);

            //已出栈的面板不再被服务持有，重复关闭返回 false
            Assert.IsFalse(service.Close(middle));
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC8A: Modal 非空时 back 只关栈顶 Modal；Modal 空时 back 按 Page 数量 pop 并恢复；仅剩一个时弹出并通知空栈；面板自行关闭后两栈同步无悬空条目。
        public IEnumerator Back_WithModals_ClosesOnlyTopModal_ThenPagesByCount()
        {
            PanelStackService service = CreateService(out _, out _);
            TestPanel first = CreatePage("First");
            TestPanel second = CreatePage("Second");
            TestModal lowerModal = CreateModal("LowerModal");
            TestModal topModal = CreateModal("TopModal");
            service.Push(first);
            service.Push(second);
            service.Push(lowerModal);
            service.Push(topModal);

            //Modal 栈非空：back 先关闭全部 Modal，Page 栈保持不变
            Assert.IsTrue(service.Back());
            Assert.AreEqual(0, service.ModalCount);
            Assert.IsNull(service.CurrentModal);
            Assert.AreEqual(2, service.PageCount);
            Assert.IsFalse(lowerModal.IsOpen);
            Assert.IsFalse(topModal.IsOpen);

            //Modal 清空后：Page 多于一个时 pop 栈顶并恢复下层
            Assert.IsTrue(service.Back());
            Assert.AreEqual(1, service.PageCount);
            Assert.AreSame(first, service.CurrentPage);
            Assert.IsTrue(first.IsVisible);

            //仅剩一个 Page：弹出后空栈并通知应用层
            bool notified = false;
            service.StackEmptied += () => notified = true;
            Assert.IsTrue(service.Back());
            Assert.AreEqual(0, service.PageCount);
            Assert.IsTrue(notified);
            Assert.IsFalse(service.Back());

            //面板自行关闭：所属栈同步移除，无悬空条目
            TestModal selfClosingModal = CreateModal("SelfClosingModal");
            service.Push(selfClosingModal);
            selfClosingModal.Close();
            Assert.AreEqual(0, service.ModalCount);
            Assert.IsNull(service.CurrentModal);

            TestPanel selfClosingPage = CreatePage("SelfClosingPage");
            service.Push(selfClosingPage);
            selfClosingPage.Close();
            Assert.AreEqual(0, service.PageCount);
            Assert.IsNull(service.CurrentPage);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC2M: Modal 遮住 Page 层或下层 Modal 时被遮面板保持可见、触发去激活且输入不可达。
        public IEnumerator Cover_KeepsCoveredPanelsRenderedAndDeactivated()
        {
            PanelStackService service = CreateService(out _, out _);
            TestPanel page = CreatePage("Page");
            TestModal lowerModal = CreateModal("LowerModal");
            TestModal topModal = CreateModal("TopModal");
            service.Push(page);
            service.Push(lowerModal);
            page.ActivatedCount = 0;
            lowerModal.ActivatedCount = 0;

            service.Push(topModal);

            //被遮面板继续渲染，只是逻辑可见性与交互被收回
            Assert.IsTrue(page.gameObject.activeSelf);
            Assert.IsFalse(page.IsVisible);
            Assert.IsFalse(page.Interactable);
            Assert.IsTrue(page.RaycastsBlocked);

            Assert.IsTrue(lowerModal.gameObject.activeSelf);
            Assert.IsFalse(lowerModal.IsVisible);
            Assert.IsFalse(lowerModal.Interactable);
            //下层面板被后一个 Modal 遮蔽一次，关键是终态被收回
            Assert.AreEqual(1, lowerModal.DeactivatedCount);

            //栈顶 Modal 保持可交互
            Assert.IsTrue(topModal.IsVisible);
            Assert.IsTrue(topModal.Interactable);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC3M: 遮蔽解除后被遮面板重新激活且焦点恢复。
        public IEnumerator Uncover_ReactivatesCoveredPanelsAndRestoresFocus()
        {
            Track(new GameObject("EventSystem", typeof(EventSystem)));
            PanelStackService service = CreateService(out _, out _);
            TestPanel page = CreatePage("Page");
            GameObject pageFocus = CreateOwnedChild(page.transform, "PageFocus");
            SetDefaultFocusTarget(page, pageFocus);
            service.Push(page);
            Assert.AreSame(pageFocus, EventSystem.current.currentSelectedGameObject);

            TestModal modal = CreateModal("Modal");
            service.Push(modal);
            Assert.IsFalse(page.Interactable);

            modal.Close();

            Assert.IsTrue(page.IsVisible);
            Assert.IsTrue(page.Interactable);
            Assert.IsTrue(page.RaycastsAllowed);
            Assert.GreaterOrEqual(page.ActivatedCount, 1);
            Assert.AreSame(pageFocus, EventSystem.current.currentSelectedGameObject);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC7：当有新页面推入，则旧页面先失去交互，新页面后显示。
        public IEnumerator PushPage_CoversTopPageBeforeNewPageShows()
        {
            PanelStackService service = CreateService(out _, out _);
            List<string> journal = new List<string>();
            TestPanel first = CreatePage("First");
            first.Journal = journal;
            service.Push(first);
            journal.Clear();

            TestPanel second = CreatePage("Second");
            second.Journal = journal;
            service.Push(second);

            int covered = journal.IndexOf("First:deactivated");
            int shown = journal.IndexOf("Second:shown");
            Assert.That(covered, Is.GreaterThanOrEqualTo(0), string.Join(",", journal));
            Assert.That(shown, Is.GreaterThan(covered), string.Join(",", journal));
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC8：当有弹窗推入，则下层页面与下层弹窗先失去交互，弹窗后显示。
        public IEnumerator PushModal_CoversPagesAndLowerModalsBeforeShowing()
        {
            PanelStackService service = CreateService(out _, out _);
            List<string> journal = new List<string>();
            TestPanel page = CreatePage("Page");
            page.Journal = journal;
            service.Push(page);
            journal.Clear();

            TestModal modal = CreateModal("Modal");
            modal.Journal = journal;
            service.Push(modal);

            int pageCovered = journal.IndexOf("Page:deactivated");
            int modalShown = journal.IndexOf("Modal:shown");
            Assert.That(pageCovered, Is.GreaterThanOrEqualTo(0), string.Join(",", journal));
            Assert.That(modalShown, Is.GreaterThan(pageCovered), string.Join(",", journal));

            journal.Clear();
            TestModal topModal = CreateModal("TopModal");
            topModal.Journal = journal;
            service.Push(topModal);

            int lowerCovered = journal.IndexOf("Modal:deactivated");
            int topShown = journal.IndexOf("TopModal:shown");
            Assert.That(lowerCovered, Is.GreaterThanOrEqualTo(0), string.Join(",", journal));
            Assert.That(topShown, Is.GreaterThan(lowerCovered), string.Join(",", journal));
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC9：当弹窗在场，则页面层与下层弹窗既不能被鼠标点击，也不能被手柄或键盘导航到达。
        public IEnumerator ModalPresent_CoversPagesAndLowerModals()
        {
            PanelStackService service = CreateService(out _, out _);
            TestPanel page = CreatePage("Page");
            TestModal lowerModal = CreateModal("LowerModal");
            TestModal topModal = CreateModal("TopModal");
            service.Push(page);
            service.Push(lowerModal);
            service.Push(topModal);

            //被遮面板逐个收回交互与射线，不依赖层整体开关
            Assert.IsTrue(page.RaycastsBlocked);
            Assert.IsFalse(page.Interactable);
            Assert.IsTrue(lowerModal.RaycastsBlocked);
            Assert.IsFalse(lowerModal.Interactable);

            //只有栈顶 Modal 保持鼠标与导航可达
            Assert.IsTrue(topModal.RaycastsAllowed);
            Assert.IsTrue(topModal.Interactable);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC10：当页面被同栈新页面盖住，则被盖页面不可交互但保持渲染。
        public IEnumerator CoveredPage_StaysRenderedButNotInteractive()
        {
            PanelStackService service = CreateService(out _, out _);
            TestPanel covered = CreatePage("Covered");
            TestPanel top = CreatePage("Top");
            service.Push(covered);

            service.Push(top);

            Assert.IsTrue(covered.gameObject.activeSelf);
            Assert.AreEqual(1f, covered.CanvasGroup.alpha);
            Assert.IsFalse(covered.IsVisible);
            Assert.IsFalse(covered.Interactable);
            Assert.IsTrue(covered.RaycastsBlocked);
            Assert.IsTrue(top.Interactable);
            yield return null;
        }

        [Test]
        // 验收条件 AC16：当需要打开 HUD 面板，则经 GUI 服务的层接口创建；面板栈服务的公开面不含 HUD 相关成员。
        public void PublicSurface_ExposesNoHudMembers()
        {
            AssertNoPublicMemberContains(typeof(PanelStackService), "hud");

            //HUD 面板的唯一创建路径是 GUI 服务的层接口
            Assert.IsNotNull(typeof(GUIService).GetMethod("CreateAndShowPanelAsync"));
            Assert.IsNotNull(typeof(GUIService).GetMethod("GetLayer"));
            Assert.IsTrue(Enum.IsDefined(typeof(GUILayer), GUILayer.HUD));
        }

        [Test]
        // 验收条件 AC17：面板栈服务不创建 backdrop 对象，公开面不含 backdrop 计数与点击关闭相关成员。
        public void PublicSurface_CreatesNoBackdropObject()
        {
            PanelStackService service = CreateService(out _, out RectTransform modalLayer);
            AssertNoPublicMemberContains(typeof(PanelStackService), "backdrop");
            AssertNoPublicMemberContains(typeof(ModalPanel), "backdrop");

            TestModal first = CreateModal("First");
            TestModal second = CreateModal("Second");
            service.Push(first);
            service.Push(second);

            //Modal 层只承载入栈面板本身，不插入任何遮蔽对象
            Assert.AreEqual(2, modalLayer.childCount);
            Assert.AreEqual(2, service.ModalCount);
        }

        private PanelStackService CreateService(out RectTransform pageLayer, out RectTransform modalLayer)
        {
            ClearLeftoverRoots();

            UIRoot root = Track(new GameObject("UIRoot")).AddComponent<UIRoot>();
            GameObject serviceObject = Track(new GameObject("PanelStackService"));
            PanelStackService service = serviceObject.AddComponent<PanelStackService>();
            service.BindLayers(new GUIService(root, new NullLoader()));
            pageLayer = root.Page;
            modalLayer = root.Modal;
            return service;
        }

        private TestPanel CreatePage(string name)
        {
            GameObject gameObject = Track(new GameObject(name, typeof(CanvasGroup)));
            return gameObject.AddComponent<TestPanel>();
        }

        private TestModal CreateModal(string name)
        {
            GameObject gameObject = Track(new GameObject(name, typeof(CanvasGroup)));
            return gameObject.AddComponent<TestModal>();
        }

        private static GameObject CreateOwnedChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void SetDefaultFocusTarget(InteractivePanel panel, GameObject focusTarget)
        {
            FieldInfo field = typeof(InteractivePanel).GetField(
                "defaultFocusTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(panel, focusTarget);
        }

        private static void AssertNoPublicMemberContains(Type type, string token)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.DeclaredOnly |
                                       BindingFlags.Instance | BindingFlags.Static;
            foreach (MemberInfo member in type.GetMembers(Flags))
            {
                Assert.IsFalse(
                    member.Name.ToLowerInvariant().Contains(token),
                    type.Name + "." + member.Name + " 属于已从公开面删除的语义");
            }
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
            public List<string> Journal { get; set; }
            public int ActivatedCount { get; set; }
            public int DeactivatedCount { get; set; }

            public bool Interactable => CanvasGroup.interactable;
            public bool RaycastsAllowed => CanvasGroup.blocksRaycasts;
            public bool RaycastsBlocked => !RaycastsAllowed;

            protected override void OnActivated()
            {
                ActivatedCount++;
                Journal?.Add(name + ":activated");
            }

            protected override void OnDeactivated()
            {
                DeactivatedCount++;
                Journal?.Add(name + ":deactivated");
            }

            protected override void OnShow()
            {
                Journal?.Add(name + ":shown");
            }
        }

        private sealed class TestModal : ModalPanel
        {
            public List<string> Journal { get; set; }
            public int ActivatedCount { get; set; }
            public int DeactivatedCount { get; set; }

            public bool Interactable => CanvasGroup.interactable;
            public bool RaycastsAllowed => CanvasGroup.blocksRaycasts;
            public bool RaycastsBlocked => !RaycastsAllowed;

            protected override void OnActivated()
            {
                ActivatedCount++;
                Journal?.Add(name + ":activated");
            }

            protected override void OnDeactivated()
            {
                DeactivatedCount++;
                Journal?.Add(name + ":deactivated");
            }

            protected override void OnShow()
            {
                Journal?.Add(name + ":shown");
            }
        }
    }
}
