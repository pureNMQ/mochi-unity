using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.Preview.GUI
{
    [DisallowMultipleComponent]
    public sealed class GUINavigator : MonoBehaviour
    {
        private const string CanvasObjectName = "Canvas";
        private const string PageContainerObjectName = "PageContainer";
        private const string ModalContainerObjectName = "ModalContainer";
        private const string HudLayerObjectName = "HUDLayer";
        private const string NotificationLayerObjectName = "NotificationLayer";

        private static GUINavigator instance;
        private GameObject canvasObject;
        private PageContainer pages;
        private ModalContainer modals;
        private OverlayContainer hud;
        private OverlayContainer notifications;
        private IPanelLoader rootLoader;
        //仅在 Awake/Initialize 建立过根 Layer 的导航器才启用属性重建；重复导航器保持 null
        private bool rootsCreated;

        public static GUINavigator Instance => instance;

        //根 Layer 被销毁时按需自动重建（沿用最近一次 Initialize 的 Loader），允许外部直接调用方法
        public PageContainer Pages => EnsureLayer(ref pages, PageContainerObjectName, 0);
        public ModalContainer Modals => EnsureLayer(ref modals, ModalContainerObjectName, 1);
        public OverlayContainer HUD => EnsureLayer(ref hud, HudLayerObjectName, 2);
        public OverlayContainer Notifications =>
            EnsureLayer(ref notifications, NotificationLayerObjectName, 3);

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureRuntimeRoots();
        }

        public void Initialize(IPanelLoader loader)
        {
            //根容器 Initialize 成功即处于逻辑显示状态：首个 Push/Open 正常带视觉；
            //整树隐藏/恢复使用容器级 Hide/ShowAsync（向所管理 Panel 幂等传播）
            rootLoader = loader;
            EnsureRuntimeRoots();
            InitializeLayer(pages, loader);
            InitializeLayer(modals, loader);
            InitializeLayer(hud, loader);
            InitializeLayer(notifications, loader);
        }

        private void EnsureRuntimeRoots()
        {
            if (rootsCreated)
            {
                return;
            }

            canvasObject = CreateCanvasRoot();
            pages = CreateRootContainer<PageContainer>(canvasObject.transform, PageContainerObjectName);
            modals = CreateRootContainer<ModalContainer>(canvasObject.transform, ModalContainerObjectName);
            hud = CreateRootContainer<OverlayContainer>(canvasObject.transform, HudLayerObjectName);
            notifications = CreateRootContainer<OverlayContainer>(
                canvasObject.transform, NotificationLayerObjectName);
            rootsCreated = true;
        }

        //根 Layer 存活检查与自动重建：销毁后以原名重建、回到原排序位，
        //已 Initialize 过的导航器沿用既有 Loader 完成初始化；未建立过根 Layer 的导航器不惰性建层
        private TContainer EnsureLayer<TContainer>(ref TContainer field, string objectName, int siblingIndex)
            where TContainer : PanelContainer
        {
            if (field == null && rootsCreated)
            {
                field = CreateRootContainer<TContainer>(EnsureCanvasRoot(), objectName);
                field.transform.SetSiblingIndex(siblingIndex);
                InitializeLayer(field, rootLoader);
            }

            return field;
        }

        private void InitializeLayer(PanelContainer container, IPanelLoader loader)
        {
            if (container != null && loader != null && !container.IsInitialized)
            {
                container.Initialize(loader);
            }
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

        private static TContainer CreateRootContainer<TContainer>(Transform parent, string objectName)
            where TContainer : Component
        {
            GameObject containerObject = new GameObject(objectName, typeof(RectTransform), typeof(TContainer));
            containerObject.transform.SetParent(parent, false);

            RectTransform rect = containerObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            return containerObject.GetComponent<TContainer>();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

    }
}