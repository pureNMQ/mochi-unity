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
        protected override CancellationToken LifetimeCancellationToken => lifetimeCancellation.Token;

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

        protected void ClearStackEntries()
        {
            stackEntries.Clear();
        }

        protected sealed override void OnDestroy()
        {
            if (IsDisposed)
            {
                //已被 Busy 关闭接管标记为不可用的容器：条目已被清栈、在途操作已被取消，
                //这里仅补齐生命周期资源释放，不再重复清栈；正常 Close 不标记 IsDisposed，
                //关闭后再销毁仍走完整时序，但栈已空、清栈为空操作
                lifetimeCancellation.Dispose();
                base.OnDestroy();
                return;
            }

            //F4 销毁时序：入口最先标记不可用（复用 IsDisposed，不新增销毁状态字段），
            //使公开导航入口立即拒绝新操作、在途 Back/Push 的异步收尾据此跳过条目清理；
            //随后取消进行中的异步操作，再交由具体容器清栈并交付通知
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

        //F4 收尾（2026-09-12）：正常 Close 触发本容器 OnClose 且本容器 Busy 时立即接管，不排队/挂起：
        //先标记不可用并取消在途操作（与销毁共用所有权规则），在途 Back/Push 的异步收尾
        //据此跳过条目清理与索引移除；条目清栈、资源释放与通知交付由调用方 OnClose 完成
        protected void BeginBusyTakeOver()
        {
            IsDisposed = true;
            lifetimeCancellation.Cancel();
        }

        protected abstract void CleanupStackOnDestroy();

        //C1：向栈内全部条目传播 Hide 请求（由 PanelContainer.Hide 调用）。
        //调用方不判断 IsVisible，由 Panel.HideInternal 幂等决定是否实际执行 OnHide；
        //沿用 R2 风格 Unity 判空：条目实例可能已被外部销毁，仅存活实例执行生命周期调用
        protected override void PropagateHideToStack()
        {
            for (int index = 0; index < StackCount; index++)
            {
                Panel panel = GetStackEntry(index).Panel;
                if (panel != null)
                {
                    panel.HideInternal();
                }
            }
        }

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
