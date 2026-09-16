using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class Panel : MonoBehaviour, IPanel
    {
        [SerializeField] private GameObject defaultFocusTarget;

        public bool IsOpen { get; private set; }
        public bool IsVisible { get; private set; }
        public GameObject DefaultFocusTarget => defaultFocusTarget;

        /// <summary>显示完成（状态已提交）时通知外部订阅者</summary>
        public event Action<Panel> Shown;
        /// <summary>隐藏完成（状态已提交）时通知外部订阅者</summary>
        public event Action<Panel> Hidden;
        /// <summary>逻辑关闭完成（状态已提交）时通知外部订阅者；退场动画与自毁随后自行完成</summary>
        public event Action<Panel> Closed;

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (canvasGroup == null)
                {
                    canvasGroup = GetComponent<CanvasGroup>();
                }

                return canvasGroup;
            }
        }

        private CanvasGroup canvasGroup;
        private GameObject savedFocusTarget;
        //关闭中状态：Close 已受理、收尾流程在途，Show/Hide/Close 重入一律拒绝
        private bool closing;
        //工厂注入的 Prefab 句柄与释放入口；未注入（直接构造）保持 null
        private IPanelLoader prefabLoader;
        private GameObject prefabHandle;

        /// <summary>
        /// 设置面板是否可交互
        /// </summary>
        /// <param name="enabled">是否可交互</param>
        public void SetInteractable(bool enabled)
        {

            if (!enabled && IsVisible)
            {
                SaveFocusInternal();
            }

            if (CanvasGroup == null)
            {
                return;
            }

            CanvasGroup.interactable = enabled;
            CanvasGroup.blocksRaycasts = enabled;
        }

        /// <summary>
        /// 设置焦点到指定的UI元素
        /// </summary>
        /// <param name="focusTarget">要设置焦点的UI元素</param>
        public void Focus(GameObject focusTarget)
        {
            if (focusTarget != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(focusTarget);
            }
        }

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

            OnOpen();
            IsOpen = true;
            closing = false;
        }

        //公开同步显示：执行即提交可见状态，输入与焦点随状态立即启用，
        //入场过渡由 Panel 检测组件后 fire-and-forget，调用方不等待动画
        public void Show()
        {
            if (closing || IsVisible)
            {
                return;
            }

            gameObject.SetActive(true);
            OnShow();
            IsVisible = true;
            SetInteractable(true);
            RestoreFocusInternal();
            Shown?.Invoke(this);
            FireTransitionAsync(enter: true).Forget();
        }

        //公开同步隐藏：执行即提交隐藏状态并立即停用实例（视觉即时消失），
        //退场过渡 fire-and-forget 由组件在失活实例上自行追赶
        public virtual void Hide()
        {
            if (closing || !IsVisible)
            {
                return;
            }

            SaveFocusInternal();
            OnHide();
            gameObject.SetActive(false);
            IsVisible = false;
            Hidden?.Invoke(this);
            FireTransitionAsync(enter: false).Forget();
        }

        //公开同步关闭：逻辑立即关闭并进入 closing 拒绝重入；关闭决策不可逆，
        //生命周期回调抛错不阻断状态提交、Closed 通知与自毁调度（finally 保证）
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
                    SaveFocusInternal();
                    OnHide();
                    IsVisible = false;
                    Hidden?.Invoke(this);
                    //不立即停用：退场动画需在存活实例上播放，视觉延后追赶逻辑状态
                }

                SetInteractable(false);
                OnClose();
            }
            finally
            {
                IsOpen = false;
                Closed?.Invoke(this);
                FinishCloseAfterExitAsync().Forget();
            }
        }

        //关闭后的实例处置：工厂创建的 Prefab 面板播完退场动画自毁；
        //运行时容器由持有方管理存活（关闭仅做逻辑收尾），覆写为 false
        protected virtual bool DestroySelfOnClose => true;

        internal void BindPrefabHandle(IPanelLoader loader, GameObject prefab)
        {
            prefabLoader = loader;
            prefabHandle = prefab;
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
            ReleasePrefabHandleImmediate();
        }

        internal void FocusDefaultInternal()
        {
            Focus(defaultFocusTarget);
        }

        private void SaveFocusInternal()
        {
            savedFocusTarget = EventSystem.current == null
                ? null
                : EventSystem.current.currentSelectedGameObject;
        }

        //容器以「恢复已保存焦点」作为显式补偿调用，不新增焦点所有权状态
        internal void RestoreFocusInternal()
        {
            Focus(savedFocusTarget ?? defaultFocusTarget);
        }

        //过渡检测与触发归 Panel 内部：fire-and-forget，容器不感知、不等待
        private async UniTaskVoid FireTransitionAsync(bool enter)
        {
            IPanelTransition transition = GetComponent<IPanelTransition>();
            if (transition == null)
            {
                return;
            }

            try
            {
                if (enter)
                {
                    await transition.EnterAsync(destroyCancellationToken);
                }
                else
                {
                    await transition.ExitAsync(destroyCancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                //实例销毁取消过渡：视觉无需继续追赶
            }
        }

        //关闭收尾：等待退场动画播完再释放句柄并按配置自毁，自毁不因过渡错误中断；
        //外部强杀先行时由 OnDestroy 兜底释放；过渡错误在处置完成后原样上抛（归组件观察）
        private async UniTaskVoid FinishCloseAfterExitAsync()
        {
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
            if (DestroySelfOnClose)
            {
                Destroy(gameObject);
            }

            if (exitError != null)
            {
                throw exitError;
            }
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
    }
}