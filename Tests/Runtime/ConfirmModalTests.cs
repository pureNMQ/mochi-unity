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

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class ConfirmModalTests
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
        public IEnumerator ConfirmModal_Prepare_BindsDisplayTexts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));

                ConfirmModal modal = await container.PushAsync<
                    ConfirmModal,
                    ConfirmModalData,
                    ConfirmModalResult>(
                    "confirm",
                    new ConfirmModalData(
                        "Delete save?",
                        "This cannot be undone.",
                        "Delete",
                        "Keep"));

                Assert.That(modal, Is.Not.Null);
                Assert.That(FindText(modal.transform, "TitleText").text, Is.EqualTo("Delete save?"));
                Assert.That(FindText(modal.transform, "MessageText").text, Is.EqualTo("This cannot be undone."));
                Assert.That(FindText(modal.transform, "ConfirmButtonText").text, Is.EqualTo("Delete"));
                Assert.That(FindText(modal.transform, "CancelButtonText").text, Is.EqualTo("Keep"));
            });
        }

        [UnityTest]
        public IEnumerator ConfirmModal_Confirm_ReturnsConfirmedOnce()
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
                ConfirmModalResult result = ConfirmModalResult.Cancelled;

                ConfirmModal modal = await container.PushAsync<
                    ConfirmModal,
                    ConfirmModalData,
                    ConfirmModalResult>(
                    "confirm",
                    new ConfirmModalData("Delete save?", "This cannot be undone.", "Delete", "Keep"),
                    (valid, value) =>
                    {
                        pushCallbackCount++;
                        hasResult = valid;
                        result = value;
                    });
                modal.ResultReturned += (valid, value) => eventCount++;

                GetField<Button>(modal, "confirmButton").onClick.Invoke();
                GetField<Button>(modal, "confirmButton").onClick.Invoke();
                await UniTask.WaitUntil(() => container.Count == 0)
                    .Timeout(System.TimeSpan.FromSeconds(2));

                Assert.That(hasResult, Is.True);
                Assert.That(result, Is.EqualTo(ConfirmModalResult.Confirmed));
                Assert.That(pushCallbackCount, Is.EqualTo(1));
                Assert.That(eventCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator ConfirmModal_Cancel_ReturnsCancelledOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));
                bool hasResult = false;
                ConfirmModalResult result = ConfirmModalResult.Confirmed;

                ConfirmModal modal = await container.PushAsync<
                    ConfirmModal,
                    ConfirmModalData,
                    ConfirmModalResult>(
                    "confirm",
                    new ConfirmModalData("Delete save?", "This cannot be undone.", "Delete", "Keep"),
                    (valid, value) =>
                    {
                        hasResult = valid;
                        result = value;
                    });

                GetField<Button>(modal, "cancelButton").onClick.Invoke();
                await UniTask.WaitUntil(() => container.Count == 0)
                    .Timeout(System.TimeSpan.FromSeconds(2));

                Assert.That(hasResult, Is.True);
                Assert.That(result, Is.EqualTo(ConfirmModalResult.Cancelled));
            });
        }

        [UnityTest]
        public IEnumerator ConfirmModal_ExternalPop_ReturnsNoResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                GameObject containerObject = Track(new GameObject("ModalContainer"));
                ModalContainer container = containerObject.AddComponent<ModalContainer>();
                GameObject prefab = CreatePrefab();
                container.Initialize(new RecordingLoader(prefab));
                bool hasResult = true;
                ConfirmModalResult result = ConfirmModalResult.Confirmed;

                await container.PushAsync<ConfirmModal, ConfirmModalData, ConfirmModalResult>(
                    "confirm",
                    new ConfirmModalData("Delete save?", "This cannot be undone.", "Delete", "Keep"),
                    (valid, value) =>
                    {
                        hasResult = valid;
                        result = value;
                    });

                Assert.That(await container.PopAsync(), Is.True);
                Assert.That(hasResult, Is.False);
                Assert.That(result, Is.EqualTo(default(ConfirmModalResult)));
            });
        }

        [UnityTest]
        public IEnumerator ConfirmModal_MissingRequiredBinding_FailsClearly()
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
                    await container.PushAsync<ConfirmModal, ConfirmModalData, ConfirmModalResult>(
                        "confirm",
                        new ConfirmModalData("Delete save?", "This cannot be undone.", "Delete", "Keep"));
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
            GameObject prefab = Track(new GameObject("ConfirmModalPrefab", typeof(RectTransform)));
            prefab.SetActive(false);
            ConfirmModal modal = prefab.AddComponent<ConfirmModal>();

            TMP_Text titleText = CreateText(prefab.transform, "TitleText");
            TMP_Text messageText = includeMessageText
                ? CreateText(prefab.transform, "MessageText")
                : null;
            Button confirmButton = CreateButton(prefab.transform, "ConfirmButton", out TMP_Text confirmButtonText);
            Button cancelButton = CreateButton(prefab.transform, "CancelButton", out TMP_Text cancelButtonText);

            SetField(modal, "titleText", titleText);
            SetField(modal, "messageText", messageText);
            SetField(modal, "confirmButton", confirmButton);
            SetField(modal, "confirmButtonText", confirmButtonText);
            SetField(modal, "cancelButton", cancelButton);
            SetField(modal, "cancelButtonText", cancelButtonText);
            return prefab;
        }

        private static TValue GetField<TValue>(ConfirmModal modal, string name)
        {
            FieldInfo field = typeof(ConfirmModal).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"ConfirmModal must serialize a '{name}' binding.");
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

        private static void SetField<TValue>(ConfirmModal modal, string name, TValue value)
        {
            FieldInfo field = typeof(ConfirmModal).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"ConfirmModal must serialize a '{name}' binding.");
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
