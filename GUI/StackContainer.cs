using System.Collections.Generic;
using System.Threading;

namespace Mochi.Unity.GUI
{
    public abstract class StackContainer : PanelContainer
    {
        private readonly List<StackEntry> stackEntries = new List<StackEntry>();
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();

        public int StackCount => stackEntries.Count;
        protected override CancellationToken LifetimeCancellationToken => lifetimeCancellation.Token;

        protected StackEntry GetStackEntry(int index)
        {
            return stackEntries[index];
        }

        protected void AddStackEntry(Panel panel)
        {
            panel.Closed += HandlePanelClosed;
            stackEntries.Add(new StackEntry(panel));
        }

        protected void RemoveStackEntryAt(int index)
        {
            Panel panel = stackEntries[index].Panel;
            if (panel != null)
            {
                panel.Closed -= HandlePanelClosed;
            }

            stackEntries.RemoveAt(index);
        }

        protected void ClearStackEntries()
        {
            stackEntries.Clear();
        }

        protected sealed override void OnDestroy()
        {
            if (IsDisposed)
            {
                lifetimeCancellation.Dispose();
                base.OnDestroy();
                return;
            }


            IsDisposed = true;
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


        protected void BeginBusyTakeOver()
        {
            IsDisposed = true;
            lifetimeCancellation.Cancel();
        }

        protected abstract void CleanupStackOnDestroy();

        //面板未经容器自行关闭：抽出该条目（其余条目顺序不变）后交由派生容器补充自身清理
        private void HandlePanelClosed(Panel panel)
        {
            panel.Closed -= HandlePanelClosed;
            if (IsDisposed)
            {
                return;
            }

            int index = IndexOfPanel(panel);
            if (index < 0)
            {
                return;
            }

            bool wasTop = index == StackCount - 1;
            RemoveStackEntryAt(index);
            HandleSelfClosedPanel(panel, wasTop);
        }

        //自行关闭面板的容器侧清理：条目已抽出、顺序保持，派生容器按需恢复栈顶状态
        protected abstract void HandleSelfClosedPanel(Panel panel, bool wasTop);

        private int IndexOfPanel(Panel panel)
        {
            for (int index = StackCount - 1; index >= 0; index--)
            {
                if (ReferenceEquals(GetStackEntry(index).Panel, panel))
                {
                    return index;
                }
            }

            return -1;
        }

        protected sealed class StackEntry
        {
            public StackEntry(Panel panel)
            {
                Panel = panel;
            }

            public Panel Panel { get; }
        }
    }
}
