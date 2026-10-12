using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>PanelStackService 的两栈收拢、遮蔽隔离与 backdrop 托管验收用例。</summary>
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
            Assert.AreEqual(typeof(Panel), push.GetParameters()[0].ParameterType);

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
            GameObject pageFocus = Track(new GameObject("PageFocus"));
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
        // 验收条件 AC4M: Modal 在场时 Page 层无法获得键盘或手柄焦点。
        public IEnumerator ModalPresent_BlocksPageLayerInteraction()
        {
            Track(new GameObject("EventSystem", typeof(EventSystem)));
            PanelStackService service = CreateService(out RectTransform pageLayer, out _);
            CanvasGroup pageLayerGroup = pageLayer.GetComponent<CanvasGroup>();
            TestPanel page = CreatePage("Page");
            GameObject pageFocus = Track(new GameObject("PageFocus"));
            SetDefaultFocusTarget(page, pageFocus);
            service.Push(page);

            Assert.IsTrue(pageLayerGroup.interactable);
            Assert.IsTrue(pageLayerGroup.blocksRaycasts);

            TestModal modal = CreateModal("Modal");
            GameObject modalFocus = Track(new GameObject("ModalFocus"));
            SetDefaultFocusTarget(modal, modalFocus);
            service.Push(modal);

            //Page 层整体不可交互且不接收射线，键盘/手柄焦点无法落入该层；焦点已由栈顶 Modal 接管
            Assert.IsFalse(pageLayerGroup.interactable);
            Assert.IsFalse(pageLayerGroup.blocksRaycasts);
            Assert.AreSame(modalFocus, EventSystem.current.currentSelectedGameObject);

            modal.Close();

            Assert.IsTrue(pageLayerGroup.interactable);
            Assert.IsTrue(pageLayerGroup.blocksRaycasts);
            Assert.AreSame(pageFocus, EventSystem.current.currentSelectedGameObject);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC7M：Modal 层不创建 Backdrop，嵌套 Modal 仍保持栈顶交互与下层可辨认。
        public IEnumerator NestedModals_DoNotCreateBackdropsAndKeepStackPanels()
        {
            PanelStackService service = CreateService(out _, out RectTransform modalLayer);
            TestModal first = CreateModal("First");
            TestModal second = CreateModal("Second");

            service.Push(first);
            service.Push(second);

            Assert.AreEqual(0, service.BackdropCount);
            Assert.AreEqual(2, service.ModalCount);
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.IsFalse(first.Interactable);
            Assert.IsTrue(second.Interactable);
            Assert.AreEqual(2, modalLayer.childCount);
            yield return null;
        }

        private PanelStackService CreateService(out RectTransform pageLayer, out RectTransform modalLayer)
        {
            ClearLeftoverRoots();

            GameObject uiRoot = Track(new GameObject("UIRoot"));
            pageLayer = CreateLayer(uiRoot.transform, "PageLayer");
            modalLayer = CreateLayer(uiRoot.transform, "ModalLayer");

            GameObject serviceObject = Track(new GameObject("PanelStackService"));
            PanelStackService service = serviceObject.AddComponent<PanelStackService>();
            service.BindLayers(pageLayer, modalLayer);
            return service;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            layer.transform.SetParent(parent, false);
            return layer.GetComponent<RectTransform>();
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

        private static Transform FindBackdrop(RectTransform modalLayer, int index)
        {
            int seen = 0;
            for (int childIndex = 0; childIndex < modalLayer.childCount; childIndex++)
            {
                Transform child = modalLayer.GetChild(childIndex);
                if (child.name != "ModalBackdrop")
                {
                    continue;
                }

                if (seen == index)
                {
                    return child;
                }

                seen++;
            }

            return null;
        }

        private static void AssertBackdropIsPresent(Transform backdrop)
        {
            RectTransform rect = backdrop.GetComponent<RectTransform>();
            Assert.IsNotNull(rect);
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.IsNotNull(backdrop.GetComponent<CanvasGroup>());
            Assert.IsNotNull(backdrop.GetComponent<UnityEngine.UI.Image>());
        }

        //backdrop 为纯遮蔽：不得存在任何 IPointerClickHandler 实现，否则点击会带出关闭语义
        private static void AssertBackdropHasNoClickHandler(Transform backdrop)
        {
            MonoBehaviour[] behaviours = backdrop.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                Assert.IsFalse(
                    behaviour is IPointerClickHandler,
                    "backdrop 不应实现 IPointerClickHandler");
            }
        }

        private static void SetDefaultFocusTarget(Panel panel, GameObject focusTarget)
        {
            FieldInfo field = typeof(Panel).GetField(
                "defaultFocusTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(panel, focusTarget);
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

        private sealed class TestPanel : Panel
        {
            public int ActivatedCount { get; set; }
            public int DeactivatedCount { get; set; }

            public bool Interactable => CanvasGroup != null && CanvasGroup.interactable;
            public bool RaycastsAllowed => CanvasGroup != null && CanvasGroup.blocksRaycasts;
            public bool RaycastsBlocked => !RaycastsAllowed;

            protected override void OnActivated()
            {
                ActivatedCount++;
            }

            protected override void OnDeactivated()
            {
                DeactivatedCount++;
            }
        }

        private sealed class TestModal : ModalPanel
        {
            public int ActivatedCount { get; set; }
            public int DeactivatedCount { get; set; }

            public bool Interactable => CanvasGroup != null && CanvasGroup.interactable;

            protected override void OnActivated()
            {
                ActivatedCount++;
            }

            protected override void OnDeactivated()
            {
                DeactivatedCount++;
            }
        }
    }
}
