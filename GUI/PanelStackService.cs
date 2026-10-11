using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 面板导航服务：Page/Modal 两栈的唯一持有与协调者，并托管模态 backdrop。
    /// </summary>
    /// <remarks>
    /// 两栈数据不对外暴露，避免调用方绕过本服务直接改栈。backdrop 为纯遮蔽：只压暗与阻挡被遮层输入，
    /// 无点击语义，Modal 关闭仅由面板自身出口与 <see cref="Back"/> 发起。
    /// </remarks>
    public sealed class PanelStackService : MonoBehaviour
    {
        private const string BackdropObjectName = "ModalBackdrop";

        [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.55f);

        private readonly List<Panel> pageStack = new List<Panel>();
        private readonly List<ModalPanel> modalStack = new List<ModalPanel>();
        private readonly List<GameObject> backdrops = new List<GameObject>();

        private RectTransform pageLayer;
        private RectTransform modalLayer;
        private CanvasGroup pageLayerCanvasGroup;
        private bool layersBound;

        /// <summary>最后一个 Page 出栈后触发。</summary>
        public event Action StackEmptied;

        /// <summary>Page 栈条目数。</summary>
        public int PageCount => pageStack.Count;
        /// <summary>Modal 栈条目数。</summary>
        public int ModalCount => modalStack.Count;
        /// <summary>在场 backdrop 层数，与 Modal 栈条目一一对应。</summary>
        public int BackdropCount => backdrops.Count;
        /// <summary>Page 栈顶，栈空时为 null。</summary>
        public Panel CurrentPage => pageStack.Count == 0 ? null : pageStack[pageStack.Count - 1];
        /// <summary>Modal 栈顶，栈空时为 null。</summary>
        public ModalPanel CurrentModal => modalStack.Count == 0 ? null : modalStack[modalStack.Count - 1];

        /// <summary>
        /// 绑定 Page 层与 Modal 层；Modal 层的 backdrop 与面板呈现均由本服务负责。
        /// </summary>
        public void BindLayers(RectTransform pageLayerTransform, RectTransform modalLayerTransform)
        {
            if (pageLayerTransform == null)
            {
                throw new ArgumentNullException(nameof(pageLayerTransform));
            }

            if (modalLayerTransform == null)
            {
                throw new ArgumentNullException(nameof(modalLayerTransform));
            }

            if (layersBound)
            {
                throw new InvalidOperationException("Layers are already bound; each panel stack service owns one pair of layers.");
            }

            pageLayer = pageLayerTransform;
            modalLayer = modalLayerTransform;
            pageLayerCanvasGroup = ResolveLayerCanvasGroup(pageLayer);
            layersBound = true;
        }

        /// <summary>
        /// 推入面板：ModalPanel 进 Modal 栈，其余进 Page 栈。
        /// </summary>
        public void Push(Panel panel)
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            if (panel is ModalPanel modal)
            {
                RequireLayers();
                PushModal(modal);
                return;
            }

            RequireLayers();
            PushPage(panel);
        }

        /// <summary>关闭面板，无论它位于哪个栈；不在任何栈中时返回 false。</summary>
        public bool Close(Panel panel)
        {
            if (panel == null || (!pageStack.Contains(panel) && !modalStack.Contains(panel as ModalPanel)))
            {
                return false;
            }

            panel.Close();
            return true;
        }

        /// <summary>关闭栈顶 Modal；Modal 栈为空时改为退栈顶 Page。</summary>
        public bool Back()
        {
            RequireLayers();

            ModalPanel modal = CurrentModal;
            if (modal != null)
            {
                modal.Close();
                return true;
            }

            return PopTopPage();
        }

        /// <summary>服务内部关闭栈顶 Modal，继承 <see cref="ModalPanel"/> 的取消语义。</summary>
        internal bool Cancel(ModalPanel modal)
        {
            if (!ReferenceEquals(CurrentModal, modal))
            {
                return false;
            }

            modal.Close();
            return true;
        }

        /// <summary>服务内部完成栈顶 Modal 并交付结果。</summary>
        internal bool Complete<TResult>(ModalPanel<TResult> modal, TResult result)
        {
            if (!ReferenceEquals(CurrentModal, modal))
            {
                return false;
            }

            Action notification = modal.CaptureResultNotification(true, result);
            modal.Close();
            notification?.Invoke();
            return true;
        }

        /// <summary>面板已销毁时清掉仍挂在两栈中的条目，避免出现不可用项。</summary>
        public void PruneDestroyedPanels()
        {
            for (int index = pageStack.Count - 1; index >= 0; index--)
            {
                if (pageStack[index] != null)
                {
                    continue;
                }

                pageStack[index].Closed -= OnPageClosed;
                pageStack.RemoveAt(index);
            }

            for (int index = modalStack.Count - 1; index >= 0; index--)
            {
                if (modalStack[index] != null)
                {
                    continue;
                }

                modalStack[index].Closed -= OnModalClosed;
                modalStack.RemoveAt(index);
            }

            RefreshCoverage();
        }

        private void PushModal(ModalPanel modal)
        {
            if (modalStack.Contains(modal))
            {
                throw new InvalidOperationException("The modal is already in the modal stack.");
            }

            modal.transform.SetParent(modalLayer, false);
            modalStack.Add(modal);
            backdrops.Add(CreateBackdrop(modal));
            modal.Closed += OnModalClosed;
            modal.BindService(this);

            //取焦点必须先于遮蔽：被遮面板只有先记下自己停滞的焦点，解除遮蔽后才有正确的恢复目标
            CaptureCoveredFocus();
            modal.Show();
            modal.FocusDefaultInternal();
            RefreshCoverage();
        }

        private void PushPage(Panel page)
        {
            if (pageStack.Contains(page))
            {
                throw new InvalidOperationException("The page is already in the page stack.");
            }

            page.transform.SetParent(pageLayer, false);
            Panel previous = CurrentPage;
            pageStack.Add(page);
            page.Closed += OnPageClosed;

            //同栈盖页同样先采焦点：被盖页解除遮蔽后要回到自己失去焦点前的目标
            previous?.CaptureFocusBeforeCover();
            page.Show();

            if (previous != null && !ReferenceEquals(previous, page))
            {
                //被新页盖住：收回显示与交互但保留实例渲染
                previous.CoverInternal();
            }

            //新页的显现由自身 Show 负责，这里只重算 Modal 遮蔽
            RefreshCoverage();
        }

        private void OnPageClosed(Panel page)
        {
            int index = pageStack.IndexOf(page);
            if (index < 0)
            {
                return;
            }

            page.Closed -= OnPageClosed;
            bool wasTop = ReferenceEquals(CurrentPage, page);
            pageStack.RemoveAt(index);

            //关掉的是显示中的栈顶：暴露出来的下层页按原状显现
            if (wasTop)
            {
                RefreshCoverage();
                if (modalStack.Count == 0)
                {
                    CurrentPage?.UncoverInternal();
                }
            }
            if (pageStack.Count == 0)
            {
                StackEmptied?.Invoke();
            }
        }

        private void OnModalClosed(Panel modal)
        {
            ModalPanel closedModal = modal as ModalPanel;
            if (closedModal == null || !modalStack.Contains(closedModal))
            {
                return;
            }

            closedModal.Closed -= OnModalClosed;
            closedModal.UnbindService();
            int index = modalStack.IndexOf(closedModal);
            modalStack.RemoveAt(index);
            DestroyBackdropAt(index);

            //先按剩余栈重算遮蔽：Page 层与下层 Modal 的锁随栈顶变化即时更新
            RefreshCoverage();

            if (modalStack.Count == 0)
            {
                CurrentPage?.UncoverInternal();
            }
            else
            {
                CurrentModal.UncoverInternal();
            }
        }

        private bool PopTopPage()
        {
            Panel page = CurrentPage;
            if (page == null)
            {
                return false;
            }

            page.Close();
            return true;
        }

        //遮蔽规则：Modal 在场时 Page 层与下层 Modal 去激活（保持渲染但不可交互、不接收射线），仅栈顶 Modal 可交互
        private void RefreshCoverage()
        {
            bool modalPresent = modalStack.Count > 0;

            if (pageLayerCanvasGroup != null)
            {
                pageLayerCanvasGroup.interactable = !modalPresent;
                pageLayerCanvasGroup.blocksRaycasts = !modalPresent;
            }

            if (!modalPresent)
            {
                return;
            }

            foreach (Panel page in pageStack)
            {
                DeactivatePanel(page);
            }

            for (int index = 0; index < modalStack.Count - 1; index++)
            {
                DeactivatePanel(modalStack[index]);
            }
        }

        private static void DeactivatePanel(Panel panel)
        {
            panel?.CoverInternal();
        }

        //遮蔽会收回焦点：先让即将被遮的面板记下自己停滞的焦点，再做任何可能转移焦点的动作
        private void CaptureCoveredFocus()
        {
            foreach (Panel page in pageStack)
            {
                page?.CaptureFocusBeforeCover();
            }

            for (int index = 0; index < modalStack.Count; index++)
            {
                modalStack[index]?.CaptureFocusBeforeCover();
            }
        }

        //每个 Modal 各配一层 backdrop，插入到该 Modal 之前：backdrop 恒在所属 Modal 之下，且自动叠在更低层的 backdrop 与 Modal 之上
        private GameObject CreateBackdrop(ModalPanel modal)
        {
            GameObject backdropObject = new GameObject(
                BackdropObjectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
            backdropRect.SetParent(modalLayer, false);
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            backdropRect.localScale = Vector3.one;

            //backdrop 先落到 Modal 层末尾，再插到所属 Modal 当前位次，所属 Modal 顺势后移一位
            int panelIndex = modal.transform.GetSiblingIndex();
            backdropRect.SetSiblingIndex(Mathf.Max(0, panelIndex));

            Image backdropImage = backdropObject.GetComponent<Image>();
            backdropImage.color = backdropColor;
            backdropImage.raycastTarget = true;

            CanvasGroup backdropCanvasGroup = backdropObject.GetComponent<CanvasGroup>();
            backdropCanvasGroup.alpha = backdropColor.a;
            backdropCanvasGroup.interactable = true;
            backdropCanvasGroup.blocksRaycasts = true;
            return backdropObject;
        }

        private void DestroyBackdropAt(int index)
        {
            if (index < 0 || index >= backdrops.Count)
            {
                return;
            }

            GameObject backdropObject = backdrops[index];
            backdrops.RemoveAt(index);
            if (backdropObject != null)
            {
                UnityEngine.Object.Destroy(backdropObject);
            }
        }

        private static CanvasGroup ResolveLayerCanvasGroup(RectTransform layer)
        {
            CanvasGroup group = layer.GetComponent<CanvasGroup>();
            return group != null ? group : layer.gameObject.AddComponent<CanvasGroup>();
        }

        private void RequireLayers()
        {
            if (modalLayer == null)
            {
                throw new InvalidOperationException("BindLayers must be called before using the panel stacks.");
            }
        }
    }
}
