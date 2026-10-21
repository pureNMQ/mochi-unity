using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    public sealed class GUIServiceTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objects)
            {
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }

            objects.Clear();
        }

        [UnityTest]
        // 验收条件 AC3：相同实例 ID 且 Panel 类型一致的并发创建请求只加载一次资源、只实例化一个 Panel，并返回同一实例。
        public IEnumerator SameInstanceIdAndType_ConcurrentCreationSharesPanel()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                FactoryLoader loader = new FactoryLoader(CreatePrefab<FactoryPanel>());
                GUIService service = new GUIService(root, loader);

                UniTask<FactoryPanel> firstTask = service.CreatePanelAsync<FactoryPanel>("panel", "shared");
                UniTask<FactoryPanel> secondTask = service.CreatePanelAsync<FactoryPanel>("panel", "shared");
                FactoryPanel first = await firstTask;
                FactoryPanel second = await secondTask;

                Assert.That(loader.LoadCount, Is.EqualTo(1));
                Assert.That(first, Is.SameAs(second));
                service.Dispose();
            });
        }

        [UnityTest]
        // 验收条件 AC4：相同实例 ID 但 Panel 类型不一致的创建请求报告类型冲突错误。
        public IEnumerator SameInstanceIdAndDifferentType_ThrowsConflict()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<FactoryPanel>()));
                await service.CreatePanelAsync<FactoryPanel>("panel", "shared");

                InvalidOperationException thrown = null;
                try
                {
                    await service.CreatePanelAsync<OtherPanel>("panel", "shared");
                }
                catch (InvalidOperationException exception)
                {
                    thrown = exception;
                }

                Assert.That(thrown, Is.Not.Null);
                service.Dispose();
            });
        }

        [UnityTest]
        // 验收条件 AC5：Panel 关闭后，GUIService 的实例查询不再返回该 Panel。
        public IEnumerator ClosedPanel_IsRemovedFromInstanceQuery()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<FactoryPanel>()));
                FactoryPanel panel = await service.CreatePanelAsync<FactoryPanel>("panel", "instance");

                Assert.That(service.GetPanel<FactoryPanel>("instance"), Is.SameAs(panel));
                panel.Close();
                Assert.That(service.GetPanel<FactoryPanel>("instance"), Is.Null);
                service.Dispose();
            });
        }

        [UnityTest]
        // 验收条件 AC2：GUIService 能按资源地址创建 Panel，并将 Panel 插入指定 GUILayer。
        public IEnumerator CreateAndShowPanel_InsertsIntoLayer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<FactoryPanel>()));
                FactoryPanel panel = await service.CreateAndShowPanelAsync<FactoryPanel>("panel", GUILayer.Page, "page");

                Assert.That(panel.transform.parent, Is.SameAs(root.Page));
                Assert.That(panel.IsVisible, Is.True);
                service.Dispose();
            });
        }

        [UnityTest]
        // 架构：数据交付时机 GUI 服务能以基础 Panel 泛型版为类型参数创建带数据面板，数据经应用通道交付。
        public IEnumerator CreatePanelAsync_TypedPanel_AppliesDataThroughDataInterface()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<TestDataPanel>()));
                TestData data = new TestData(42);

                TestDataPanel panel = await service.CreatePanelAsync<TestDataPanel, TestData>(data, "panel", "typed-page");

                Assert.That(panel.GetType(), Is.EqualTo(typeof(TestDataPanel)));
                Assert.That(panel.Current, Is.SameAs(data));
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(service.GetPanel("typed-page"), Is.SameAs(panel));
                service.Dispose();
            });
        }

        [UnityTest]
        // 架构：数据交付时机 GUI 服务能以可交互面板泛型版为类型参数创建带数据面板，数据经应用通道交付。
        public IEnumerator CreatePanelAsync_TypedInteractivePanel_AppliesDataThroughDataInterface()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<TestDataInteractivePanel>()));
                TestData data = new TestData(7);

                TestDataInteractivePanel panel =
                    await service.CreatePanelAsync<TestDataInteractivePanel, TestData>(data, "panel", "typed-interactive-page");

                Assert.That(panel.GetType(), Is.EqualTo(typeof(TestDataInteractivePanel)));
                Assert.That(panel.Current, Is.SameAs(data));
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(service.GetPanel("typed-interactive-page"), Is.SameAs(panel));
                service.Dispose();
            });
        }

        [UnityTest]
        // 架构：数据交付时机 面板打开后再次应用数据只替换数据，不重入 Open/Close 生命周期。
        public IEnumerator ApplyData_AfterOpen_ReplacesDataWithoutLifecycleReentry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<TestDataPanel>()));
                TestData initial = new TestData(1);
                TestData replacement = new TestData(2);

                TestDataPanel panel =
                    await service.CreatePanelAsync<TestDataPanel, TestData>(initial, "panel", "reapply-page");
                Assert.That(panel.Current, Is.SameAs(initial));
                Assert.That(panel.ApplyCount, Is.EqualTo(1));
                Assert.That(panel.OpenCount, Is.EqualTo(1));

                panel.ApplyData(replacement);

                Assert.That(panel.Current, Is.SameAs(replacement));
                Assert.That(panel.ApplyCount, Is.EqualTo(2));
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.CloseCount, Is.EqualTo(0));
                Assert.That(panel.IsOpen, Is.True);
                service.Dispose();
            });
        }

        [UnityTest]
        // 架构：数据交付时机 可交互面板打开后再次应用数据只替换数据，不重入 Open/Close 生命周期。
        public IEnumerator ApplyData_AfterOpen_OnInteractivePanel_ReplacesDataWithoutLifecycleReentry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UIRoot root = CreateRoot();
                GUIService service = new GUIService(root, new FactoryLoader(CreatePrefab<TestDataInteractivePanel>()));
                TestData initial = new TestData(3);
                TestData replacement = new TestData(4);

                TestDataInteractivePanel panel =
                    await service.CreatePanelAsync<TestDataInteractivePanel, TestData>(
                        initial, "panel", "reapply-interactive-page");
                Assert.That(panel.Current, Is.SameAs(initial));
                Assert.That(panel.ApplyCount, Is.EqualTo(1));
                Assert.That(panel.OpenCount, Is.EqualTo(1));

                panel.ApplyData(replacement);

                Assert.That(panel.Current, Is.SameAs(replacement));
                Assert.That(panel.ApplyCount, Is.EqualTo(2));
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.CloseCount, Is.EqualTo(0));
                Assert.That(panel.IsOpen, Is.True);
                service.Dispose();
            });
        }

        [Test]
        public void UIRoot_ResolvesAllFiveLayers()
        {
            UIRoot root = CreateRoot();

            Assert.That(root.GetLayer(GUILayer.HUD).name, Is.EqualTo("HUDLayer"));
            Assert.That(root.GetLayer(GUILayer.Page).name, Is.EqualTo("PageLayer"));
            Assert.That(root.GetLayer(GUILayer.Modal).name, Is.EqualTo("ModalLayer"));
            Assert.That(root.GetLayer(GUILayer.Transition).name, Is.EqualTo("TransitionLayer"));
            Assert.That(root.GetLayer(GUILayer.Toast).name, Is.EqualTo("ToastLayer"));
            Assert.That(root.GetLayer(GUILayer.None), Is.Null);
        }

        private UIRoot CreateRoot()
        {
            GameObject rootObject = Track(new GameObject("UIRoot"));
            return rootObject.AddComponent<UIRoot>();
        }

        private GameObject CreatePrefab<T>() where T : Component
        {
            GameObject prefab = Track(new GameObject(typeof(T).Name));
            prefab.AddComponent<T>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objects.Add(target);
            return target;
        }

        private sealed class FactoryLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public FactoryLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int LoadCount { get; private set; }

            public async UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                LoadCount++;
                await UniTask.Yield(cancellationToken);
                return prefab;
            }

            public void Release(GameObject releasedPrefab)
            {
            }
        }

        private sealed class FactoryPanel : Panel
        {
        }

        private sealed class OtherPanel : Panel
        {
        }

        private sealed class TestData
        {
            public TestData(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private sealed class TestDataPanel : Panel<TestData>
        {
            public TestData Current { get; private set; }
            public int ApplyCount { get; private set; }
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override void OnApplyData(TestData data)
            {
                Current = data;
                ApplyCount++;
            }

            protected override void OnOpen()
            {
                OpenCount++;
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }

        private sealed class TestDataInteractivePanel : InteractivePanel<TestData>
        {
            public TestData Current { get; private set; }
            public int ApplyCount { get; private set; }
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override void OnApplyData(TestData data)
            {
                Current = data;
                ApplyCount++;
            }

            protected override void OnOpen()
            {
                OpenCount++;
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }
    }
}
