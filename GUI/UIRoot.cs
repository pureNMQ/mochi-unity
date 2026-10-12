using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 独立 UI 根：装配 Screen Space Overlay 画布（CanvasScaler 1920x1080）与固定五层，
    /// 渲染顺序恒为 HUD &lt; Page &lt; Modal &lt; Transition &lt; Toast，层级或画布被销毁后按需重建且排序不变。
    /// </summary>
    /// <remarks>
    /// 本组件只负责装配与排序，不承载导航、栈或业务行为。Toast 层不提供公共访问口，归后续同程序集 ToastService 私有。
    /// 显式放置本组件的场景自备 EventSystem；仅懒创建路径补建兜底 EventSystem。
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
        /// 取当前 UI 根；不存在时懒创建常驻（DontDestroyOnLoad）根并完成五层与 EventSystem 装配。
        /// 根被销毁后再次访问即整套重建。同场景已存在 UIRoot 时，后苏醒的重复组件自毁且不接管单例。
        /// </summary>
        public static UIRoot Instance => instance != null ? instance : CreateDefaultInstance();

        /// <summary>HUD 层（第 0 层）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform HUD => EnsureLayer(ref hud, HudLayerObjectName, 0);

        /// <summary>Page 层（第 1 层）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Page => EnsureLayer(ref page, PageLayerObjectName, 1);

        /// <summary>Modal 层（第 2 层）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Modal => EnsureLayer(ref modal, ModalLayerObjectName, 2);

        /// <summary>Transition 层（第 3 层）；层级被销毁后访问会以原名在原排序位重建。</summary>
        public RectTransform Transition => EnsureLayer(ref transition, TransitionLayerObjectName, 3);

        /// <summary>Toast 层（第 4 层）；仅供同程序集的 ToastService 使用，不向应用暴露公共访问口。</summary>
        internal RectTransform Toast => EnsureLayer(ref toast, ToastLayerObjectName, 4);

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
            hud = CreateLayer(HudLayerObjectName, 0);
            page = CreateLayer(PageLayerObjectName, 1);
            modal = CreateLayer(ModalLayerObjectName, 2);
            transition = CreateLayer(TransitionLayerObjectName, 3);
            toast = CreateLayer(ToastLayerObjectName, 4);
            hierarchyCreated = true;
        }

        //懒创建：业务首次访问时自建常驻 UI 根，不使用 UI 的场景永不创建
        private static UIRoot CreateDefaultInstance()
        {
            GameObject root = new GameObject("UIRoot");
            DontDestroyOnLoad(root);
            UIRoot uiRoot = root.AddComponent<UIRoot>();
            //兜底仅限懒创建路径：显式放置的场景（含测试）自备 EventSystem，避免双实例抢占 current
            EnsureEventSystem(root.transform);
            return uiRoot;
        }

        //复用场景已有 EventSystem，仅缺失时兜底创建
        private static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            //兜底 EventSystem 挂在 UI 根之下与其同寿命：既不随创建时刻的 Active Scene 卸载而失效，
            //也不会在 UI 根销毁后残留为孤儿全局对象
            eventSystemObject.transform.SetParent(parent, false);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private RectTransform EnsureLayer(ref RectTransform layer, string objectName, int siblingIndex)
        {
            if (layer == null && hierarchyCreated)
            {
                layer = CreateLayer(objectName, siblingIndex);
            }

            return layer;
        }

        private RectTransform CreateLayer(string objectName, int siblingIndex)
        {
            GameObject layerObject = new GameObject(objectName, typeof(RectTransform));
            layerObject.transform.SetParent(EnsureCanvasRoot(), false);

            RectTransform rect = layerObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetSiblingIndex(siblingIndex);
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
