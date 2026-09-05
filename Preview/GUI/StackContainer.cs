using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class StackContainer : PanelContainer
    {
        private readonly List<StackEntry> stackEntries = new List<StackEntry>();
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();

        public int StackCount => stackEntries.Count;
        protected CancellationToken LifetimeCancellationToken => lifetimeCancellation.Token;

        protected StackEntry GetStackEntry(int index)
        {
            return stackEntries[index];
        }

        protected void AddStackEntry(Panel panel, GameObject prefab)
        {
            stackEntries.Add(new StackEntry(panel, prefab));
        }

        protected void RemoveStackEntryAt(int index)
        {
            stackEntries.RemoveAt(index);
        }

        protected void RemoveStackEntries(int index, int count)
        {
            stackEntries.RemoveRange(index, count);
        }

        protected void ClearStackEntries()
        {
            stackEntries.Clear();
        }

        protected sealed override void OnDestroy()
        {
            if (IsDisposed)
            {
                return;
            }

            IsBusy = false;
            lifetimeCancellation.Cancel();
            try
            {
                CleanupStackOnDestroy();
            }
            finally
            {
                ClearStackEntries();
                lifetimeCancellation.Dispose();
                base.OnDestroy();
            }
        }

        protected abstract void CleanupStackOnDestroy();

        protected sealed class StackEntry
        {
            public StackEntry(Panel panel, GameObject prefab)
            {
                Panel = panel;
                Prefab = prefab;
            }

            public Panel Panel { get; }
            public GameObject Prefab { get; }
        }
    }
}
