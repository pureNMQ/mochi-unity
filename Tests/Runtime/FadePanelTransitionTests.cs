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
    public class FadePanelTransitionTests
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
        public IEnumerator FadeTransition_EnterAndExitReachFinalAlpha()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject pageObject = Track(new GameObject("FadePage"));
                CanvasGroup canvasGroup = pageObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0f;
                FadePanelTransition transition = pageObject.AddComponent<FadePanelTransition>();
                transition.Duration = 0.02f;

                await transition.EnterAsync(CancellationToken.None);
                Assert.That(canvasGroup.alpha, Is.EqualTo(1f).Within(0.0001f));

                await transition.ExitAsync(CancellationToken.None);
                Assert.That(canvasGroup.alpha, Is.EqualTo(0f).Within(0.0001f));

                //激活与停用归 Panel，过渡不改激活状态
                Assert.That(pageObject.activeSelf, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator FadeTransition_CancellationPropagatesWithoutTouchingActivation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject pageObject = Track(new GameObject("FadePage"));
                CanvasGroup canvasGroup = pageObject.AddComponent<CanvasGroup>();
                FadePanelTransition transition = pageObject.AddComponent<FadePanelTransition>();
                transition.Duration = 10f;

                canvasGroup.alpha = 0f;
                CancellationTokenSource enterCancellation = new CancellationTokenSource();
                UniTask enter = transition.EnterAsync(enterCancellation.Token);
                await UniTask.Yield();
                enterCancellation.Cancel();

                Assert.That(await CaptureExceptionAsync(enter), Is.TypeOf<OperationCanceledException>());
                Assert.That(pageObject.activeSelf, Is.True);
                enterCancellation.Dispose();

                canvasGroup.alpha = 1f;
                CancellationTokenSource exitCancellation = new CancellationTokenSource();
                UniTask exit = transition.ExitAsync(exitCancellation.Token);
                await UniTask.Yield();
                exitCancellation.Cancel();

                Assert.That(await CaptureExceptionAsync(exit), Is.TypeOf<OperationCanceledException>());
                Assert.That(pageObject.activeSelf, Is.True);
                exitCancellation.Dispose();
            });
        }

        private static async UniTask<Exception> CaptureExceptionAsync(UniTask operation)
        {
            try
            {
                await operation;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }
    }
}
