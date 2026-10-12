using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    //过渡取消用例：关闭打断在途过渡时旧过渡被取消并实际停止，逻辑提交先于退场完成
    public class PanelTransitionCancellationTests
    {
        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                //场景内自毁的面板只留下失效引用，销毁前过滤
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }

            objectsToDestroy.Clear();
        }

        // 验收条件 AC21A:当过渡动画在播期间发生关闭，则旧动画被取消、逻辑提交不等待退场完成。
        [UnityTest]
        public IEnumerator CloseDuringTransition_CancelsPreviousAndCommitsBeforeExitCompletes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await CloseWhileEnterInFlightAsync();
                await CloseWhileHideExitInFlightAsync();
            });
        }

        private async UniTask CloseWhileEnterInFlightAsync()
        {
            RecordingLoader loader = new RecordingLoader();
            loader.Register("panel", CreatePrefab("CloseDuringEnterPrefab"));

            TransitionPanel panel = await PanelFactory.CreateAsync<TransitionPanel>(
                loader, "panel", null, new ServiceProvider(), CancellationToken.None);
            Track(panel.gameObject);
            //桩在工厂返回后挂到实例上再 Show：门控等运行时状态不随 Instantiate 复制
            ControlledTransition transition = panel.gameObject.AddComponent<ControlledTransition>();

            panel.Show();
            TransitionCall enterCall = transition.EnterCalls[0];

            int closedCount = 0;
            panel.Closed += _ => closedCount++;

            panel.Close();

            //关闭退场仍被挂起：此刻的状态与 Closed 通知证明逻辑提交不等视觉完成
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(panel.IsVisible, Is.False);
            Assert.That(closedCount, Is.EqualTo(1));
            Assert.That(panel == null, Is.False);

            await UniTask.Yield();

            //旧入场过渡实际停止：令牌取消，等待以取消异常终止且从未正常完成
            Assert.That(enterCall.Token.IsCancellationRequested, Is.True);
            Assert.That(enterCall.Cancelled, Is.True);
            Assert.That(enterCall.Completed, Is.False);

            TransitionCall closeExitCall = transition.ExitCalls[0];
            Assert.That(closeExitCall.Completed, Is.False);
            Assert.That(closeExitCall.Cancelled, Is.False);

            //释放关闭退场：完成自毁与句柄释放，不留存活实例
            closeExitCall.Release();
            await UniTask.Yield();

            Assert.That(panel == null, Is.True);
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
        }

        private async UniTask CloseWhileHideExitInFlightAsync()
        {
            RecordingLoader loader = new RecordingLoader();
            loader.Register("panel", CreatePrefab("CloseDuringHideExitPrefab"));

            TransitionPanel panel = await PanelFactory.CreateAsync<TransitionPanel>(
                loader, "panel", null, new ServiceProvider(), CancellationToken.None);
            Track(panel.gameObject);
            ControlledTransition transition = panel.gameObject.AddComponent<ControlledTransition>();

            panel.Show();
            transition.EnterCalls[0].Release();
            await UniTask.Yield();
            //基线：入场已正常完成，在途过渡只剩 Hide 的退场
            Assert.That(transition.EnterCalls[0].Completed, Is.True);

            panel.Hide();
            TransitionCall hideExitCall = transition.ExitCalls[0];

            int closedCount = 0;
            panel.Closed += _ => closedCount++;

            panel.Close();

            //关闭退场仍被挂起：此刻的状态与 Closed 通知证明逻辑提交不等视觉完成
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(panel.IsVisible, Is.False);
            Assert.That(closedCount, Is.EqualTo(1));
            Assert.That(panel == null, Is.False);

            await UniTask.Yield();

            //Hide 退场作为旧过渡被取消并实际停止；Close 自己的退场是独立一次等待
            Assert.That(hideExitCall.Token.IsCancellationRequested, Is.True);
            Assert.That(hideExitCall.Cancelled, Is.True);
            Assert.That(hideExitCall.Completed, Is.False);

            TransitionCall closeExitCall = transition.ExitCalls[1];
            Assert.That(closeExitCall.Completed, Is.False);
            Assert.That(closeExitCall.Cancelled, Is.False);

            closeExitCall.Release();
            await UniTask.Yield();

            Assert.That(panel == null, Is.True);
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
        }

        private GameObject CreatePrefab(string name)
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            prefab.AddComponent<TransitionPanel>();
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

        private sealed class TransitionPanel : Panel
        {
        }

        private sealed class ControlledTransition : MonoBehaviour, IPanelTransition
        {
            public List<TransitionCall> EnterCalls { get; } = new List<TransitionCall>();
            public List<TransitionCall> ExitCalls { get; } = new List<TransitionCall>();

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                TransitionCall call = new TransitionCall(cancellationToken);
                EnterCalls.Add(call);
                return call.WaitAsync();
            }

            public UniTask ExitAsync(CancellationToken cancellationToken)
            {
                TransitionCall call = new TransitionCall(cancellationToken);
                ExitCalls.Add(call);
                return call.WaitAsync();
            }
        }

        private sealed class TransitionCall
        {
            private readonly UniTaskCompletionSource gate = new UniTaskCompletionSource();

            public TransitionCall(CancellationToken cancellationToken)
            {
                Token = cancellationToken;
            }

            public CancellationToken Token { get; }

            public bool Completed { get; private set; }

            public bool Cancelled { get; private set; }

            //受控等待：gate 决定正常完成，令牌决定取消，两条路径都真实终止等待
            public async UniTask WaitAsync()
            {
                try
                {
                    await gate.Task.AttachExternalCancellation(Token);
                    Completed = true;
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }

            public void Release()
            {
                gate.TrySetResult();
            }
        }
    }
}
