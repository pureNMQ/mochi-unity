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
        //setter 开放给 StackContainer：F4 要求销毁/Busy 关闭接管的入口最先标记不可用（不新增销毁状态字段）
        public bool IsDisposed { get; protected set; }

        //C1 视觉跳过标志：容器隐藏期间置位。隐藏期导航只推进逻辑（加载、生命周期、入栈出栈、
        //清理与通知），不执行子 Panel OnShow、Enter/Exit 动画与 Backdrop 淡入淡出。
        //独立于销毁 CTS 与生命周期状态，不复用 F4 的取消语义、不引入操作队列；
        //根容器 Initialize 成功即逻辑显示，默认 false，首个 Push 正常带视觉。
        //写入收敛在容器级 ShowAsync/Hide 入口，具体容器经只读属性参与分流
        private bool suppressVisuals;

        //C1：当前是否处于隐藏期视觉跳过（true=只推进逻辑、跳过视觉步骤）
        protected bool SuppressVisuals => suppressVisuals;

        //C1 视觉作用域：过渡等待统一挂在链接生命周期 Token 的独立 CTS 上。Hide 时取消当前作用域，
        //在途过渡被停止/跳过（组件契约保证干净停止），被中断的导航逻辑按隐藏期规则继续完成；
        //ShowAsync 时重建作用域，隐藏期的取消状态不污染显示期视觉
        private CancellationTokenSource visualCancellation;

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

        //C1 容器级显示入口：先恢复视觉并置容器自身逻辑可见（ShowInternal：激活 → OnShow → IsVisible=true），
        //再进入子 Panel 呈现流程——按最终栈呈现并 await 转场，动画结束后再启用应接收输入的条目；
        //不积压/重放隐藏期间的导航动画，中间态不补播。F4：Busy 期间不排队、不替换，
        //显示请求直接返回且不改变任何状态；呈现期间销毁/接管按 F4 以取消异常向调用方传播
        public async UniTask ShowAsync()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (IsBusy)
            {
                return;
            }

            suppressVisuals = false;
            RenewVisualScope();
            ShowInternal();

            IsBusy = true;
            try
            {
                await PresentOnShowAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        //C1 容器级隐藏入口（与 ShowAsync 对称）：先置容器自身逻辑隐藏（HideInternal：保存焦点 →
        //OnHide → 失活 → IsVisible=false），再向所管理 Panel 传播 Hide 请求——已隐藏条目由幂等处理，
        //已显示条目执行 OnHide；不为隐藏执行 Close、不释放仍在栈中的资源。
        //最后取消视觉作用域：停视觉不停逻辑，已接受的导航按隐藏期规则继续完成
        public void Hide()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            //先置视觉跳过标志再停止视觉：被取消的过渡恢复执行时按隐藏期规则分流
            suppressVisuals = true;
            HideInternal();
            PropagateHideToStack();
            visualCancellation?.Cancel();
        }

        //C1：向栈内全部条目传播 Hide 请求；调用方不判断 IsVisible，由 Panel.HideInternal 幂等决定。
        //栈条目的访问在 StackContainer（本基类不可见），由其实现遍历；
        //沿用 R2 风格 Unity 判空：条目实例可能已被外部销毁，仅存活实例执行生命周期调用
        protected abstract void PropagateHideToStack();

        //C1：容器显示的呈现流程由具体容器实现——Page 只呈现当前页，
        //Modal 按多层展示规则恢复所需层并同步 Backdrop
        protected abstract UniTask PresentOnShowAsync();

        //C1：过渡等待统一经视觉作用域。Hide 停止视觉时过渡以取消结束，此处按「停视觉不停逻辑」
        //吞并该取消并继续隐藏期规则；生命周期 Token 的取消照常向上传播（维持 F4 语义），
        //过渡异常按 F5 可信原则不做收集、直接传播
        protected async UniTask WaitVisualTransitionAsync(UniTask transitionTask, CancellationToken lifetimeToken)
        {
            try
            {
                await transitionTask;
            }
            catch (OperationCanceledException) when (suppressVisuals && !lifetimeToken.IsCancellationRequested)
            {
                //Hide 停止/跳过在途视觉：组件契约保证干净停止；导航逻辑继续完成
            }
        }

        //C1：生命周期 Token 由 StackContainer 持有（销毁/接管取消源），基类经抽象成员访问
        protected abstract CancellationToken LifetimeCancellationToken { get; }

        //C1：当前视觉作用域 Token；延迟创建（基类生命周期 CTS 在本类字段初始化阶段尚不可用）
        protected CancellationToken VisualCancellationToken
        {
            get
            {
                if (visualCancellation == null)
                {
                    visualCancellation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeCancellationToken);
                }

                return visualCancellation.Token;
            }
        }

        //C1：Show 时重建视觉作用域，隐藏期的取消状态不影响本次显示期的视觉等待
        private void RenewVisualScope()
        {
            visualCancellation?.Dispose();
            visualCancellation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeCancellationToken);
        }

        protected static UniTask RunEnterTransitionAsync(Panel panel, CancellationToken cancellationToken)
        {
            IPanelTransition transition = panel.GetComponent<IPanelTransition>();
            return transition == null
                ? UniTask.CompletedTask
                : transition.EnterAsync(cancellationToken);
        }

        protected static UniTask RunExitTransitionAsync(Panel panel, CancellationToken cancellationToken)
        {
            IPanelTransition transition = panel.GetComponent<IPanelTransition>();
            return transition == null
                ? UniTask.CompletedTask
                : transition.ExitAsync(cancellationToken);
        }

        protected virtual void OnDestroy()
        {
            IsDisposed = true;
            //C1：随容器销毁释放视觉作用域；在途过渡已由链接的生命周期 Token 取消（F4）
            visualCancellation?.Dispose();
            visualCancellation = null;
        }
    }
}
