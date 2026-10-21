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
    /// <summary>InteractivePanel 的焦点交接验收用例：断言面只有 EventSystem 的选中对象。</summary>
    public sealed class InteractivePanelFocusTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ClearLeftoverRoots();
            Track(new GameObject("EventSystem", typeof(EventSystem)));
            EventSystem.current.SetSelectedGameObject(null);
        }

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
        // 验收条件 AC1：当可交互面板打开，则 EventSystem 的选中落在该面板声明的入口焦点上。
        public IEnumerator Show_SelectsDeclaredEntryFocus()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestInteractivePanel page = CreatePanel("Page");
                GameObject entry = CreateOwnedChild(page.transform, "Entry");
                SetDefaultFocusTarget(page, entry);

                page.Show();

                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(entry));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC2：入口焦点可在运行时由代码声明；声明时若面板已在前台，立即生效。
        public IEnumerator SetEntryFocus_DeclaresFocusRuntimeAndAppliesWhileForeground()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestInteractivePanel page = CreatePanel("Page");
                GameObject inspectorEntry = CreateOwnedChild(page.transform, "InspectorEntry");
                GameObject declared = CreateOwnedChild(page.transform, "Declared");
                SetDefaultFocusTarget(page, inspectorEntry);

                //不在前台时只登记，不抢当前选中
                page.SetEntryFocus(declared);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);

                page.Show();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(declared));

                //已在前台时声明立即生效
                GameObject later = CreateOwnedChild(page.transform, "Later");
                page.SetEntryFocus(later);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(later));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC3：当可交互面板重新显示，则选中回到它失去焦点前的那个控件。
        public IEnumerator ReShow_RestoresControlSelectedBeforeHide()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestInteractivePanel page = CreatePanel("Page");
                GameObject entry = CreateOwnedChild(page.transform, "Entry");
                GameObject inner = CreateOwnedChild(page.transform, "Inner");
                SetDefaultFocusTarget(page, entry);
                page.Show();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(entry));

                EventSystem.current.SetSelectedGameObject(inner);
                page.Hide();
                page.Show();

                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(inner));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC4：当要恢复的控件不属于该面板或已失效，则选中退回该面板的入口焦点；入口焦点也不可用时清空选中。
        public IEnumerator ReShow_WithUnusableRestoreTarget_FallsBackToEntryFocusThenClears()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestInteractivePanel page = CreatePanel("Page");
                GameObject entry = CreateOwnedChild(page.transform, "Entry");
                GameObject inner = CreateOwnedChild(page.transform, "Inner");
                SetDefaultFocusTarget(page, entry);
                page.Show();

                //恢复目标被移出面板：不再属于本面板
                EventSystem.current.SetSelectedGameObject(inner);
                page.Hide();
                inner.transform.SetParent(null, false);
                page.Show();
                Assert.That(
                    EventSystem.current.currentSelectedGameObject,
                    Is.SameAs(entry),
                    "不属于本面板的恢复目标应退回入口焦点");

                //恢复目标已失效，入口焦点也已失效：无可用目标，清空当前选中
                page.Hide();
                GameObject foreign = Track(new GameObject("Foreign"));
                EventSystem.current.SetSelectedGameObject(foreign);
                Object.DestroyImmediate(entry);
                page.Show();
                Assert.That(
                    EventSystem.current.currentSelectedGameObject,
                    Is.Null,
                    "恢复目标与入口焦点都不可用时应清空选中");
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC5：当框架把焦点交给某个面板，则只接受属于该面板子孙的控件；不属于时不改变当前选中。
        public IEnumerator FocusTo_ForeignTarget_KeepsCurrentSelection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestInteractivePanel page = CreatePanel("Page");
                GameObject entry = CreateOwnedChild(page.transform, "Entry");
                SetDefaultFocusTarget(page, entry);
                page.Show();

                GameObject foreign = Track(new GameObject("Foreign"));
                page.FocusTo(foreign);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(entry));

                GameObject inner = CreateOwnedChild(page.transform, "Inner");
                page.FocusTo(inner);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(inner));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC6：当栈顶可交互面板关闭且其下没有其他可交互面板，则 EventSystem 的选中被清空。
        public IEnumerator CloseTopPage_WithoutLowerPanel_ClearsSelection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = Track(new GameObject("UIRoot")).AddComponent<UIRoot>();
                GameObject serviceObject = Track(new GameObject("PanelStackService"));
                PanelStackService service = serviceObject.AddComponent<PanelStackService>();
                service.BindLayers(new GUIService(root, new NullLoader()));

                TestInteractivePanel page = CreatePanel("Page");
                GameObject entry = CreateOwnedChild(page.transform, "Entry");
                SetDefaultFocusTarget(page, entry);
                service.Push(page);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(entry));

                page.Close();

                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
                Assert.AreEqual(0, service.PageCount);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        // 验收条件 AC12：当面板继承基础 Panel 而非可交互面板类型，则它不参与焦点交接。
        public IEnumerator BasePanel_DoesNotTakePartInFocusHandoff()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject panelObject = Track(new GameObject("Plain", typeof(CanvasGroup)));
                TestBasePanel panel = panelObject.AddComponent<TestBasePanel>();
                GameObject inner = CreateOwnedChild(panel.transform, "Inner");
                EventSystem.current.SetSelectedGameObject(inner);

                panel.Show();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(inner));

                panel.Hide();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(inner));
                await UniTask.Yield();
            });
        }

        private TestInteractivePanel CreatePanel(string name)
        {
            GameObject panelObject = Track(new GameObject(name, typeof(CanvasGroup)));
            return panelObject.AddComponent<TestInteractivePanel>();
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

        private sealed class TestInteractivePanel : InteractivePanel
        {
        }

        private sealed class TestBasePanel : Panel
        {
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
    }
}
