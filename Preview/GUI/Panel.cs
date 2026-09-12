using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class Panel : MonoBehaviour, IPanel, IPanelLifecycle
    {
        [SerializeField] private GameObject defaultFocusTarget;

        public bool IsOpen { get; private set; }
        public bool IsVisible { get; private set; }
        public GameObject DefaultFocusTarget => defaultFocusTarget;

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
        // 自上一次成功 Open 以来是否已执行过 Close；作为 CloseInternal 的幂等判定依据（见 CloseInternal 注释）
        private bool closeExecuted;

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
            // 幂等：已处于打开状态时重复请求为空操作，不重复执行 OnOpen
            if (IsOpen)
            {
                return;
            }

            OnOpen();
            IsOpen = true;
            closeExecuted = false;
        }

        internal void ShowInternal()
        {
            // 幂等：已处于显示状态时重复请求为空操作，不重复执行 OnShow。
            // 注意：重复 Show 不再隐式恢复焦点（原副作用已按 R3 契约移除），
            // 需要恢复焦点的场景应由调用方显式调用 Focus（容器侧由后续任务补偿）。
            if (IsVisible)
            {
                return;
            }

            gameObject.SetActive(true);
            OnShow();
            IsVisible = true;
            RestoreFocusInternal();
        }

        internal void HideInternal()
        {
            // 幂等：已处于隐藏状态时重复请求为空操作，
            // 不重复保存焦点（避免覆盖首次隐藏时记录的目标）、不重复执行 OnHide
            if (!IsVisible)
            {
                return;
            }

            SaveFocusInternal();
            OnHide();
            gameObject.SetActive(false);
            IsVisible = false;
        }

        internal void CloseInternal()
        {
            // 幂等：按「自上一次 Open 以来是否已执行过 Close」判定，而非仅按 IsOpen 判定。
            // 原因：容器（PageContainer/ModalContainer）自身从不 Open，却复用 Panel.OnClose
            // 作为栈清理入口（生产与既有测试均如此）；若仅按 IsOpen 判定，从未打开的面板
            // 第一次 Close 就会被跳过，容器清理直接失效。该判定保证：
            // 重复 Close 恒为空操作；Open→Close→Open→Close 时 OnClose 每个打开周期各执行一次；
            // 从未 Open 的面板首次 Close 仍执行一次清理，与既有行为一致。
            if (closeExecuted)
            {
                return;
            }

            OnClose();
            IsOpen = false;
            closeExecuted = true;
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

        //internal：容器需要把「恢复已保存焦点」作为最小显式补偿调用
        //（PageContainer.Push 隐藏前页前先把选择还给前页的焦点记忆、失败收尾时恢复前页焦点），
        //仅开放既有实现，不新增焦点所有权状态，焦点系统仍由后续任务处理
        internal void RestoreFocusInternal()
        {
            Focus(savedFocusTarget ?? defaultFocusTarget);
        }

        // IPanelLifecycle 显式实现：生命周期契约入口收敛到 internal 接口，公共 API 表面不暴露；
        // 同程序集容器仍可直接调用上方 internal 方法，外部程序集两者皆不可达（C2 边界）。
        void IPanelLifecycle.OpenInternal()
        {
            OpenInternal();
        }

        void IPanelLifecycle.CloseInternal()
        {
            CloseInternal();
        }

        void IPanelLifecycle.ShowInternal()
        {
            ShowInternal();
        }

        void IPanelLifecycle.HideInternal()
        {
            HideInternal();
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
