using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    public enum ConfirmModalResult
    {
        Confirmed,
        Cancelled
    }

    public sealed class ConfirmModalData
    {
        public ConfirmModalData(
            string title,
            string message,
            string confirmButtonText,
            string cancelButtonText)
        {
            Title = title;
            Message = message;
            ConfirmButtonText = confirmButtonText;
            CancelButtonText = cancelButtonText;
        }

        public string Title { get; }
        public string Message { get; }
        public string ConfirmButtonText { get; }
        public string CancelButtonText { get; }
    }

    public sealed class ConfirmModal : ModalPanel<ConfirmModalData, ConfirmModalResult>
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmButtonText;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text cancelButtonText;

        protected override UniTask OnPrepareAsync(
            ConfirmModalData data,
            CancellationToken cancellationToken)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            ValidateRequiredBindings();
            if (titleText != null)
            {
                titleText.text = data.Title ?? string.Empty;
            }

            messageText.text = data.Message ?? string.Empty;
            confirmButtonText.text = data.ConfirmButtonText ?? string.Empty;
            cancelButtonText.text = data.CancelButtonText ?? string.Empty;
            return UniTask.CompletedTask;
        }

        protected override void OnOpen()
        {
            confirmButton.onClick.AddListener(Confirm);
            cancelButton.onClick.AddListener(Cancel);
        }

        protected override void OnClose()
        {
            confirmButton?.onClick.RemoveListener(Confirm);
            cancelButton?.onClick.RemoveListener(Cancel);
        }

        private void Confirm()
        {
            CompleteAsync(ConfirmModalResult.Confirmed).Forget();
        }

        private void Cancel()
        {
            CompleteAsync(ConfirmModalResult.Cancelled).Forget();
        }

        private void ValidateRequiredBindings()
        {
            RequireBinding(messageText, nameof(messageText));
            RequireBinding(confirmButton, nameof(confirmButton));
            RequireBinding(confirmButtonText, nameof(confirmButtonText));
            RequireBinding(cancelButton, nameof(cancelButton));
            RequireBinding(cancelButtonText, nameof(cancelButtonText));
        }

        private void RequireBinding(UnityEngine.Object binding, string fieldName)
        {
            if (binding == null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} requires a configured '{fieldName}' binding.");
            }
        }
    }
}
