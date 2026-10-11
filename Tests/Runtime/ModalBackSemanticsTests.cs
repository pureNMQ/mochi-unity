using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    //R5+F5 新契约语义用例：Back 单条目、清理后通知、失败不回滚、拒绝不通知、Push 失败集中收尾与显式焦点补偿
    public class ModalBackSemanticsTests
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

        [Test]
        public void PublicApi_BackAsyncReplacesPopAsync()
        {
            //R5：批量 PopAsync(int) 删除且不保留兼容空壳，公开 API 收窄为 BackAsync()
            Assert.That(
                typeof(ModalContainer).GetMethod(
                    "PopAsync",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static),
                Is.Null);
            Assert.That(
                typeof(ModalContainer).GetMethod(
                    "BackAsync",
                    BindingFlags.Public | BindingFlags.Instance),
                Is.Not.Null);
            //回滚路径一并删除：Pop 确定关闭，不再恢复 Modal
            Assert.That(
                typeof(ModalContainer).GetMethod(
                    "RollbackPopAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                Is.Null);
        }

        [UnityTest]
        public IEnumerator Notification_DeliveredAfterBusyClearsAndCleanupCompletes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out KeyedRecordingLoader loader);
                bool busyAtCallback = true;
                int stackAtCallback = -1;
                int releaseAtCallback = -1;
                int callbackCount = 0;
                bool hasResult = false;
                string result = null;

                NoDataModal modal = await container.PushAsync<NoDataModal, string>(
                    "ModalPrefab",
                    new ModalResultCallback<string>((valid, value) =>
                    {
                        callbackCount++;
                        busyAtCallback = container.IsBusy;
                        stackAtCallback = container.StackCount;
                        releaseAtCallback = loader.ReleaseCount;
                        hasResult = valid;
                        result = value;
                    }));

                Assert.That(await modal.CompleteResultAsync("accepted"), Is.True);

                //R5：通知在实例清理、资源释放、出栈、Backdrop 更新、Busy 解除之后按条目交付
                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(busyAtCallback, Is.False);
                Assert.That(stackAtCallback, Is.Zero);
                Assert.That(releaseAtCallback, Is.EqualTo(1));
                Assert.That(hasResult, Is.True);
                Assert.That(result, Is.EqualTo("accepted"));
                Assert.That(container.StackCount, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator Back_AfterInstanceDestroyedExternally_StillNotifiesByEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out KeyedRecordingLoader loader);
                int callbackCount = 0;
                bool hasResult = true;
                string result = "unexpected";
                NoDataModal modal = await container.PushAsync<NoDataModal, string>(
                    "ModalPrefab",
                    new ModalResultCallback<string>((valid, value) =>
                    {
                        callbackCount++;
                        hasResult = valid;
                        result = value;
                    }));

                //实例在 Back 前已被外部销毁：回调由栈条目持有，交付不依赖 Panel 存活
                UnityEngine.Object.DestroyImmediate(modal.gameObject);
                Assert.That(modal == null, Is.True);

                Assert.That(await container.BackAsync(), Is.True);

                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.Zero);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Back_Rejection_ProducesNoNotification()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ModalContainer container = CreateContainer(out KeyedRecordingLoader loader);
                List<string> notifications = new List<string>();

                //空栈拒绝
                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(notifications, Is.Empty);

                NoDataModal modal = await container.PushAsync<NoDataModal, string>(
                    "ModalPrefab",
                    new ModalResultCallback<string>((valid, value) => notifications.Add($"{valid}:{value}")));

                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));

                //栈清空后的请求同样拒绝且不产生新通知（Busy 拒绝不通知由 Behavior 用例覆盖）
                Assert.That(await container.BackAsync(), Is.False);
                Assert.That(notifications, Is.EqualTo(new[] { "False:" }));
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Back_RestoresNextTopInteractionAndFocus()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Track(new GameObject("EventSystem", typeof(EventSystem)));
                GameObject focusA = Track(new GameObject("FocusA"));
                GameObject focusB = Track(new GameObject("FocusB"));
                GameObject prefabA = Track(CreatePrefab<FocusModal>("A"));
                GameObject prefabB = Track(CreatePrefab<FocusModal>("B"));
                SetDefaultFocusTarget(prefabA.GetComponent<FocusModal>(), focusA);
                SetDefaultFocusTarget(prefabB.GetComponent<FocusModal>(), focusB);

                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                container.Initialize(new KeyedRecordingLoader(prefabA, prefabB));

                FocusModal first = await container.PushAsync<FocusModal>("A");
                FocusModal second = await container.PushAsync<FocusModal>("B");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(focusB));

                Assert.That(await container.BackAsync(), Is.True);

                //幂等化后 Show 不再隐式恢复焦点，Back 完成后对仍可见的 nextTop 显式补偿
                Assert.That(container.Current, Is.SameAs(first));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(focusA));
            });
        }

        [UnityTest]
        public IEnumerator Push_WhenEnterThrows_CommitsAndLeavesErrorToTransitionComponent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Track(new GameObject("EventSystem", typeof(EventSystem)));
                GameObject focusFirst = Track(new GameObject("FocusFirst"));
                GameObject goodPrefab = Track(CreatePrefab<FocusModal>("Good"));
                GameObject badPrefab = Track(CreatePrefab<EnterFailureModal>("Bad"));
                SetDefaultFocusTarget(goodPrefab.GetComponent<FocusModal>(), focusFirst);

                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                KeyedRecordingLoader loader = new KeyedRecordingLoader(goodPrefab, badPrefab);
                container.Initialize(loader);

                FocusModal first = await container.PushAsync<FocusModal>("Good");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(focusFirst));

                List<string> notifications = new List<string>();
                //新契约：入场过渡 fire-and-forget，Enter 抛错归组件——Push 照常提交
                LogAssert.Expect(LogType.Exception, new Regex("EnterFailureException"));
                EnterFailureModal second = await container.PushAsync<EnterFailureModal, string>(
                    "Bad",
                    new ModalResultCallback<string>((valid, value) => notifications.Add($"{valid}:{value}")));

                Assert.That(second, Is.Not.Null);
                Assert.That(container.IsBusy, Is.False);
                Assert.That(container.StackCount, Is.EqualTo(2));
                Assert.That(container.Current, Is.SameAs(second));
                //下层 Modal 保持可见但不可交互；新栈顶接管焦点（D1）
                Assert.That(first.gameObject.activeSelf, Is.True);
                Assert.That(first.CanvasGroup.interactable, Is.False);
                Assert.That(second.CanvasGroup.interactable, Is.True);

                await UniTask.DelayFrame(1);
            });
        }

        private ModalContainer CreateContainer(out KeyedRecordingLoader loader)
        {
            GameObject prefab = Track(CreatePrefab<NoDataModal>("ModalPrefab"));
            GameObject containerObject = Track(new GameObject("ModalContainer"));
            ModalContainer container = containerObject.AddComponent<ModalContainer>();
            loader = new KeyedRecordingLoader(prefab);
            container.Initialize(loader);
            return container;
        }

        private GameObject CreatePrefab<TModal>(string name) where TModal : ModalPanel
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TModal>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private static void SetDefaultFocusTarget(Panel panel, GameObject target)
        {
            FieldInfo field = typeof(Panel).GetField(
                "defaultFocusTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(panel, target);
        }

        private sealed class KeyedRecordingLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public KeyedRecordingLoader(params GameObject[] prefabList)
            {
                foreach (GameObject prefab in prefabList)
                {
                    prefabs.Add(prefab.name, prefab);
                }
            }

            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[key]);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        private sealed class NoDataModal : ModalPanel<string>
        {
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();

            public UniTask<bool> CompleteResultAsync(string result)
            {
                return CompleteAsync(result);
            }
        }

        private sealed class FocusModal : ModalPanel
        {
            public CanvasGroup CanvasGroup => GetComponent<CanvasGroup>();
        }

        private sealed class EnterFailureModal : ModalPanel<string>, IPanelTransition
        {
            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                throw new EnterFailureException();
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class EnterFailureException : Exception
        {
        }
    }
}
