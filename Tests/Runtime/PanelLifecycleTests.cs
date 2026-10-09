using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class PanelLifecycleTests
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

        // 验收条件 AC1A:当面板经创建到显示，则按 Prepare→Open→Show 顺序回调，且 OnOpen 执行时面板处于激活状态。
        [UnityTest]
        public IEnumerator CreateToShow_PrepareOpenShowInOrder_ActiveDuringOpen()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<LifecyclePanel>("LifecyclePrefab"));
                StubLoader loader = new StubLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                LifecyclePanel panel = await PanelFactory.CreateAsync<LifecyclePanel>(
                    loader, "key", parent, new ServiceProvider(), CancellationToken.None);

                panel.Show();

                Assert.That(panel.Order, Is.EqualTo(new[] { "prepare", "open", "show" }));
                Assert.That(panel.ActiveDuringOpen, Is.True);
            });
        }

        // 验收条件 AC20A:当面板出生，则已持有服务容器引用（panel.Services 可用）。
        [UnityTest]
        public IEnumerator Prepare_HasInjectedServiceProvider()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<ServiceProbePanel>("ServiceProbePrefab"));
                StubLoader loader = new StubLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;
                ServiceProvider services = new ServiceProvider();
                services.Register(new MarkerService("expected"));

                ServiceProbePanel panel = await PanelFactory.CreateAsync<ServiceProbePanel>(
                    loader, "key", parent, services, CancellationToken.None);

                Assert.That(panel.Services, Is.SameAs(services));
                Assert.That(panel.ServicesDuringPrepare, Is.SameAs(services));
                Assert.That(panel.MarkerValueDuringPrepare, Is.EqualTo("expected"));

                ServiceProvider previousRoot = Services.Root;
                try
                {
                    Services.Root = null;
                    GameObject legacyPrefab = Track(CreatePrefab<LifecyclePanel>("LegacyLifecyclePrefab"));
                    LifecyclePanel legacyPanel = await PanelFactory.CreateAsync<LifecyclePanel>(
                        new StubLoader(legacyPrefab), "legacy", parent, CancellationToken.None);

                    Assert.That(legacyPanel.ServicesDuringPrepare, Is.Not.Null);
                    Assert.That(legacyPanel.Services, Is.SameAs(legacyPanel.ServicesDuringPrepare));
                }
                finally
                {
                    Services.Root = previousRoot;
                }
            });
        }

        private sealed class LifecyclePanel : Panel
        {
            public List<string> Order { get; } = new List<string>();
            public bool? ActiveDuringOpen { get; private set; }
            public ServiceProvider ServicesDuringPrepare { get; private set; }

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                ServicesDuringPrepare = Services;
                Order.Add("prepare");
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                Order.Add("open");
                ActiveDuringOpen = gameObject.activeSelf;
            }

            protected override void OnShow()
            {
                Order.Add("show");
            }
        }

        private sealed class ServiceProbePanel : Panel
        {
            public ServiceProvider ServicesDuringPrepare { get; private set; }
            public string MarkerValueDuringPrepare { get; private set; }

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                ServicesDuringPrepare = Services;
                MarkerValueDuringPrepare = Services.Get<MarkerService>().Value;
                return UniTask.CompletedTask;
            }
        }

        private sealed class MarkerService
        {
            public MarkerService(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private GameObject CreatePrefab<TPanel>(string name) where TPanel : Panel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(true);
            prefab.AddComponent<TPanel>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class StubLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public StubLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }
    }
}
