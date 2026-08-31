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
        public IEnumerator FadeTransition_EnterAndExitReachFinalState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject pageObject = Track(new GameObject("FadePage"));
                pageObject.SetActive(false);
                CanvasGroup canvasGroup = pageObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0f;
                FadePanelTransition transition = pageObject.AddComponent<FadePanelTransition>();
                transition.Duration = 0.02f;

                await transition.EnterAsync(CancellationToken.None);

                Assert.That(pageObject.activeSelf, Is.True);
                Assert.That(canvasGroup.alpha, Is.EqualTo(1f).Within(0.001f));

                await transition.ExitAsync(CancellationToken.None);

                Assert.That(pageObject.activeSelf, Is.False);
                Assert.That(canvasGroup.alpha, Is.EqualTo(0f).Within(0.001f));
            });
        }

        [UnityTest]
        public IEnumerator FadeTransition_CancellationPropagatesAndDoesNotDeactivatePage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject pageObject = Track(new GameObject("FadePage"));
                CanvasGroup canvasGroup = pageObject.AddComponent<CanvasGroup>();
                FadePanelTransition transition = pageObject.AddComponent<FadePanelTransition>();
                transition.Duration = 10f;

                canvasGroup.alpha = 0f;
                pageObject.SetActive(false);
                CancellationTokenSource enterCancellation = new CancellationTokenSource();
                UniTask enter = transition.EnterAsync(enterCancellation.Token);
                await UniTask.Yield();
                enterCancellation.Cancel();

                Assert.That(await CaptureExceptionAsync(enter), Is.TypeOf<OperationCanceledException>());
                Assert.That(pageObject.activeSelf, Is.True);
                Assert.That(canvasGroup.alpha, Is.LessThan(1f));
                enterCancellation.Dispose();

                canvasGroup.alpha = 1f;
                pageObject.SetActive(true);
                CancellationTokenSource exitCancellation = new CancellationTokenSource();
                UniTask exit = transition.ExitAsync(exitCancellation.Token);
                await UniTask.Yield();
                exitCancellation.Cancel();

                Assert.That(await CaptureExceptionAsync(exit), Is.TypeOf<OperationCanceledException>());
                Assert.That(pageObject.activeSelf, Is.True);
                Assert.That(canvasGroup.alpha, Is.GreaterThan(0f));
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
