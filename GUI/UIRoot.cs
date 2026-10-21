using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 独立 UI 根：装配 Screen Space Overlay 画布（CanvasScaler 1920x1080）与固定五层，
    /// 渲染顺序恒为 HUD &lt; Page &lt; Modal &lt; Transition &lt; Toast，层级或画布被销毁后按需重建且排序不变。
    /// </summary>
    /// <remarks>
    /// 本组件只负责装配与排序，不承载导航、栈或业务行为。Toast 层不提供公共访问口，归后续同程序集 ToastService 私有。
    /// EventSystem 的存在保证与输入资产配置归 GUIService，本组件既不创建也不配置 EventSystem。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        private const string CanvasObjectName = "Canvas";
        private const string HudLayerObjectName = "HUDLayer";
        private const string PageLayerObjectName = "PageLayer";
        private const string ModalLayerObjectName = "ModalLayer";
        private const string TransitionLayerObjectName = "TransitionLayer";
        private const string ToastLayerObjectName = "ToastLayer";

        private static UIRoot instance;

        private GameObject canvasObject;
        private RectTransform hud;
        private RectTransform page;
        private RectTransform modal;
        private RectTransform transition;
        private RectTransform toast;
        //仅在 Awake 完成过五层装配的根才启用属性重建；重复根保持 null
        private bool hierarchyCreated;

        /// <summary>
        /// 取当前 UI 根；不存在时懒创建常驻（DontDestroyOnLoad）根并完成五层装配。
        /// 根被销毁后再次访问即整套重建。同场景已存在 UIRoot 时，后苏醒的重复组件自毁且不接管单例。
        /// </summary>
        public static UIRoot Instance => instance != null ? instance : CreateDefaultInstance();

        /// <summary>HUD 层（第 0 层，不可交互）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform HUD => EnsureLayer(ref hud, HudLayerObjectName, 0, false);

        /// <summary>Page 层（第 1 层，可交互）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Page => EnsureLayer(ref page, PageLayerObjectName, 1, true);

        /// <summary>Modal 层（第 2 层，可交互）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Modal => EnsureLayer(ref modal, ModalLayerObjectName, 2, true);

        /// <summary>Transition 层（第 3 层，不可交互）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Transition => EnsureLayer(ref transition, TransitionLayerObjectName, 3, false);

        /// <summary>Toast 层（第 4 层，不可交互）；仅供同程序集的 ToastService 使用，不向应用暴露公共访问口。</summary>
        internal RectTransform Toast => EnsureLayer(ref toast, ToastLayerObjectName, 4, false);

        /// <summary>解析指定 GUI 层；None 返回 null，其他层按固定绘制顺序懒加载。</summary>
        public RectTransform GetLayer(GUILayer layer)
        {
            switch (layer)
            {
                case GUILayer.HUD:
                    return HUD;
                case GUILayer.Page:
                    return Page;
                case GUILayer.Modal:
                    return Modal;
                case GUILayer.Transition:
                    return Transition;
                case GUILayer.Toast:
                    return Toast;
                case GUILayer.None:
                    return null;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(layer), layer, null);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            AssembleHierarchy();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void AssembleHierarchy()
        {
            canvasObject = CreateCanvasRoot();
            hud = CreateLayer(HudLayerObjectName, 0, false);
            page = CreateLayer(PageLayerObjectName, 1, true);
            modal = CreateLayer(ModalLayerObjectName, 2, true);
            transition = CreateLayer(TransitionLayerObjectName, 3, false);
            toast = CreateLayer(ToastLayerObjectName, 4, false);
            hierarchyCreated = true;
        }

        //懒创建：业务首次访问时自建常驻 UI 根，不使用 UI 的场景永不创建
        private static UIRoot CreateDefaultInstance()
        {
            GameObject root = new GameObject("UIRoot");
            DontDestroyOnLoad(root);
            return root.AddComponent<UIRoot>();
        }

        private RectTransform EnsureLayer(ref RectTransform layer, string objectName, int siblingIndex, bool interactive)
        {
            if (layer == null && hierarchyCreated)
            {
                layer = CreateLayer(objectName, siblingIndex, interactive);
            }

            return layer;
        }

        //HUD、Transition、Toast 只呈现信息，落在这些层的面板不具备交互语义：把不可交互钉在层上，
        //面板就不必各自关闭 CanvasGroup，也不会因为漏关而挡住下层输入
        private RectTransform CreateLayer(string objectName, int siblingIndex, bool interactive)
        {
            GameObject layerObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasGroup));
            layerObject.transform.SetParent(EnsureCanvasRoot(), false);

            RectTransform rect = layerObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetSiblingIndex(siblingIndex);

            CanvasGroup group = layerObject.GetComponent<CanvasGroup>();
            group.interactable = interactive;
            group.blocksRaycasts = interactive;
            return rect;
        }

        private Transform EnsureCanvasRoot()
        {
            if (canvasObject == null)
            {
                canvasObject = CreateCanvasRoot();
            }

            return canvasObject.transform;
        }

        private GameObject CreateCanvasRoot()
        {
            GameObject canvasRoot = new GameObject(
                CanvasObjectName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvasRoot;
        }
    }
}
