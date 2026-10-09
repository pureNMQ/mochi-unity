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
        public IEnumerator Create_CompletesThroughOnOpen_ReturnsActiveOpenedInstanceWithHandle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader, "key", parent, CancellationToken.None);

                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.IsOpen, Is.True);
                Assert.That(panel.OpenCount, Is.EqualTo(1));
                Assert.That(panel.transform.parent, Is.EqualTo(parent));
                Assert.That(panel.gameObject.activeSelf, Is.True);

                //句柄注入后销毁自释放：恰好一次、释放来源 Prefab
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                Assert.That(loader.ReleasedPrefabs, Is.EqualTo(new[] { prefab }));
            });
        }

        [UnityTest]
        public IEnumerator Create_RunsPrepareThenOpen_InOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject prefab = Track(CreatePrefab<FactoryPage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                FactoryPage panel = await PanelFactory.CreateAsync<FactoryPage>(
                    loader, "key", parent, CancellationToken.None);

                Assert.That(panel.Order, Is.EqualTo(new[] { "prepare", "open" }));
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
                    loader, "key", parent, CancellationToken.None);

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
                    loader, "key", parent, CancellationToken.None);

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
                GameObject prefab = Track(CreatePrefab<ThrowingPreparePage>("FactoryPrefab", active: true));
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;
                InvalidOperationException thrown = null;

                try
                {
                    await PanelFactory.CreateAsync<ThrowingPreparePage>(
                        loader, "key", parent, CancellationToken.None);
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
                GameObject prefab = Track(CreatePrefab<CancellingPreparePage>("FactoryPrefab", active: true));
                CancellationTokenSource cancellation = new CancellationTokenSource();
                CancellingPreparePage.SharedSource = cancellation;
                CountingLoader loader = new CountingLoader(prefab);
                Transform parent = Track(new GameObject("Parent")).transform;

                OperationCanceledException cancelled = null;
                try
                {
                    await PanelFactory.CreateAsync<CancellingPreparePage>(
                        loader, "key", parent, cancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    cancelled = exception;
                }
                finally
                {
                    CancellingPreparePage.SharedSource = null;
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

        private class FactoryPage : Panel
        {
            public List<string> Order { get; } = new List<string>();
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                Order.Add("prepare");
                return UniTask.CompletedTask;
            }

            protected override void OnOpen()
            {
                OpenCount++;
                Order.Add("open");
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }

        private sealed class ThrowingPreparePage : FactoryPage
        {
            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("prepare failed");
            }
        }

        //Instantiate 不复制非序列化引用，取消源只能经静态共享给实例
        private sealed class CancellingPreparePage : FactoryPage
        {
            public static CancellationTokenSource SharedSource { get; set; }

            protected override UniTask OnPrepareAsync(CancellationToken cancellationToken)
            {
                SharedSource.Cancel();
                return UniTask.CompletedTask;
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
