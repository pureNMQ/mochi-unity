using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
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

        public static GUINavigator Instance => instance != null ? instance : CreateDefaultInstance();

        //根 Layer 被销毁时按需自动重建;
        public PageContainer Pages => EnsureLayer(ref pages, PageContainerObjectName, 1);
        public ModalContainer Modals => EnsureLayer(ref modals, ModalContainerObjectName, 2);
        public OverlayContainer HUD => EnsureLayer(ref hud, HudLayerObjectName, 0);
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
            rootLoader = loader;
            EnsureRuntimeRoots();
            InitializeLayer(hud, loader);
            InitializeLayer(pages, loader);
            InitializeLayer(modals, loader);
            InitializeLayer(notifications, loader);
        }

        private void EnsureRuntimeRoots()
        {
            if (rootsCreated)
            {
                return;
            }

            canvasObject = CreateCanvasRoot();
            hud = CreateRootContainer<OverlayContainer>(canvasObject.transform, HudLayerObjectName);
            pages = CreateRootContainer<PageContainer>(canvasObject.transform, PageContainerObjectName);
            modals = CreateRootContainer<ModalContainer>(canvasObject.transform, ModalContainerObjectName);
            notifications = CreateRootContainer<OverlayContainer>(
                canvasObject.transform, NotificationLayerObjectName);

            //根 Layer 创建即逻辑显示，首个导航带视觉
            hud.Show();
            pages.Show();
            modals.Show();
            notifications.Show();
            rootsCreated = true;
        }

        //懒创建：业务首次访问时自建常驻导航器，不使用 UI 的场景永不创建
        private static GUINavigator CreateDefaultInstance()
        {
            GameObject root = new GameObject("GUINavigator");
            DontDestroyOnLoad(root);
            GUINavigator navigator = root.AddComponent<GUINavigator>();
            navigator.Initialize(new AddressablesPanelLoader());
            //兜底仅限懒创建路径：显式安装的场景（含测试）自备 EventSystem，避免双实例抢占 current
            EnsureEventSystem();
            return navigator;
        }

        //复用场景已有 EventSystem，仅缺失时兜底创建
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            //兜底 EventSystem 必须与懒创建的导航器同寿命：它创建于访问那一刻的 Active Scene，
            //若留在该场景里，场景卸载后导航器仍在而 UI 输入静默失效
            DontDestroyOnLoad(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        //根 Layer 存活检查与自动重建：销毁后以原名重建、回到原排序位，
        private TContainer EnsureLayer<TContainer>(ref TContainer field, string objectName, int siblingIndex)
            where TContainer : PanelContainer
        {
            if (field == null && rootsCreated)
            {
                field = CreateRootContainer<TContainer>(EnsureCanvasRoot(), objectName);
                field.transform.SetSiblingIndex(siblingIndex);
                InitializeLayer(field, rootLoader);
                field.Show();
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

            (rootLoader as IDisposable)?.Dispose();
        }

    }
}