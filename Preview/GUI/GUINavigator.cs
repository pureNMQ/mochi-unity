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

        private static GUINavigator instance;
        private PageContainer pages;
        private ModalContainer modals;

        public static GUINavigator Instance => instance;
        public PageContainer Pages => pages;
        public ModalContainer Modals => modals;

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
            //C1 核对说明：根容器（Pages/Modals）Initialize 成功即处于逻辑显示状态——
            //容器级视觉跳过标志默认复位、首个 Push 正常执行 OnShow 与入场过渡，业务无需额外 Show 调用；
            //整树隐藏/恢复使用容器级 Hide/ShowAsync（经 Internal 入口向所管理 Panel 幂等传播）。
            EnsureRuntimeRoots();
            pages.Initialize(loader);
            modals.Initialize(loader);
        }

        private void EnsureRuntimeRoots()
        {
            if (pages != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject(
                CanvasObjectName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject pageContainerObject = new GameObject(
                PageContainerObjectName,
                typeof(RectTransform),
                typeof(PageContainer));
            pageContainerObject.transform.SetParent(canvasObject.transform, false);

            RectTransform pageContainerRect = pageContainerObject.GetComponent<RectTransform>();
            pageContainerRect.anchorMin = Vector2.zero;
            pageContainerRect.anchorMax = Vector2.one;
            pageContainerRect.offsetMin = Vector2.zero;
            pageContainerRect.offsetMax = Vector2.zero;
            pageContainerRect.localScale = Vector3.one;

            pages = pageContainerObject.GetComponent<PageContainer>();

            GameObject modalContainerObject = new GameObject(
                ModalContainerObjectName,
                typeof(RectTransform),
                typeof(ModalContainer));
            modalContainerObject.transform.SetParent(canvasObject.transform, false);

            RectTransform modalContainerRect = modalContainerObject.GetComponent<RectTransform>();
            modalContainerRect.anchorMin = Vector2.zero;
            modalContainerRect.anchorMax = Vector2.one;
            modalContainerRect.offsetMin = Vector2.zero;
            modalContainerRect.offsetMax = Vector2.zero;
            modalContainerRect.localScale = Vector3.one;

            modals = modalContainerObject.GetComponent<ModalContainer>();
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
