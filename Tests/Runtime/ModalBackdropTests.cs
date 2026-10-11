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
    public class ModalBackdropTests
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
        public IEnumerator Initialize_CreatesBackdropOnceAndRejectsReinitialize()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                RecordingLoader loader = new RecordingLoader(Track(CreatePrefab("ModalPrefab", false)));

                //Awake 不再创建 Backdrop，Initialize 之前必须不存在
                Assert.That(container.transform.Find("ModalBackdrop"), Is.Null);

                container.Initialize(loader);

                //Initialize 成功返回后 Backdrop 必定存在且仅有一个
                Assert.That(GetBackdropCount(containerObject.transform), Is.EqualTo(1));
                Transform backdrop = container.transform.Find("ModalBackdrop");
                Assert.That(backdrop, Is.Not.Null);
                Assert.That(backdrop.GetComponent("ModalBackdrop"), Is.Not.Null);

                //重复 Initialize 被基类拒绝，且不会创建第二个 Backdrop
                Assert.Throws<InvalidOperationException>(() => container.Initialize(loader));
                Assert.That(GetBackdropCount(containerObject.transform), Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator PopTop_MoveBackdropBelowNewTopBeforeExitTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                container.Initialize(new KeyedLoader(
                    Track(CreatePrefab("A", false)),
                    Track(CreatePrefab("B", false)),
                    Track(CreatePrefab("C", true))));

                await container.PushAsync<DelayExitModal>("A");
                await container.PushAsync<DelayExitModal>("B");
                DelayExitModal top = await container.PushAsync<DelayExitModal>("C");

                //Push C 后 Backdrop 应位于当前栈顶之下
                Assert.That(
                    GetSiblingNames(containerObject.transform),
                    Is.EqualTo(new[] { "A", "B", "ModalBackdrop", "C" }));

                UniTask<bool> popTask = container.BackAsync();

                //过渡开始前 Backdrop 已移动到新栈顶 B 之下：A, Backdrop, B, C
                Assert.That(
                    GetSiblingNames(containerObject.transform),
                    Is.EqualTo(new[] { "A", "ModalBackdrop", "B", "C" }));

                top.OpenExitGate();
                Assert.That(await popTask, Is.True);
                //Destroy 在帧末执行，等一帧后 C 的实例才真正从层级移除
                await UniTask.DelayFrame(1);
                Assert.That(
                    GetSiblingNames(containerObject.transform),
                    Is.EqualTo(new[] { "A", "ModalBackdrop", "B" }));
            });
        }

        [UnityTest]
        public IEnumerator StackCount_AvailableAndCountAliasRemoved()
        {
            return UniTask.ToCoroutine(async () =>
            {
                //Count 别名已删除，统一使用继承自 StackContainer 的 StackCount
                Assert.That(typeof(ModalContainer).GetProperty("Count"), Is.Null);

                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                container.Initialize(new RecordingLoader(Track(CreatePrefab("ModalPrefab", false))));
                Assert.That(container.StackCount, Is.Zero);

                DelayExitModal modal = await container.PushAsync<DelayExitModal>("modal");
                Assert.That(container.Current, Is.SameAs(modal));
                Assert.That(container.StackCount, Is.EqualTo(1));

                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(container.StackCount, Is.Zero);
            });
        }

        private static int GetBackdropCount(Transform root)
        {
            int count = 0;
            for (int index = 0; index < root.childCount; index++)
            {
                if (root.GetChild(index).name == "ModalBackdrop")
                {
                    count++;
                }
            }

            return count;
        }

        private static string[] GetSiblingNames(Transform root)
        {
            string[] names = new string[root.childCount];
            for (int index = 0; index < root.childCount; index++)
            {
                //实例名会带 (Clone) 后缀，比较时统一剥掉，断言保持可读的语义名
                names[index] = root.GetChild(index).name.Replace("(Clone)", string.Empty);
            }

            return names;
        }

        private GameObject CreatePrefab(string name, bool shouldDelayExit)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<CanvasGroup>();
            DelayExitModal modal = prefab.AddComponent<DelayExitModal>();
            modal.ShouldDelayExit = shouldDelayExit;
            return prefab;
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private sealed class DelayExitModal : ModalPanel<string>, IPanelTransition
        {
            private readonly UniTaskCompletionSource exitGate = new UniTaskCompletionSource();

            public bool ShouldDelayExit { get; set; }

            public void OpenExitGate()
            {
                exitGate.TrySetResult();
            }

            public UniTask EnterAsync(CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public async UniTask ExitAsync(CancellationToken cancellationToken)
            {
                if (!ShouldDelayExit)
                {
                    return;
                }

                await exitGate.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        private sealed class RecordingLoader : IPanelLoader
        {
            private readonly GameObject prefab;

            public RecordingLoader(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefab);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }

        private sealed class KeyedLoader : IPanelLoader
        {
            private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

            public KeyedLoader(params GameObject[] prefabList)
            {
                foreach (GameObject prefab in prefabList)
                {
                    prefabs.Add(prefab.name, prefab);
                }
            }

            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(prefabs[key]);
            }

            public void Release(GameObject loadedPrefab)
            {
            }
        }
    }
}