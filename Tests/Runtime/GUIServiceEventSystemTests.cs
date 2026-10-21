using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>GUIService 的 EventSystem 保证与 UI 输入资产接线用例。</summary>
    public sealed class GUIServiceEventSystemTests
    {
        private static readonly string[] UiActionNames =
        {
            "Point", "Move", "Submit", "Cancel", "LeftClick", "MiddleClick", "RightClick", "ScrollWheel"
        };

        private readonly List<Object> objectsToDestroy = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in objectsToDestroy)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void MissingEventSystem_CreatesOneOnUIRoot()
        {
            ClearLeftovers();
            UIRoot root = CreateRoot();

            GUIService service = new GUIService(root, new NullLoader());

            EventSystem[] eventSystems = FindEventSystems();
            Assert.That(eventSystems.Length, Is.EqualTo(1));
            Assert.That(eventSystems[0].transform.parent, Is.SameAs(root.transform));
            InputSystemUIInputModule module = eventSystems[0].GetComponent<InputSystemUIInputModule>();
            Assert.That(module, Is.Not.Null);
            Assert.That(module.actionsAsset, Is.Not.Null, "未注入资产时应保留模块自带默认资产");
            service.Dispose();
        }

        [Test]
        public void ExistingEventSystem_IsReusedInsteadOfCreatingAnother()
        {
            ClearLeftovers();
            GameObject eventSystemObject = Track(new GameObject("EventSystem", typeof(EventSystem)));
            UIRoot root = CreateRoot();

            GUIService service = new GUIService(root, new NullLoader());

            EventSystem[] eventSystems = FindEventSystems();
            Assert.That(eventSystems.Length, Is.EqualTo(1));
            Assert.That(eventSystems[0].gameObject, Is.SameAs(eventSystemObject));
            Assert.That(eventSystemObject.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
            service.Dispose();
        }

        [Test]
        public void InactiveEventSystemInScene_IsReusedInsteadOfCreatingAnother()
        {
            ClearLeftovers();
            GameObject eventSystemObject = Track(new GameObject("EventSystem", typeof(EventSystem)));
            eventSystemObject.SetActive(false);
            UIRoot root = CreateRoot();

            GUIService service = new GUIService(root, new NullLoader());

            Assert.That(FindEventSystems().Length, Is.EqualTo(1));
            Assert.That(eventSystemObject.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
            service.Dispose();
        }

        [Test]
        public void InjectedUiActions_BindEveryModuleActionToThatAsset()
        {
            ClearLeftovers();
            UIRoot root = CreateRoot();
            InputActionAsset uiActions = CreateUiActions(UiActionNames);
            InputActionMap uiMap = uiActions.FindActionMap("UI");

            GUIService service = new GUIService(root, new NullLoader(), null, uiActions);

            InputSystemUIInputModule module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            Assert.That(module.actionsAsset, Is.SameAs(uiActions));
            Assert.That(module.point.action, Is.SameAs(uiMap.FindAction("Point")));
            Assert.That(module.move.action, Is.SameAs(uiMap.FindAction("Move")));
            Assert.That(module.submit.action, Is.SameAs(uiMap.FindAction("Submit")));
            Assert.That(module.cancel.action, Is.SameAs(uiMap.FindAction("Cancel")));
            Assert.That(module.leftClick.action, Is.SameAs(uiMap.FindAction("LeftClick")));
            Assert.That(module.middleClick.action, Is.SameAs(uiMap.FindAction("MiddleClick")));
            Assert.That(module.rightClick.action, Is.SameAs(uiMap.FindAction("RightClick")));
            Assert.That(module.scrollWheel.action, Is.SameAs(uiMap.FindAction("ScrollWheel")));
            service.Dispose();
        }

        [Test]
        // 验收条件 AC20：当玩家在任意可交互页面触发导航、确认或取消，则其键位来自项目输入资产的 UI 映射，而不是 Unity 自带默认资产。
        public void ModuleAlreadyOnDefaultActions_SwapsToProjectUiMap()
        {
            ClearLeftovers();
            GameObject eventSystemObject = Track(new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule)));
            InputSystemUIInputModule module = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            Assert.That(module.actionsAsset, Is.Not.Null, "模块 OnEnable 未退到默认资产，本用例没覆盖复用路径");

            UIRoot root = CreateRoot();
            InputActionAsset uiActions = CreateUiActions(UiActionNames);
            InputActionMap uiMap = uiActions.FindActionMap("UI");

            GUIService service = new GUIService(root, new NullLoader(), null, uiActions);

            Assert.That(module.actionsAsset, Is.SameAs(uiActions));
            Assert.That(module.move.action, Is.SameAs(uiMap.FindAction("Move")), "方向输入必须来自项目资产");
            Assert.That(module.submit.action, Is.SameAs(uiMap.FindAction("Submit")));
            Assert.That(module.cancel.action, Is.SameAs(uiMap.FindAction("Cancel")));
            Assert.That(module.leftClick.action, Is.SameAs(uiMap.FindAction("LeftClick")), "与默认资产不同名的点击动作必须重接");
            Assert.That(module.move.action.enabled, Is.True, "重新绑定后动作必须处于启用状态");
            service.Dispose();
        }

        [Test]
        public void InjectedUiActions_MissingUiAction_Throws()
        {
            ClearLeftovers();
            UIRoot root = CreateRoot();
            InputActionAsset uiActions = CreateUiActions(
                "Point", "Move", "Submit", "Cancel", "MiddleClick", "RightClick", "ScrollWheel");

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => new GUIService(root, new NullLoader(), null, uiActions));

            StringAssert.Contains("LeftClick", thrown.Message);
        }

        private UIRoot CreateRoot()
        {
            return Track(new GameObject("UIRoot")).AddComponent<UIRoot>();
        }

        private InputActionAsset CreateUiActions(params string[] actionNames)
        {
            InputActionAsset asset = Track(ScriptableObject.CreateInstance<InputActionAsset>());
            asset.name = "TestUiActions";
            InputActionMap map = asset.AddActionMap("UI");
            foreach (string actionName in actionNames)
            {
                map.AddAction(actionName);
            }

            return asset;
        }

        private static EventSystem[] FindEventSystems()
        {
            return Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private static void ClearLeftovers()
        {
            foreach (UIRoot leftover in Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(leftover.gameObject);
            }

            foreach (EventSystem leftover in FindEventSystems())
            {
                Object.DestroyImmediate(leftover.gameObject);
            }
        }

        private T Track<T>(T target) where T : Object
        {
            objectsToDestroy.Add(target);
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
    }
}
