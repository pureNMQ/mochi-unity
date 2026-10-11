using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    public enum MessageModalResult
    {
        Acknowledged
    }

    public sealed class MessageModalData
    {
        public MessageModalData(string title, string message, string acknowledgeButtonText)
        {
            Title = title;
            Message = message;
            AcknowledgeButtonText = acknowledgeButtonText;
        }

        public string Title { get; }
        public string Message { get; }
        public string AcknowledgeButtonText { get; }
    }

    public sealed class MessageModal : ModalPanel<MessageModalData, MessageModalResult>
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button acknowledgeButton;
        [SerializeField] private TMP_Text acknowledgeButtonText;

        protected override UniTask OnPrepareAsync(
            MessageModalData data,
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
            acknowledgeButtonText.text = data.AcknowledgeButtonText ?? string.Empty;
            return UniTask.CompletedTask;
        }

        protected override void OnOpen()
        {
            acknowledgeButton.onClick.AddListener(Acknowledge);
        }

        protected override void OnClose()
        {
            acknowledgeButton?.onClick.RemoveListener(Acknowledge);
        }

        private void Acknowledge()
        {
            CompleteAsync(MessageModalResult.Acknowledged).Forget();
        }

        private void ValidateRequiredBindings()
        {
            RequireBinding(messageText, nameof(messageText));
            RequireBinding(acknowledgeButton, nameof(acknowledgeButton));
            RequireBinding(acknowledgeButtonText, nameof(acknowledgeButtonText));
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
