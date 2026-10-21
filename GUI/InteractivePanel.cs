using UnityEngine;
using UnityEngine.EventSystems;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 可交互面板：承载交互开关与页面/弹窗之间的焦点交接。
    /// 焦点唯一事实源是 <see cref="EventSystem.currentSelectedGameObject"/>，本类只在显示状态变化时决定把选中交给谁。
    /// </summary>
    public class InteractivePanel : Panel, IInteractivePanel
    {
        [Tooltip("面板打开时优先选中的控件；运行时可用 SetEntryFocus 覆盖")]
        [SerializeField] private GameObject defaultFocusTarget;

        [Tooltip("框架收到取消输入且本面板在前台时是否允许用它返回：弹窗按取消语义关闭自己，页面退出栈顶；关闭后取消键不再影响本页")]
        [SerializeField] private bool allowCancelBack = true;

        private GameObject entryFocusTarget;
        private GameObject restoreFocusTarget;
        private bool interactable;

        /// <summary>入口焦点：运行时声明的目标优先于 Inspector 配置的值。</summary>
        public GameObject DefaultFocusTarget => entryFocusTarget != null ? entryFocusTarget : defaultFocusTarget;

        /// <summary>框架是否允许在收到取消输入时返回本面板；页面可按自身类型在开页时改写。</summary>
        public bool AllowCancelBack
        {
            get => allowCancelBack;
            set => allowCancelBack = value;
        }

        /// <summary>面板当前是否可交互。</summary>
        public bool IsInteractable => interactable;

        //面板在前台指它既显示中又持有交互，二者缺一都不能接住焦点
        private bool IsForeground => IsVisible && interactable;

        /// <summary>切换面板自身及其上控件的指针与导航可用性；状态未变化时不重复触发激活边沿。</summary>
        public void SetInteractable(bool enabled)
        {
            CanvasGroup group = CanvasGroup;
            group.interactable = enabled;
            group.blocksRaycasts = enabled;

            if (interactable == enabled)
            {
                return;
            }

            interactable = enabled;
            if (enabled)
            {
                OnActivated();
            }
            else
            {
                OnDeactivated();
            }
        }

        /// <summary>声明运行时入口焦点；面板已在前台时立即把选中移过去。</summary>
        public void SetEntryFocus(GameObject target)
        {
            entryFocusTarget = target;
            if (IsForeground)
            {
                FocusTo(DefaultFocusTarget);
            }
        }

        /// <summary>把选中交给属于本面板的控件；目标不是本面板子孙时保持当前选中不变。</summary>
        public void FocusTo(GameObject target)
        {
            if (IsOwned(target))
            {
                SetSelection(target);
            }
        }

        /// <summary>面板经交互状态边沿转为可交互时触发；首次生效视为激活，不重复触发。</summary>
        protected virtual void OnActivated()
        {
        }

        /// <summary>面板被遮蔽而失去交互时触发。</summary>
        protected virtual void OnDeactivated()
        {
        }

        internal override void OnVisibilityChanged(bool visible)
        {
            if (!visible)
            {
                SetInteractable(false);
                //只把属于自己的选中记为恢复目标:选中落在别的面板上时记录它,会把别处的焦点当成自己的
                if (IsOwned(Selection))
                {
                    restoreFocusTarget = Selection;
                }

                return;
            }

            SetInteractable(true);
            GameObject target = IsOwned(restoreFocusTarget) ? restoreFocusTarget : DefaultFocusTarget;
            SetSelection(IsOwned(target) ? target : null);
        }

        internal override void OnClosedInternal()
        {
            if (IsOwned(Selection))
            {
                SetSelection(null);
            }
        }

        private static GameObject Selection =>
            EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;

        private static void SetSelection(GameObject target)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private bool IsOwned(GameObject target)
        {
            return target != null && target.transform.IsChildOf(transform);
        }
    }
}
