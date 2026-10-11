using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    public sealed class PageModalNavigationTests
    {
        private readonly System.Collections.Generic.List<GameObject> objects =
            new System.Collections.Generic.List<GameObject>();

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
        }

        [UnityTest]
        // 验收条件 AC5A: 显式关闭栈顶或中段 Page 分别等价 pop 或抽出条目且其余状态不变。
        public IEnumerator CloseTopPage_PopsAndCloseMiddlePageExtractsWithoutChangingOthers()
        {
            PageService pages = new PageService();
            TestPanel first = CreatePanel();
            TestPanel middle = CreatePanel();
            TestPanel top = CreatePanel();
            pages.Push(first);
            pages.Push(middle);
            pages.Push(top);

            Assert.IsTrue(pages.Close(middle));
            Assert.AreEqual(2, pages.Count);
            Assert.AreSame(first, pages.Stack[0]);
            Assert.AreSame(top, pages.Current);

            Assert.IsTrue(pages.Close(top));
            Assert.AreEqual(1, pages.Count);
            Assert.AreSame(first, pages.Current);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC8A: Modal 栈非空时玩家 back 只关闭栈顶 Modal。
        public IEnumerator Back_WithModals_ClosesOnlyTopModal()
        {
            ModalService modals = new ModalService();
            TestModal lower = CreateModal();
            TestModal top = CreateModal();
            modals.Push(lower);
            modals.Push(top);

            Assert.IsTrue(modals.Back());
            Assert.AreEqual(1, modals.Count);
            Assert.AreSame(lower, modals.Current);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC9A: Modal 为空且 Page 多于一个时 back pop 栈顶 Page 并恢复下层。
        public IEnumerator Back_WithMultiplePages_PopsTopAndRestoresLower()
        {
            PageService pages = new PageService();
            TestPanel lower = CreatePanel();
            TestPanel top = CreatePanel();
            pages.Push(lower);
            pages.Push(top);
            lower.ShowImmediate();
            lower.HideImmediate();

            Assert.IsTrue(pages.Back());
            Assert.AreEqual(1, pages.Count);
            Assert.AreSame(lower, pages.Current);
            Assert.IsTrue(lower.IsVisible);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC10A: 仅剩一个 Page 时 back 弹出该 Page，空栈通知应用层。
        public IEnumerator Back_WithSinglePage_EmptiesStackAndNotifiesApplication()
        {
            PageService pages = new PageService();
            TestPanel page = CreatePanel();
            bool notified = false;
            pages.StackEmptied += () => notified = true;
            pages.Push(page);

            Assert.IsTrue(pages.Back());
            Assert.AreEqual(0, pages.Count);
            Assert.IsTrue(notified);
            yield return null;
        }

        [UnityTest]
        // 验收条件 AC12A: 面板任何路径自行关闭后服务同步栈且无悬空条目。
        public IEnumerator SelfClosingPanel_RemovesEntryFromOwningStack()
        {
            PageService pages = new PageService();
            TestPanel page = CreatePanel();
            pages.Push(page);

            page.Close();

            Assert.AreEqual(0, pages.Count);
            Assert.IsNull(pages.Current);
            yield return null;
        }

        private TestPanel CreatePanel()
        {
            GameObject gameObject = new GameObject("TestPanel");
            objects.Add(gameObject);
            return gameObject.AddComponent<TestPanel>();
        }

        private TestModal CreateModal()
        {
            GameObject gameObject = new GameObject("TestModal");
            objects.Add(gameObject);
            return gameObject.AddComponent<TestModal>();
        }

        private sealed class TestPanel : Panel
        {
        }

        private sealed class TestModal : ModalPanel
        {
        }
    }
}
