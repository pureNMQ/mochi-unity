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
    //统一覆盖层容器用例：Open 创建即显示并登记、幂等、查询、Close 后可重建、
    //容器销毁强杀归属实例、不托管显隐；GUINavigator 根 Layer 接入与自动重建
    public class OverlayContainerTests
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
        public IEnumerator Open_CreatesShowsAndRegistersPanel()
        {
            return UniTask.ToCoroutine(async () =>
            {
                OverlayContainer container = CreateContainer(out RecordingLoader loader);
                loader.Register("hud", CreatePrefab("HudPrefab"));

                OverlayPanel panel = await container.OpenAsync<OverlayPanel>("hud");

                //创建即显示：状态提交、实例挂载、输入即时启用（D5/D1）
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.IsOpen, Is.True);
                Assert.That(panel.IsVisible, Is.True);
                Assert.That(panel.gameObject.activeSelf, Is.True);
                Assert.That(panel.transform.parent, Is.SameAs(container.transform));
                Assert.That(panel.CanvasGroup.interactable, Is.True);
                Assert.That(container.IsBusy, Is.False);

                Assert.That(container.TryGetPanel("hud", out Panel resolved), Is.True);
                Assert.That(resolved, Is.SameAs(panel));
                Assert.That(container.TryGetPanel<OverlayPanel>("hud", out OverlayPanel typed), Is.True);
                Assert.That(typed, Is.SameAs(panel));
                Assert.That(container.TryGetPanel("missing", out _), Is.False);
            });
        }

        [UnityTest]
        public IEnumerator Open_RegisteredKey_IsIdempotent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                OverlayContainer container = CreateContainer(out RecordingLoader loader);
                loader.Register("hud", CreatePrefab("HudPrefab"));

                OverlayPanel first = await container.OpenAsync<OverlayPanel>("hud");
                OverlayPanel second = await container.OpenAsync<OverlayPanel>("hud");

                //D4：已登记 key 幂等返回既有实例，不重复创建、不重复加载
                Assert.That(second, Is.SameAs(first));
                Assert.That(loader.LoadCount, Is.EqualTo(1));
                Assert.That(container.transform.childCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator ClosedPanel_UnregistersAndSameKeyRebuilds()
        {
            return UniTask.ToCoroutine(async () =>
            {
                OverlayContainer container = CreateContainer(out RecordingLoader loader);
                loader.Register("hud", CreatePrefab("HudPrefab"));

                OverlayPanel first = await container.OpenAsync<OverlayPanel>("hud");

                //面板自行关闭：登记随逻辑关闭即时除名（不等延迟销毁）
                first.Close();
                Assert.That(container.TryGetPanel("hud", out _), Is.False);

                //同 key 可重建：新实例独立创建与显示
                OverlayPanel rebuilt = await container.OpenAsync<OverlayPanel>("hud");
                Assert.That(rebuilt, Is.Not.SameAs(first));
                Assert.That(rebuilt.IsVisible, Is.True);
                Assert.That(loader.LoadCount, Is.EqualTo(2));

                await UniTask.Yield();
                Assert.That(first == null, Is.True);
                //旧实例恰好释放一次，新实例句柄仍在持有
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator ContainerHide_DoesNotManageRegisteredPanels()
        {
            return UniTask.ToCoroutine(async () =>
            {
                OverlayContainer container = CreateContainer(out RecordingLoader loader);
                loader.Register("hud", CreatePrefab("HudPrefab"));
                OverlayPanel panel = await container.OpenAsync<OverlayPanel>("hud");

                //不托管 Show/Hide：容器隐藏不传播到登记面板，显隐由 Panel 公开 API 决定
                container.Hide();

                Assert.That(container.IsVisible, Is.False);
                Assert.That(panel.IsVisible, Is.True);

                container.Show();
                Assert.That(container.IsVisible, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator ContainerDestroy_ForceKillsOwnedPanels()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("OverlayContainer"));
                OverlayContainer container = containerObject.AddComponent<OverlayContainer>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("hud", CreatePrefab("HudPrefab"));
                loader.Register("notice", CreatePrefab("NoticePrefab"));
                container.Initialize(loader);

                OverlayPanel hud = await container.OpenAsync<OverlayPanel>("hud");
                OverlayPanel notice = await container.OpenAsync<OverlayPanel>("notice");

                //D6：容器销毁对所有归属 Panel 强杀——立即销毁并释放，不等退场动画
                UnityEngine.Object.DestroyImmediate(containerObject);
                await UniTask.Yield();

                Assert.That(hud == null, Is.True);
                Assert.That(notice == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator Navigator_ProvidesHudAndNotificationLayersInSortOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject root = Track(new GameObject("GUIRoot"));
                GUINavigator navigator = root.AddComponent<GUINavigator>();
                navigator.Initialize(new NullLoader());

                //根 Layer 顺序：HUD → Page → Modal → Notification（同层级按兄弟索引排序）
                Assert.That(navigator.HUD, Is.Not.Null);
                Assert.That(navigator.Notifications, Is.Not.Null);
                Assert.That(navigator.HUD.transform.parent, Is.SameAs(navigator.Pages.transform.parent));
                string[] siblingNames = GetSiblingNames(navigator.Pages.transform.parent);
                Assert.That(siblingNames, Is.EqualTo(new[]
                {
                    "HUDLayer", "PageContainer", "ModalContainer", "NotificationLayer"
                }));

                //两个根 Layer 各持一个实例，互不共享登记
                Assert.That(navigator.HUD, Is.Not.SameAs(navigator.Notifications));
            });
        }

        [UnityTest]
        public IEnumerator Navigator_RebuildsDestroyedLayerAutomatically()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject root = Track(new GameObject("GUIRoot"));
                GUINavigator navigator = root.AddComponent<GUINavigator>();
                RecordingLoader loader = new RecordingLoader();
                loader.Register("hud", CreatePrefab("HudPrefab"));
                navigator.Initialize(loader);

                OverlayContainer original = navigator.HUD;
                UnityEngine.Object.DestroyImmediate(original.gameObject);
                await UniTask.Yield();
                Assert.That(original == null, Is.True);

                //发现被销毁即自动重建：沿用既有 Loader，回到原排序位
                OverlayContainer rebuilt = navigator.HUD;
                Assert.That(rebuilt, Is.Not.Null);
                Assert.That(rebuilt, Is.Not.SameAs(original));
                Assert.That(rebuilt.transform.GetSiblingIndex(), Is.EqualTo(0));

                OverlayPanel panel = await rebuilt.OpenAsync<OverlayPanel>("hud");
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.IsVisible, Is.True);
            });
        }

        private OverlayContainer CreateContainer(out RecordingLoader loader)
        {
            GameObject containerObject = Track(new GameObject("OverlayContainer"));
            OverlayContainer container = containerObject.AddComponent<OverlayContainer>();
            loader = new RecordingLoader();
            container.Initialize(loader);
            return container;
        }

        private GameObject CreatePrefab(string name)
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<OverlayPanel>();
            return prefab;
        }

        private static string[] GetSiblingNames(Transform parent)
        {
            string[] names = new string[parent.childCount];
            for (int index = 0; index < parent.childCount; index++)
            {
                names[index] = parent.GetChild(index).name;
            }

            return names;
        }

        private GameObject Track(GameObject target)
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

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public int LoadCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public void Register(string key, GameObject prefab)
            {
                prefabs[key] = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                LoadCount++;
                return UniTask.FromResult(prefabs[key]);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        private sealed class OverlayPanel : Panel
        {
        }
    }
}
