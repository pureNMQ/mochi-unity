using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    public abstract class Panel : MonoBehaviour, IPanel
    {
        public bool IsOpen { get; private set; }
        public bool IsVisible { get; private set; }

        /// <summary>出生时由工厂注入的服务容器；未经工厂注入的面板为 null。</summary>
        public ServiceProvider Services { get; private set; }

        /// <summary>显示完成（状态已提交）时通知外部订阅者</summary>
        public event Action<Panel> Shown;
        /// <summary>隐藏完成（状态已提交）时通知外部订阅者</summary>
        public event Action<Panel> Hidden;
        /// <summary>逻辑关闭完成（状态已提交）时通知外部订阅者；退场动画与自毁随后自行完成</summary>
        public event Action<Panel> Closed;

        /// <summary>面板遮罩与透明度载体；组件缺失时自动补齐，过渡与交互控制都依赖它，因此永远不为 null。</summary>
        public CanvasGroup CanvasGroup
        {
            get
            {
                if (canvasGroup == null)
                {
                    canvasGroup = GetComponent<CanvasGroup>();
                }

                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }

                return canvasGroup;
            }
        }

        private CanvasGroup canvasGroup;
        private bool closing;
        private bool coverLogicalHidden;
        private IPanelLoader prefabLoader;
        private GameObject prefabHandle;
        private CancellationTokenSource transitionCancellation;

        internal UniTask PrepareInternalAsync(CancellationToken cancellationToken)
        {
            return OnPrepareAsync(cancellationToken);
        }

        internal void OpenInternal()
        {
            if (IsOpen)
            {
                return;
            }

            //激活归 Open、视觉入场归 Show：OnOpen 须运行在已激活实例上
            gameObject.SetActive(true);
            OnOpen();
            IsOpen = true;
            closing = false;
        }

        /// <summary>
        /// 显示面板，若已显示则不执行任何操作
        /// </summary>
        public void Show()
        {
            if (closing || IsVisible)
            {
                return;
            }

            ShowCore();
            FireTransitionAsync(true).Forget();
        }

        /// <summary>显示面板，不播放过渡动画</summary>
        public void ShowImmediate()
        {
            if (closing || IsVisible)
            {
                return;
            }

            ShowCore();
        }

        private void ShowCore()
        {
            gameObject.SetActive(true);
            IsVisible = true;
            OnVisibilityChanged(true);
            OnShow();
            Shown?.Invoke(this);
        }

        /// <summary>
        /// 隐藏面板，若已隐藏则不执行任何操作
        /// </summary>
        public virtual void Hide()
        {
            if (closing || !IsVisible)
            {
                return;
            }

            HideCore();
            FireTransitionAsync(false).Forget();
        }

        /// <summary>隐藏面板，不播放过渡动画</summary>
        public void HideImmediate()
        {
            if (closing || !IsVisible)
            {
                return;
            }

            HideCore();
            gameObject.SetActive(false);
        }

        /// <summary>遮蔽该面板：收回交互并中断在途过渡，但保留实例渲染。</summary>
        internal void CoverInternal()
        {
            OnVisibilityChanged(false);
            CancelTransition();
            OnHide();
            IsVisible = false;
            coverLogicalHidden = true;
            Hidden?.Invoke(this);
        }

        /// <summary>解除遮蔽：恢复被遮蔽前的显示态，交互与焦点由显示流程统一恢复。</summary>
        internal void UncoverInternal()
        {
            if (coverLogicalHidden)
            {
                coverLogicalHidden = false;
                //显示流程已内含交互位恢复与焦点交接，重复调用会把 OnShow 里设置的焦点覆盖掉
                ShowImmediate();
                return;
            }

            OnVisibilityChanged(true);
        }

        private void HideCore()
        {
            OnHide();
            IsVisible = false;
            OnVisibilityChanged(false);
            Hidden?.Invoke(this);
        }

        /// <summary>
        /// 关闭面板，播放退场过渡后释放并销毁实例
        /// </summary>
        public void Close()
        {
            if (closing)
            {
                return;
            }

            closing = true;
            try
            {
                if (IsVisible)
                {
                    //不立即停用：退场动画需在存活实例上播放
                    HideCore();
                }

                OnClose();
            }
            finally
            {
                IsOpen = false;
                Closed?.Invoke(this);
                OnClosedInternal();
                FinishCloseAfterExitAsync().Forget();
            }
        }

        /// <summary>关闭面板，不播退场过渡，立即释放并销毁实例</summary>
        public void CloseImmediate()
        {
            if (closing)
            {
                return;
            }

            closing = true;
            try
            {
                if (IsVisible)
                {
                    HideCore();
                }

                OnClose();
            }
            finally
            {
                IsOpen = false;
                Closed?.Invoke(this);
                OnClosedInternal();
                CancelTransition();
                ReleasePrefabHandleImmediate();
                Destroy(gameObject);
            }
        }

        internal void BindPrefabHandle(IPanelLoader loader, GameObject prefab)
        {
            prefabLoader = loader;
            prefabHandle = prefab;
        }

        internal void BindServices(ServiceProvider services)
        {
            Services = services;
        }

        // Unity 只对曾激活过的对象保证 OnDestroy 回调，未激活实例须销毁前在此显式释放
        internal void ReleasePrefabHandleImmediate()
        {
            if (prefabHandle == null)
            {
                return;
            }

            GameObject handle = prefabHandle;
            IPanelLoader loader = prefabLoader;
            prefabHandle = null;
            prefabLoader = null;
            loader?.Release(handle);
        }

        protected virtual void OnDestroy()
        {
            CancelTransition();
            ReleasePrefabHandleImmediate();
        }

        private async UniTaskVoid FireTransitionAsync(bool enter)
        {
            IPanelTransition transition = GetComponent<IPanelTransition>();
            if (transition == null)
            {
                if (!enter)
                {
                    gameObject.SetActive(false);
                }

                return;
            }

            CancellationToken token = BeginTransition();
            try
            {
                if (enter)
                {
                    await transition.EnterAsync(token);
                }
                else
                {
                    await transition.ExitAsync(token);
                    //退场完成强制停用，停用权归 Panel 而非过渡实现
                    gameObject.SetActive(false);
                }
            }
            catch (OperationCanceledException)
            {
                //过渡被新过渡取代或实例销毁：取消方接管视觉收尾
            }
        }

        private async UniTaskVoid FinishCloseAfterExitAsync()
        {
            //关闭退场先行接管视觉，取消 Close 前仍在途的过渡
            CancelTransition();
            Exception exitError = null;
            IPanelTransition transition = GetComponent<IPanelTransition>();
            if (transition != null)
            {
                try
                {
                    await transition.ExitAsync(destroyCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    //实例销毁先行：句柄已由 OnDestroy 兜底释放，无需继续处置
                    return;
                }
                catch (Exception exception)
                {
                    exitError = exception;
                }
            }

            if (this == null)
            {
                return;
            }

            ReleasePrefabHandleImmediate();
            Destroy(gameObject);

            if (exitError != null)
            {
                throw exitError;
            }
        }

        //同一时刻至多一个在途过渡，新过渡取消旧过渡
        private CancellationToken BeginTransition()
        {
            transitionCancellation?.Cancel();
            transitionCancellation?.Dispose();
            transitionCancellation = new CancellationTokenSource();
            return transitionCancellation.Token;
        }

        private void CancelTransition()
        {
            transitionCancellation?.Cancel();
            transitionCancellation?.Dispose();
            transitionCancellation = null;
        }

        /// <summary>
        /// 准备面板，先于OnOpen，用于加载资源等异步操作
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected virtual UniTask OnPrepareAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnShow()
        {
        }

        protected virtual void OnHide()
        {
        }

        protected virtual void OnClose()
        {
        }

        /// <summary>显示状态提交时回调，交互开关与焦点交接由此驱动。</summary>
        /// <remarks>
        /// 只能由框架内部覆写：业务子类覆写 OnShow/OnHide 时通常不会调用 base，
        /// 把交接放在这两个可覆写回调里会被静默吞掉。
        /// </remarks>
        internal virtual void OnVisibilityChanged(bool visible)
        {
        }

        /// <summary>Close 与 CloseImmediate 提交 Closed 之后回调一次。</summary>
        internal virtual void OnClosedInternal()
        {
        }
    }
}
