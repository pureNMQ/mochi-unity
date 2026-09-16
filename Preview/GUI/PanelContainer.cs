using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class PanelContainer : Panel
    {
        private IPanelLoader loader;

        public bool IsBusy { get; protected set; }

        protected IPanelLoader Loader => loader;
        //销毁/Busy 关闭接管的入口最先标记不可用（不新增销毁状态字段）
        public bool IsDisposed { get; protected set; }

        //隐藏期标志：容器隐藏期间导航只推进逻辑，不执行子 Panel OnShow 与容器侧视觉
        //（Backdrop 淡入淡出）；根容器 Initialize 成功即逻辑显示（默认 false）
        private bool suppressVisuals;

        //当前是否处于隐藏期（true=只推进逻辑、跳过视觉步骤）
        protected bool SuppressVisuals => suppressVisuals;

        //导航器用于跳过对已初始化容器的重复 Initialize（根 Layer 自动重建时需要）
        internal bool IsInitialized => loader != null;

        public virtual void Initialize(IPanelLoader panelLoader)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (panelLoader == null)
            {
                throw new ArgumentNullException(nameof(panelLoader));
            }

            if (loader != null)
            {
                throw new InvalidOperationException($"{GetType().Name} is already initialized.");
            }

            loader = panelLoader;
        }

        //容器级显示入口：恢复视觉、容器自身逻辑显示并同步呈现最终栈；
        //过渡由各 Panel 自行 fire-and-forget，输入与焦点随 Panel 显示即时启用
        public UniTask ShowAsync()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (IsBusy)
            {
                return UniTask.CompletedTask;
            }

            suppressVisuals = false;
            Show();
            PresentOnShow();
            return UniTask.CompletedTask;
        }

        //容器级隐藏入口：先置容器自身逻辑隐藏，再向所管理 Panel 幂等传播即时隐藏
        //（停用即停止在途视觉），不为隐藏执行 Close、不释放栈中资源
        public override void Hide()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            suppressVisuals = true;
            base.Hide();
            PropagateHideToStack();
        }

        //运行时容器的实例终终权归持有方：Close 仅做逻辑收尾与栈清理，不随关闭自毁
        protected override bool DestroySelfOnClose => false;

        //向栈内全部条目传播 Hide 请求；调用方不判断 IsVisible，由 Panel 幂等决定，
        //条目实例可能已被外部销毁，仅存活实例执行生命周期调用
        protected abstract void PropagateHideToStack();

        //容器显示的呈现流程由具体容器实现——Page 只呈现当前页，
        //Modal 按多层展示规则恢复所需层并同步 Backdrop
        protected abstract void PresentOnShow();

        //生命周期 Token 由具体容器持有（销毁/接管取消源），基类经抽象成员访问
        protected abstract CancellationToken LifetimeCancellationToken { get; }

        protected override void OnDestroy()
        {
            IsDisposed = true;
            base.OnDestroy();
        }
    }
}