using System;
using System.Collections.Generic;

namespace Mochi.Unity.GUI
{
    /// <summary>Owns the ordered Modal stack and synchronizes entries with panel closure.</summary>
    public sealed class ModalService
    {
        private readonly List<ModalPanel> stack = new List<ModalPanel>();

        /// <summary>Gets the number of modals currently in the stack.</summary>
        public int Count => stack.Count;
        /// <summary>Gets the current stack order from bottom to top.</summary>
        public IReadOnlyList<ModalPanel> Stack => stack;
        /// <summary>Gets the top modal, or null when the stack is empty.</summary>
        public ModalPanel Current => stack.Count == 0 ? null : stack[stack.Count - 1];

        /// <summary>Adds a modal to the top of the stack.</summary>
        public void Push(ModalPanel modal)
        {
            if (modal == null)
            {
                throw new ArgumentNullException(nameof(modal));
            }

            if (stack.Contains(modal))
            {
                throw new InvalidOperationException("The modal is already in the modal stack.");
            }

            modal.Closed += OnModalClosed;
            stack.Add(modal);
        }

        /// <summary>Closes a modal wherever it occurs in the stack.</summary>
        public bool Close(ModalPanel modal)
        {
            if (modal == null || !stack.Contains(modal))
            {
                return false;
            }

            modal.Close();
            return true;
        }

        /// <summary>Closes only the top modal.</summary>
        public bool Back()
        {
            ModalPanel modal = Current as ModalPanel;
            if (modal == null)
            {
                return false;
            }

            modal.Close();
            return true;
        }

        private void OnModalClosed(Panel modal)
        {
            modal.Closed -= OnModalClosed;
            stack.Remove(modal as ModalPanel);
        }
    }
}
