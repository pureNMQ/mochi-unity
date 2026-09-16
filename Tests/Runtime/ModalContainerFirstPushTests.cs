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
    public class ModalContainerFirstPushTests
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
        public IEnumerator PushFirstModal_ReturnsModalAndCompletesCallbackAfterPop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();

                GameObject prefab = Track(new GameObject("ModalPrefab"));
                prefab.SetActive(false);
                prefab.AddComponent<TestModal>();
                RecordingLoader loader = new RecordingLoader(prefab);
                container.Initialize(loader);

                bool callbackCalled = false;
                bool hasResult = true;
                string callbackResult = "unexpected";
                TestModal modal = await container.PushAsync<TestModal, string>(
                    "modal",
                    new ModalResultCallback<string>((resultIsValid, result) =>
                    {
                        callbackCalled = true;
                        hasResult = resultIsValid;
                        callbackResult = result;
                    }));

                Assert.That(modal, Is.Not.Null);
                Assert.That(container.Current, Is.SameAs(modal));
                Assert.That(container.StackCount, Is.EqualTo(1));
                Assert.That(callbackCalled, Is.False);

                bool popped = await container.BackAsync();

                Assert.That(popped, Is.True);
                Assert.That(callbackCalled, Is.True);
                Assert.That(hasResult, Is.False);
                Assert.That(callbackResult, Is.Null);
                Assert.That(container.StackCount, Is.Zero);
                //等一帧确保延迟销毁生效
                await UniTask.Yield();
                Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            });
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class TestModal : ModalPanel<string>
        {
            public void Complete(string result)
            {
                CompleteAsync(result).Forget();
            }
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public RecordingLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
                ReleaseCount++;
            }
        }
    }
}
