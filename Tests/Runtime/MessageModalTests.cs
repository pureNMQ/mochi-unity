using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Mochi.Unity.GUI.Tests
{
    public class MessageModalTests
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
        public IEnumerator MessageModal_Prepare_BindsDisplayTexts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));

                MessageModal modal = await container.PushAsync<
                    MessageModal,
                    MessageModalData,
                    MessageModalResult>(
                    "message",
                    new MessageModalData(
                        "Save data",
                        "The counter is not persisted.",
                        "Understood"));

                Assert.That(modal, Is.Not.Null);
                Assert.That(FindText(modal.transform, "TitleText").text, Is.EqualTo("Save data"));
                Assert.That(FindText(modal.transform, "MessageText").text, Is.EqualTo("The counter is not persisted."));
                Assert.That(FindText(modal.transform, "AcknowledgeButtonText").text, Is.EqualTo("Understood"));
            });
        }

        [UnityTest]
        public IEnumerator MessageModal_Acknowledge_ReturnsAcknowledgedOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));
                int pushCallbackCount = 0;
                int eventCount = 0;
                bool hasResult = false;
                MessageModalResult result = default;

                MessageModal modal = await container.PushAsync<
                    MessageModal,
                    MessageModalData,
                    MessageModalResult>(
                    "message",
                    new MessageModalData("Save data", "The counter is not persisted.", "Understood"),
                    (valid, value) =>
                    {
                        pushCallbackCount++;
                        hasResult = valid;
                        result = value;
                    });
                modal.ResultReturned += (valid, value) => eventCount++;

                GetField<Button>(modal, "acknowledgeButton").onClick.Invoke();
                GetField<Button>(modal, "acknowledgeButton").onClick.Invoke();
                await UniTask.WaitUntil(() => container.StackCount == 0)
                    .Timeout(System.TimeSpan.FromSeconds(2));

                Assert.That(hasResult, Is.True);
                Assert.That(result, Is.EqualTo(MessageModalResult.Acknowledged));
                Assert.That(pushCallbackCount, Is.EqualTo(1));
                Assert.That(eventCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator MessageModal_ExternalPop_ReturnsNoResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));
                bool hasResult = true;
                MessageModalResult result = MessageModalResult.Acknowledged;

                await container.PushAsync<MessageModal, MessageModalData, MessageModalResult>(
                    "message",
                    new MessageModalData("Save data", "The counter is not persisted.", "Understood"),
                    (valid, value) =>
                    {
                        hasResult = valid;
                        result = value;
                    });

                Assert.That(await container.BackAsync(), Is.True);
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.EqualTo(default(MessageModalResult)));
            });
        }

        [UnityTest]
        public IEnumerator MessageModal_MissingRequiredBinding_FailsClearly()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab(includeMessageText: false);
                container.Initialize(new RecordingLoader(prefab));
                InvalidOperationException caught = null;

                try
                {
                    await container.PushAsync<MessageModal, MessageModalData, MessageModalResult>(
                        "message",
                        new MessageModalData("Save data", "The counter is not persisted.", "Understood"));
                }
                catch (InvalidOperationException exception)
                {
                    caught = exception;
                }

                Assert.That(caught, Is.Not.Null);
                Assert.That(caught.Message, Does.Contain("messageText"));
            });
        }

        private GameObject CreatePrefab(bool includeMessageText = true)
        {
            GameObject prefab = Track(new GameObject("MessageModalPrefab", typeof(RectTransform)));
            prefab.SetActive(false);
            MessageModal modal = prefab.AddComponent<MessageModal>();

            TMP_Text titleText = CreateText(prefab.transform, "TitleText");
            TMP_Text messageText = includeMessageText
                ? CreateText(prefab.transform, "MessageText")
                : null;
            Button acknowledgeButton = CreateButton(
                prefab.transform,
                "AcknowledgeButton",
                out TMP_Text acknowledgeButtonText);

            SetField(modal, "titleText", titleText);
            SetField(modal, "messageText", messageText);
            SetField(modal, "acknowledgeButton", acknowledgeButton);
            SetField(modal, "acknowledgeButtonText", acknowledgeButtonText);
            return prefab;
        }

        private static TValue GetField<TValue>(MessageModal modal, string name)
        {
            FieldInfo field = typeof(MessageModal).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"MessageModal must serialize a '{name}' binding.");
            return (TValue)field.GetValue(modal);
        }

        private static TMP_Text CreateText(Transform parent, string name)
        {
            GameObject textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            return textObject.GetComponent<TMP_Text>();
        }

        private static Button CreateButton(Transform parent, string name, out TMP_Text label)
        {
            GameObject buttonObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            label = CreateText(buttonObject.transform, name + "Text");
            return buttonObject.GetComponent<Button>();
        }

        private static TMP_Text FindText(Transform root, string name)
        {
            return root.Find(name)?.GetComponent<TMP_Text>()
                   ?? root.Find(name.Replace("ButtonText", "Button/" + name))?.GetComponent<TMP_Text>();
        }

        private static void SetField<TValue>(MessageModal modal, string name, TValue value)
        {
            FieldInfo field = typeof(MessageModal).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"MessageModal must serialize a '{name}' binding.");
            field.SetValue(modal, value);
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
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
    }
}
