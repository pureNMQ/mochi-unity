using System.Collections.Generic;
using System.Threading;

namespace Mochi.Unity.Preview.GUI
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

        //入栈即监听面板关闭：面板未经容器自行关闭时，容器在关闭之后清理
        //（与容器驱动路径的“先清理后关闭”互为倒序）
        protected void AddStackEntry(Panel panel)
        {
            panel.Closed += HandlePanelClosed;
            stackEntries.Add(new StackEntry(panel));
        }

        //移出栈即解除监听：容器驱动关闭先移出栈解除关系、再关闭面板，
        //关闭事件不再回流容器
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
                //已被 Busy 关闭接管的容器：条目已清栈、在途操作已取消，仅补齐资源释放；
                //正常 Close 不标记 IsDisposed，关闭后再销毁仍走完整时序（栈已空，清栈为空操作）
                lifetimeCancellation.Dispose();
                base.OnDestroy();
                return;
            }

            //销毁时序：入口最先标记不可用（复用 IsDisposed），公开导航入口立即拒绝新操作、
            //在途收尾跳过条目清理；随后取消在途操作，再交由具体容器清栈并交付通知
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

        //正常 Close 触发本容器 OnClose 且 Busy 时立即接管（不排队）：先标记不可用并取消在途
        //操作（与销毁共用所有权规则），在途收尾跳过条目清理；清栈与通知交付由调用方 OnClose 完成
        protected void BeginBusyTakeOver()
        {
            IsDisposed = true;
            lifetimeCancellation.Cancel();
        }

        protected abstract void CleanupStackOnDestroy();

        //向栈内全部条目传播 Hide 请求（由 PanelContainer.Hide 调用）：是否实际执行 OnHide
        //由 Panel 幂等决定；条目实例可能已被外部销毁，仅存活实例执行生命周期调用
        protected override void PropagateHideToStack()
        {
            for (int index = 0; index < StackCount; index++)
            {
                Panel panel = GetStackEntry(index).Panel;
                if (panel != null)
                {
                    panel.Hide();
                }
            }
        }

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
