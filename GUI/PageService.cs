using System;
using System.Collections.Generic;

namespace Mochi.Unity.GUI
{
    /// <summary>Owns the ordered Page stack and synchronizes entries with panel closure.</summary>
    public sealed class PageService
    {
        private readonly List<Panel> stack = new List<Panel>();

        /// <summary>Gets the number of pages currently in the stack.</summary>
        public int Count => stack.Count;
        /// <summary>Gets the current stack order from bottom to top.</summary>
        public IReadOnlyList<Panel> Stack => stack;
        /// <summary>Gets the top page, or null when the stack is empty.</summary>
        public Panel Current => stack.Count == 0 ? null : stack[stack.Count - 1];
        /// <summary>Raised after the last page has been removed.</summary>
        public event Action StackEmptied;

        /// <summary>Adds a page to the top of the stack.</summary>
        public void Push(Panel page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            if (stack.Contains(page))
            {
                throw new InvalidOperationException("The page is already in the page stack.");
            }

            page.Closed += OnPageClosed;
            stack.Add(page);
        }

        /// <summary>Closes a page wherever it occurs in the stack.</summary>
        public bool Close(Panel page)
        {
            if (page == null || !stack.Contains(page))
            {
                return false;
            }

            page.Close();
            return true;
        }

        /// <summary>Closes the top page and succeeds even when it is the last page.</summary>
        public bool Back()
        {
            Panel page = Current;
            if (page == null)
            {
                return false;
            }

            page.Close();
            return true;
        }

        private void OnPageClosed(Panel page)
        {
            page.Closed -= OnPageClosed;
            int index = stack.IndexOf(page);
            if (index < 0)
            {
                return;
            }

            bool wasTop = index == stack.Count - 1;
            stack.RemoveAt(index);
            if (wasTop && Current != null && !Current.IsVisible)
            {
                Current.Show();
            }

            if (stack.Count == 0)
            {
                StackEmptied?.Invoke();
            }
        }
    }
}
