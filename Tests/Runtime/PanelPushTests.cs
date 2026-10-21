using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>面板推入入口验收用例：地址必填、创建后挂载与可等待的弹窗结果。</summary>
    public sealed class PanelPushTests
    {
        //结果通道若失效会表现为永久 await：以超时把挂起转成可读失败，避免整轮测试卡死
        private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(5);

        private readonly List<GameObject> objects = new List<GameObject>();
        private FakeLoader loader;
        private GUIService gui;
        private UIRoot root;

        [TearDown]
        public void TearDown()
        {
            gui?.Dispose();
            gui = null;

            foreach (GameObject gameObject in objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            objects.Clear();
        }

        [Test]
        // 验收条件 AC14：当推入调用未提供资源地址，则立即抛出异常，不按类型名或映射推测地址。
        public void PushWithoutAddress_ThrowsArgumentException()
        {
            PanelStackService service = CreateService();

            Assert.Throws<ArgumentException>(() => service.PushPageAsync<MenuTestPage>());
            Assert.Throws<ArgumentException>(() => service.PushPageAsync<DataTestPage, DataTestPageData>(new DataTestPageData()));
            Assert.Throws<ArgumentException>(() => service.PushModalAsync<ConfirmTestModal, int, bool>(7));

            //异常发生在创建之前：加载层不得收到任何按类型名兜底的请求
            Assert.IsEmpty(loader.RequestedKeys);
        }

        [UnityTest]
        // 验收条件 AC24A: 应用层不接触 UI 细节即可开关页与弹窗。
        public IEnumerator PushAsync_MountsPanelsIntoBoundLayers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PanelStackService service = CreateService();
                loader.Register("MenuPanel", CreatePrefab<MenuTestPage>("MenuPanel"));
                loader.Register("ConfirmPanel", CreatePrefab<ConfirmTestModal>("ConfirmPanel"));

                MenuTestPage page = await service.PushPageAsync<MenuTestPage>("MenuPanel").Timeout(AwaitTimeout);
                Assert.IsNotNull(page);
                Assert.AreSame(root.Page, page.transform.parent);
                Assert.IsTrue(page.IsVisible);
                Assert.AreEqual(1, service.PageCount);

                ConfirmTestModal modal = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(7, "ConfirmPanel")
                    .Timeout(AwaitTimeout);
                Assert.IsNotNull(modal);
                Assert.AreSame(root.Modal, modal.transform.parent);
                Assert.AreEqual(7, modal.Payload);
                Assert.AreEqual(1, service.ModalCount);
                Assert.IsFalse(page.IsInteractable);
                Assert.IsTrue(page.gameObject.activeSelf);
            });
        }

        [UnityTest]
        // 验收条件 AC25A: 弹窗结果可 await，外部关闭返回取消信号。
        public IEnumerator ModalResult_AwaitReturnsSelectionAndExternalCloseCancels()
        {
            return UniTask.ToCoroutine(async () =>
            {
                PanelStackService service = CreateService();
                loader.Register("ConfirmPanel", CreatePrefab<ConfirmTestModal>("ConfirmPanel"));

                ConfirmTestModal accepted = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(1, "ConfirmPanel")
                    .Timeout(AwaitTimeout);
                UniTask<ModalResult<bool>> acceptedResult = service.GetModalResult<ConfirmTestModal, bool>(accepted);
                //经服务的可等待结果路径完成，而非面板结果通道
                bool completed = service.CompleteModal(accepted, true);
                ModalResult<bool> result = await acceptedResult.Timeout(AwaitTimeout);
                Assert.IsTrue(
                    result.HasResult,
                    "completed=" + completed + " hasResult=" + result.HasResult + " value=" + result.Result
                    + " modalCount=" + service.ModalCount);
                Assert.IsTrue(result.Result);
                Assert.AreEqual(0, service.ModalCount);

                ConfirmTestModal cancelled = await service
                    .PushModalAsync<ConfirmTestModal, int, bool>(2, "ConfirmPanel")
                    .Timeout(AwaitTimeout);
                UniTask<ModalResult<bool>> cancelledResult = service.GetModalResult<ConfirmTestModal, bool>(cancelled);
                cancelled.Close();
                ModalResult<bool> cancelledOutcome = await cancelledResult.Timeout(AwaitTimeout);
                Assert.IsFalse(cancelledOutcome.HasResult);
                Assert.IsFalse(cancelledOutcome.Result);
                Assert.AreEqual(0, service.ModalCount);
            });
        }

        private PanelStackService CreateService()
        {
            foreach (UIRoot leftover in Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(leftover.gameObject);
            }

            root = Track(new GameObject("UIRoot")).AddComponent<UIRoot>();
            loader = new FakeLoader();
            gui = new GUIService(root, loader);

            GameObject serviceObject = Track(new GameObject("PanelStackService"));
            PanelStackService service = serviceObject.AddComponent<PanelStackService>();
            service.BindLayers(gui);
            return service;
        }

        private GameObject CreatePrefab<TPanel>(string name) where TPanel : InteractivePanel
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

        private sealed class MenuTestPage : InteractivePanel
        {
        }

        private sealed class DataTestPageData
        {
        }

        private sealed class DataTestPage : InteractivePanel<DataTestPageData>
        {
        }

        private sealed class ConfirmTestModal : ModalPanel<int, bool>
        {
            public int Payload { get; private set; }

            protected override void OnApplyData(int data)
            {
                Payload = data;
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
