using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.Preview.GUI.Tests
{
    //工厂契约用例：CreateAsync 完成到 OnOpen 的全部流程（configure→Prepare→OnOpen），实例保持失活；
    //加载失败或缺组件返回 null；Prepare 抛错/取消由工厂收尾后原样上抛
    public class PanelFactoryTests
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
        public IEnumerator Create_CompletesThroughOnOpen_ReturnsInactiveOpenedInstanceWithHandle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader, "key", parent, null, null, CancellationToken.None);

                //完成打开但保持失活：显示与入场由调用方决定
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.IsOpen, Is.True);
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.transform.parent, Is.EqualTo(parent));
                Assert.That(panel.gameObject.activeSelf, Is.False);

                //句柄注入后销毁自释放：恰好一次、释放来源 Prefab
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { prefab }));
            });
        }

        [UnityTest]
        public IEnumerator Create_RunsConfigureThenPrepareThenOpen_InOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;
                List<string> order = new List<string>();

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader,
                    "key",
                    parent,
                    candidate =>
                    {
                        candidate.Order = order;
                        order.Add("configure");
                    },
                    (candidate, token) =>
                    {
                        order.Add("prepare");
                        return UniTask.CompletedTask;
                    },
                    CancellationToken.None);

                Assert.That(order, Is.EqualTo(new[] { "configure", "prepare", "open" }));
                Assert.That(panel.IsOpen, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Create_LoaderReturnsNull_ReturnsNullWithoutSideEffects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                NullLoader loader = new NullLoader();
                Transform parent = Track(new GameObject("Parent")).transform;

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader, "key", parent, null, null, CancellationToken.None);

                Assert.That(panel, Is.Null);
                Assert.That(loader.ReleaseCount, Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator Create_MissingPanelComponent_DestroysInstanceAndReleasesPrefab()
        {
            return UniTask.ToCoroutine(async () =>
            {
                //Prefab 上没有 Panel 组件：错误契约收敛在工厂——销毁错配实例并释放 Prefab，返回 null
                GameObject prefab = Track(CreatePrefab<CanvasGroupOnlyMarker>("BarePrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader, "key", parent, null, null, CancellationToken.None);

                Assert.That(panel, Is.Null);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { prefab }));

                //错配实例已被工厂销毁，不残留在挂载父节点下（含失活对象）
                await UniTask.Yield();
                Assert.That(parent.GetComponentsInChildren<CanvasGroupOnlyMarker>(true).Length, Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator Create_PrepareThrows_CleansUpInstanceAndPropagatesException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;
                InvalidOperationException thrown = null;

                try
                {
                    await PanelFactory.CreateAsync<FactoryPage>(
                        loader,
                        "key",
                        parent,
                        null,
                        (candidate, token) => throw new InvalidOperationException("prepare failed"),
                        CancellationToken.None);
                }
                catch (InvalidOperationException exception)
                {
                    thrown = exception;
                }

                //异常原样上抛；实例已收尾：OnClose 恰好一次、句柄释放恰好一次、实例销毁
                Assert.That(thrown, Is.Not.Null);
                Assert.That(thrown.Message, Is.EqualTo("prepare failed"));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { prefab }));
                await UniTask.Yield();
                Assert.That(parent.GetComponentsInChildren<FactoryPage>(true).Length, Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator Create_PrepareCancelled_CleansUpInstanceAndPropagatesCancellation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;
                CancellationTokenSource cancellation = new CancellationTokenSource();

                OperationCanceledException cancelled = null;
                try
                {
                    await PanelFactory.CreateAsync<FactoryPage>(
                        loader,
                        "key",
                        parent,
                        null,
                        (candidate, token) =>
                        {
                            cancellation.Cancel();
                            return UniTask.CompletedTask;
                        },
                        cancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    cancelled = exception;
                }

                Assert.That(cancelled, Is.Not.Null);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                await UniTask.Yield();
                Assert.That(parent.GetComponentsInChildren<FactoryPage>(true).Length, Is.EqualTo(0));
                cancellation.Dispose();
            });
        }

        [UnityTest]
        public IEnumerator Destroy_PanelWithoutHandle_DoesNotThrowOrRelease()
        {
            return UniTask.ToCoroutine(async () =>
            {
                //既有测试路径：直接构造 Panel、不经工厂，无句柄时销毁必须安全
                CountingLoader loader = new CountingLoader(null);
                FactoryPage panel = Track(new GameObject("DirectPanel")).AddComponent<FactoryPage>();

                Assert.DoesNotThrow(() => UnityEngine.Object.DestroyImmediate(panel.gameObject));
                Assert.That(loader.ReleaseCount, Is.EqualTo(0));
                await UniTask.Yield();
            });
        }

        //占位组件类型：只用于构造“无 Panel 组件”的 Prefab
        private sealed class CanvasGroupOnlyMarker : MonoBehaviour
        {
        }

        private sealed class FactoryPage : Panel
        {
            public List<string> Order { get; set; }
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override void OnOpen()
            {
                OpenCount++;
                Order?.Add("open");
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }

        private GameObject CreatePrefab<TComponent>(string name, bool active) where TComponent : Component
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(active);
            prefab.AddComponent<TComponent>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class CountingLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public CountingLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int ReleaseCount { get; private set; }
            public List<GameObject> ReleasedPrefabs { get; } = new List<GameObject>();

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
                ReleasedPrefabs.Add(loadedPrefab);
            }
        }

        private sealed class NullLoader : IPanelLoader
        {
            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }
    }
}
