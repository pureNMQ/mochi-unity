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
    //Panel 公开同步契约用例：状态即时提交、过渡 fire-and-forget 不阻塞调用方、
    //Close 自毁自释放、closing 重入拒绝、强杀句柄必释放、Shown/Hidden/Closed 事件
    public class PanelTransitionContractTests
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
        public IEnumerator Show_CommitsStateImmediatelyAndDoesNotBlockCaller()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                GameObject prefab = CreatePrefab("EnterHeldPrefab", withTransition: true);
                prefab.GetComponent<CountingTransition>().HoldEnter = true;
                loader.Register("panel", prefab);

                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                //同步 Show 立即返回：入场动画被挂起也不阻塞调用方
                panel.Show();

                Assert.That(panel.IsVisible, Is.True);
                Assert.That(panel.gameObject.activeSelf, Is.True);
                //D1：输入随状态立即启用（动画播放期间即可交互）
                Assert.That(panel.CanvasGroup.interactable, Is.True);
                CountingTransition transition = panel.GetComponent<CountingTransition>();
                Assert.That(transition.EnterCount, Is.EqualTo(1));

                transition.ReleaseEnter();
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator Hide_CommitsStateImmediatelyAndDeactivatesInstance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                loader.Register("panel", CreatePrefab("HidePrefab", withTransition: true));
                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                panel.Show();
                panel.Hide();

                //执行即提交：状态与停用同步生效，退场过渡 fire-and-forget 由组件自行追赶
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel.gameObject.activeSelf, Is.False);
                Assert.That(panel.GetComponent<CountingTransition>().ExitCount, Is.EqualTo(1));

                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator Close_SelfDestroysAndReleasesHandleAfterExit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                GameObject prefab = CreatePrefab("ExitHeldPrefab", withTransition: true);
                prefab.GetComponent<CountingTransition>().HoldExit = true;
                loader.Register("panel", prefab);

                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);
                panel.Show();

                panel.Close();

                //逻辑关闭即时提交；退场动画挂起期间实例存活（视觉追赶），句柄未释放
                Assert.That(panel.IsOpen, Is.False);
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel == null, Is.False);
                Assert.That(loader.ReleaseCount, Is.EqualTo(0));

                //动画播完：自毁并自释放句柄
                panel.GetComponent<CountingTransition>().ReleaseExit();
                await UniTask.Yield();
                Assert.That(panel == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Close_WithoutTransitionDestroysImmediately()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                loader.Register("panel", CreatePrefab("PlainPrefab", withTransition: false));
                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                panel.Close();

                //无过渡组件：无等待步骤，释放与销毁同步完成
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                await UniTask.Yield();
                Assert.That(panel == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator Close_ReentryRejectedDuringClosing()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                GameObject prefab = CreatePrefab("ReentryPrefab", withTransition: true);
                prefab.GetComponent<CountingTransition>().HoldExit = true;
                loader.Register("panel", prefab);

                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);
                panel.Show();

                panel.Close();

                //closing 状态：Show/Hide/Close 重入一律拒绝
                panel.Show();
                panel.Hide();
                panel.Close();

                Assert.That(panel.ShowCount, Is.EqualTo(1));
                Assert.That(panel.HideCount, Is.EqualTo(1));
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                Assert.That(panel.IsVisible, Is.False);
                Assert.That(panel.IsOpen, Is.False);

                panel.GetComponent<CountingTransition>().ReleaseExit();
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator ForceKill_DuringPendingExit_ReleasesHandleExactlyOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                GameObject prefab = CreatePrefab("ForceKillPrefab", withTransition: true);
                prefab.GetComponent<CountingTransition>().HoldExit = true;
                loader.Register("panel", prefab);

                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);
                panel.Show();
                panel.Close();
                Assert.That(loader.ReleaseCount, Is.EqualTo(0));

                //强杀先行：立即销毁不等动画；退场等待被销毁令牌取消，OnDestroy 兜底释放句柄
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
                await UniTask.Yield();

                Assert.That(panel == null, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator LifecycleEvents_NotifySubscribersAfterStateCommit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                loader.Register("panel", CreatePrefab("EventsPrefab", withTransition: false));
                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                List<string> events = new List<string>();
                panel.Shown += changed => events.Add("Shown:" + changed.IsVisible);
                panel.Hidden += changed => events.Add("Hidden:" + changed.IsVisible);
                panel.Closed += changed => events.Add("Closed:" + changed.IsOpen);

                panel.Show();
                panel.Hide();
                panel.Close();

                //事件在状态提交后触发，订阅者读到一致状态；关闭可见面板时同样通知 Hidden
                Assert.That(events, Is.EqualTo(new[]
                {
                    "Shown:True", "Hidden:False", "Closed:False"
                }));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator CloseFromVisible_FiresHiddenThenClosed()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                loader.Register("panel", CreatePrefab("CloseVisiblePrefab", withTransition: false));
                CountingPanel panel = await PanelFactory.CreateAsync<CountingPanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                List<string> events = new List<string>();
                panel.Hidden += _ => events.Add("Hidden");
                panel.Closed += _ => events.Add("Closed");

                panel.Show();
                panel.Close();

                Assert.That(events, Is.EqualTo(new[] { "Hidden", "Closed" }));
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator Close_WhenOnCloseThrows_StillCommitsDestroysAndNotifies()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecordingLoader loader = new RecordingLoader();
                loader.Register("panel", CreatePrefab<ThrowingOnClosePanel>("ThrowingPrefab", withTransition: false));

                ThrowingOnClosePanel panel = await PanelFactory.CreateAsync<ThrowingOnClosePanel>(
                    loader, "panel", null, null, null, CancellationToken.None);

                bool closedNotified = false;
                panel.Closed += _ => closedNotified = true;

                Assert.Throws<InvalidOperationException>(panel.Close);

                //关闭决策不可逆：回调抛错不阻断状态提交、事件通知与自毁释放
                Assert.That(panel.CloseCount, Is.EqualTo(1));
                Assert.That(closedNotified, Is.True);
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
                await UniTask.Yield();
                Assert.That(panel == null, Is.True);
            });
        }

        private GameObject CreatePrefab(string name, bool withTransition)
        {
            return CreatePrefab<CountingPanel>(name, withTransition);
        }

        private GameObject CreatePrefab<TPanel>(string name, bool withTransition) where TPanel : Panel
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            if (withTransition)
            {
                prefab.AddComponent<CountingTransition>();
            }

            prefab.AddComponent<TPanel>();
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public int ReleaseCount { get; private set; }

            public void Register(string key, GameObject prefab)
            {
                prefabs[key] = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[key]);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }

        private class CountingPanel : Panel
        {
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public int CloseCount { get; private set; }

            protected override void OnShow()
            {
                ShowCount++;
            }

            protected override void OnHide()
            {
                HideCount++;
            }

            protected override void OnClose()
            {
                CloseCount++;
            }
        }

        private sealed class ThrowingOnClosePanel : Panel
        {
            public int CloseCount { get; private set; }

            protected override void OnClose()
            {
                CloseCount++;
                throw new InvalidOperationException("OnClose failed.");
            }
        }

        private sealed class CountingTransition : MonoBehaviour, IPanelTransition
        {
            private readonly UniTaskCompletionSource enterGate = new UniTaskCompletionSource();
            private readonly UniTaskCompletionSource exitGate = new UniTaskCompletionSource();
            [SerializeField] private bool holdEnter;
            [SerializeField] private bool holdExit;

            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }

            public bool HoldEnter
            {
                get => holdEnter;
                set => holdEnter = value;
            }

            public bool HoldExit
            {
                get => holdExit;
                set => holdExit = value;
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                EnterCount++;
                return holdEnter ? enterGate.Task.AttachExternalCancellation(cancellationToken) : UniTask.CompletedTask;
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                ExitCount++;
                return holdExit ? exitGate.Task.AttachExternalCancellation(cancellationToken) : UniTask.CompletedTask;
            }

            public void ReleaseEnter()
            {
                enterGate.TrySetResult();
            }

            public void ReleaseExit()
            {
                exitGate.TrySetResult();
            }
        }
    }
}
