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
    /// <summary>面板创建入口、地址映射与可等待弹窗结果的验收用例。</summary>
    public sealed class PanelAddressMappingTests
    {
        //结果通道若失效会表现为永久 await：以超时把挂起转成可读失败，避免整轮测试卡死
        private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(5);

        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in objects)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            objects.Clear();
        }

        [UnityTest]
        // 验收条件 AC19A: 面板地址由服务映射负责，调用方不传资源键。
        public IEnumerator CreateByType_ResolvesAddressFromMap()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PanelAddressMap map = new PanelAddressMap().Map<MappedPage>("MappedPageAddress");
                FakeLoader loader = new FakeLoader();
                GameObject mappedPrefab = CreatePrefab<MappedPage>("MappedPageAddress");
                loader.Register("MappedPageAddress", mappedPrefab);
                //同一类型的第二个地址：用于验证显式地址覆盖映射表
                loader.Register("MappedPageOverride", mappedPrefab);
                loader.Register(nameof(FallbackPage), CreatePrefab<FallbackPage>(nameof(FallbackPage)));
                PanelStackService service = CreateService(loader, map);

                //映射表登记的类型走映射地址
                MappedPage mapped = await service.PushPageAsync<MappedPage>().Timeout(AwaitTimeout);
                Assert.IsNotNull(mapped, "mapped=" + Describe(mapped) + " requested=" + string.Join(",", loader.RequestedKeys));
                Assert.Contains("MappedPageAddress", loader.RequestedKeys);

                //未登记类型回落为类型名
                FallbackPage fallback = await service.PushPageAsync<FallbackPage>().Timeout(AwaitTimeout);
                Assert.IsNotNull(fallback, "fallback=" + Describe(fallback) + " requested=" + string.Join(",", loader.RequestedKeys));

                //显式传入地址时以显式地址为准，忽略映射表
                MappedPage explicitAddress = await service
                    .PushPageAsync<MappedPage>("MappedPageOverride").Timeout(AwaitTimeout);
                Assert.IsNotNull(explicitAddress, "explicit=" + Describe(explicitAddress) + " requested=" + string.Join(",", loader.RequestedKeys));
                Assert.AreEqual(3, loader.RequestedKeys.Count);
                Assert.AreEqual("MappedPageOverride", loader.RequestedKeys[2]);
            });
        }

        [UnityTest]
        // 验收条件 AC24A: 应用层不接触 UI 细节即可开关页与弹窗。
        public IEnumerator CreateAndPush_HidesPanelCreationFromCaller()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FakeLoader loader = new FakeLoader();
                loader.Register("MenuPanel", CreatePrefab<MenuTestPage>("MenuPanel"));
                loader.Register("ConfirmPanel", CreatePrefab<ConfirmTestModal>("ConfirmPanel"));
                PanelStackService service = CreateService(loader, new PanelAddressMap());
                RectTransform pageLayer = (RectTransform)typeof(PanelStackService)
                    .GetField("pageLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(service);
                RectTransform modalLayer = (RectTransform)typeof(PanelStackService)
                    .GetField("modalLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(service);

                MenuTestPage page = await service.PushPageAsync<MenuTestPage>("MenuPanel").Timeout(AwaitTimeout);
                Assert.IsNotNull(page);
                Assert.AreSame(pageLayer, page.transform.parent);
                Assert.IsTrue(page.IsVisible);
                Assert.AreEqual(1, service.PageCount);

                ConfirmTestModal modal = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(7, "ConfirmPanel").Timeout(AwaitTimeout);
                Assert.IsNotNull(modal);
                Assert.AreSame(modalLayer, modal.transform.parent);
                Assert.AreEqual(7, modal.Payload);
                Assert.AreEqual(1, service.ModalCount);
                Assert.IsFalse(page.Interactable);
                Assert.IsTrue(page.gameObject.activeSelf);
            });
        }

        [UnityTest]
        // 验收条件 AC25A: 弹窗结果可 await，外部关闭返回取消信号。
        public IEnumerator ModalResult_AwaitReturnsSelectionAndExternalCloseCancels()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FakeLoader loader = new FakeLoader();
                loader.Register("ConfirmPanel", CreatePrefab<ConfirmTestModal>("ConfirmPanel"));
                PanelStackService service = CreateService(loader, new PanelAddressMap());

                ConfirmTestModal accepted = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(1, "ConfirmPanel").Timeout(AwaitTimeout);
                UniTask<ModalResult<bool>> acceptedResult = service.GetModalResult<ConfirmTestModal, bool>(accepted);
                //经服务的可等待结果路径完成，而非旧容器的容器结果通道
                bool completed = service.CompleteModal(accepted, true);
                ModalResult<bool> result = await acceptedResult.Timeout(AwaitTimeout);
                Assert.IsTrue(
                    result.HasResult,
                    "completed=" + completed + " hasResult=" + result.HasResult + " value=" + result.Result
                    + " modalCount=" + service.ModalCount);
                Assert.IsTrue(result.Result);
                Assert.AreEqual(0, service.ModalCount);

                ConfirmTestModal cancelled = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(2, "ConfirmPanel").Timeout(AwaitTimeout);
                UniTask<ModalResult<bool>> cancelledResult = service.GetModalResult<ConfirmTestModal, bool>(cancelled);
                cancelled.Close();
                ModalResult<bool> cancelledOutcome = await cancelledResult.Timeout(AwaitTimeout);
                Assert.IsFalse(cancelledOutcome.HasResult);
                Assert.IsFalse(cancelledOutcome.Result);
                Assert.AreEqual(0, service.ModalCount);
            });
        }

        private static string Describe(Panel panel)
        {
            return panel == null ? "null" : panel.GetType().Name;
        }

        private PanelStackService CreateService(FakeLoader loader, PanelAddressMap map)
        {
            foreach (UIRoot leftover in UnityEngine.Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            }

            GameObject uiRoot = Track(new GameObject("UIRoot"));
            RectTransform pageLayer = CreateLayer(uiRoot.transform, "PageLayer");
            RectTransform modalLayer = CreateLayer(uiRoot.transform, "ModalLayer");
            CreateLayer(uiRoot.transform, "HUDLayer");

            GameObject serviceObject = Track(new GameObject("PanelStackService"));
            PanelStackService service = serviceObject.AddComponent<PanelStackService>();
            service.BindLayers(pageLayer, modalLayer, loader, null, map);
            return service;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            layer.transform.SetParent(parent, false);
            return layer.GetComponent<RectTransform>();
        }

        private GameObject CreatePrefab<TPanel>(string name) where TPanel : Panel
        {
            GameObject prefab = Track(new GameObject(name, typeof(CanvasGroup)));
            prefab.SetActive(false);
            prefab.AddComponent<TPanel>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objects.Add(target);
            return target;
        }

        private sealed class MappedPage : Panel
        {
        }

        private sealed class FallbackPage : Panel
        {
        }

        private sealed class MenuTestPage : Panel
        {
            public bool Interactable => CanvasGroup != null && CanvasGroup.interactable;
        }

        private sealed class ConfirmTestModal : ModalPanel<int, bool>
        {
            public int Payload { get; private set; }

            protected override UniTask OnPrepareAsync(int data, CancellationToken cancellationToken)
            {
                Payload = data;
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public List<string> RequestedKeys { get; } = new List<string>();

            public void Register(string key, GameObject prefab)
            {
                prefabs[key] = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                RequestedKeys.Add(key);
                return UniTask.FromResult(prefabs.TryGetValue(key, out GameObject prefab) ? prefab : null);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }
    }
}
