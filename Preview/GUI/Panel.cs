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
        private bool closing;
        private IPanelLoader prefabLoader;
        private GameObject prefabHandle;
        private CancellationTokenSource transitionCancellation;

        /// <summary>
        /// 设置面板是否可交互
        /// </summary>
        /// <param name="enabled"></param>
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
        /// <param name="focusTarget"></param>
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
            SetInteractable(true);
            RestoreFocusInternal();
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

        private void HideCore()
        {
            SaveFocusInternal();
            OnHide();
            IsVisible = false;
            SetInteractable(false);
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
    }
}
