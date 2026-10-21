using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Mochi.Unity.GUI
{
    /// <summary>创建、挂载、登记并释放运行时面板实例，并保证运行环境有一个可派发 UI 输入的 EventSystem。</summary>
    public sealed class GUIService : IDisposable
    {
        private const string UiMapName = "UI";

        //UI 模块按动作名逐条接线，缺任何一条都表现为对应输入永久失效且不报错，
        //因此全部动作在改动模块之前一次核完，缺项集中抛出
        private static readonly string[] UiActionNames =
        {
            "Point",
            "Move",
            "Submit",
            "Cancel",
            "LeftClick",
            "MiddleClick",
            "RightClick",
            "ScrollWheel"
        };

        private sealed class Entry
        {
            public Entry(Type panelType)
            {
                PanelType = panelType;
                Completion = new UniTaskCompletionSource<Panel>();
            }

            public Type PanelType { get; }
            public UniTaskCompletionSource<Panel> Completion { get; }
            public Panel Panel { get; set; }
        }

        private readonly UIRoot uiRoot;
        private readonly IPanelLoader panelLoader;
        private readonly ServiceProvider services;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool disposed;

        /// <summary>使用指定 UI 根、资源加载器和服务容器创建 GUI 服务，并保证 EventSystem 可用。</summary>
        /// <param name="uiActions">
        /// 项目的 UI 输入资产；提供后 UI 输入模块改用该资产的 UI Map，传 null 表示项目未提供，模块保持自带默认资产。
        /// 资产的生命周期归调用方，本服务只在构造时读取。
        /// </param>
        public GUIService(
            UIRoot uiRoot,
            IPanelLoader panelLoader,
            ServiceProvider services = null,
            InputActionAsset uiActions = null)
        {
            this.uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            this.panelLoader = panelLoader ?? throw new ArgumentNullException(nameof(panelLoader));
            this.services = services ?? Services.Root ?? new ServiceProvider();
            EnsureEventSystem(uiActions);
        }

        //EventSystem 的存在保证收在服务里：显式放置 UI 根的场景与应用装配路径共用同一条兜底，
        //业务侧因此不需要在场景里放 EventSystem，也不会因为漏放而在运行期完全失去导航
        private void EnsureEventSystem(InputActionAsset uiActions)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                //未激活的 EventSystem 不占 current，但再建一个就会让场景里长期并存两个派发器
                eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            }

            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem");
                //挂在 UI 根下与其同寿命：既不随创建时刻的 Active Scene 卸载而失效，
                //也不会在 UI 根销毁后残留为孤儿全局对象
                eventSystemObject.transform.SetParent(uiRoot.transform, false);
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                if (eventSystem.TryGetComponent(out BaseInputModule existingModule))
                {
                    //别的输入模块不是 Input System 的动作派发器，项目 UI Map 无处落地；
                    //换模块会改变既有场景的输入后端，超出本服务的职责，只能报错交给装配方处理
                    throw new InvalidOperationException(
                        $"EventSystem '{eventSystem.name}' uses {existingModule.GetType().Name}, which cannot take project UI actions.");
                }

                module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            if (uiActions != null)
            {
                ConfigureUiActions(module, uiActions);
            }
        }

        private static void ConfigureUiActions(InputSystemUIInputModule module, InputActionAsset uiActions)
        {
            InputActionMap uiMap = uiActions.FindActionMap(UiMapName);
            if (uiMap == null)
            {
                throw new InvalidOperationException(
                    $"Input asset '{uiActions.name}' has no '{UiMapName}' action map.");
            }

            InputAction[] actions = new InputAction[UiActionNames.Length];
            List<string> missing = new List<string>();
            for (int i = 0; i < UiActionNames.Length; i++)
            {
                actions[i] = uiMap.FindAction(UiActionNames[i]);
                if (actions[i] == null)
                {
                    missing.Add(UiActionNames[i]);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Input asset '{uiActions.name}' UI map is missing actions: {string.Join(", ", missing)}.");
            }

            bool wasEnabled = module.enabled;
            //先停模块再改绑定：模块 OnEnable 在无动作时会自己塞进 new DefaultInputActions()，
            //OnDisable 会释放它，UnassignActions 再清掉残留引用，这样下面设 actionsAsset 时
            //setter 不会按“Map 名 + 动作名”把旧默认资产的同名动作错接到新资产上
            //（Point/Submit 等同名动作会被接上，Move/LeftClick 这类改名的动作会被清空）
            module.enabled = false;
            module.UnassignActions();
            module.actionsAsset = uiActions;
            module.point = InputActionReference.Create(actions[0]);
            module.move = InputActionReference.Create(actions[1]);
            module.submit = InputActionReference.Create(actions[2]);
            module.cancel = InputActionReference.Create(actions[3]);
            module.leftClick = InputActionReference.Create(actions[4]);
            module.middleClick = InputActionReference.Create(actions[5]);
            module.rightClick = InputActionReference.Create(actions[6]);
            module.scrollWheel = InputActionReference.Create(actions[7]);
            //重新启用交给模块自己的 OnEnable 一次 Hook 并 Enable 全部动作，避免在这里逐条补 enable 漏项
            module.enabled = wasEnabled;
        }

        /// <summary>按资源地址创建并登记面板。相同实例 ID 的并发请求共享一次创建。</summary>
        public UniTask<TPanel> CreatePanelAsync<TPanel>(string address, string instanceId = null)
            where TPanel : Panel
        {
            return CreatePanelAsyncCore<TPanel>(
                address,
                instanceId,
                CancellationToken.None,
                token => PanelFactory.CreateAsync<TPanel>(panelLoader, address, null, services, token));
        }

        /// <summary>按资源地址创建并登记带数据的面板。相同实例 ID 的并发请求共享一次创建。</summary>
        public UniTask<TPanel> CreatePanelAsync<TPanel, TData>(
            TData data,
            string address,
            string instanceId = null)
            where TPanel : Panel, IDataPanel<TData>
        {
            return CreatePanelAsyncCore<TPanel>(
                address,
                instanceId,
                CancellationToken.None,
                token => PanelFactory.CreateAsync<TPanel, TData>(panelLoader, address, null, data, services, token));
        }

        public UniTask<TPanel> CreateModalAsync<TPanel, TData, TResult>(
            TData data,
            string address,
            string instanceId = null)
            where TPanel : ModalPanel<TData, TResult>
        {
            return CreatePanelAsyncCore<TPanel>(
                address,
                instanceId,
                CancellationToken.None,
                token => PanelFactory.CreateAsync<TPanel, TData, TResult>(panelLoader, address, null, data, services, token));
        }

        /// <summary>创建面板并挂载到指定层后显示。</summary>
        public async UniTask<TPanel> CreateAndShowPanelAsync<TPanel>(
            string address,
            GUILayer layer,
            string instanceId = null)
            where TPanel : Panel
        {
            TPanel panel = await CreatePanelAsync<TPanel>(address, instanceId);
            if (panel == null)
            {
                return null;
            }

            InsertPanel(panel, layer);
            panel.Show();
            return panel;
        }

        /// <summary>按实例 ID 查询指定类型的面板；未登记时返回 null。</summary>
        public TPanel GetPanel<TPanel>(string instanceId) where TPanel : Panel
        {
            if (string.IsNullOrEmpty(instanceId) || !entries.TryGetValue(instanceId, out Entry entry))
            {
                return null;
            }

            if (entry.PanelType != typeof(TPanel))
            {
                throw new InvalidOperationException(
                    $"Panel instance '{instanceId}' is registered as {entry.PanelType.FullName}, not {typeof(TPanel).FullName}.");
            }

            return entry.Panel as TPanel;
        }

        /// <summary>按实例 ID 查询任意已登记面板；未登记时返回 null。</summary>
        public Panel GetPanel(string instanceId)
        {
            return string.IsNullOrEmpty(instanceId) || !entries.TryGetValue(instanceId, out Entry entry)
                ? null
                : entry.Panel;
        }

        /// <summary>解析由 UI 根管理的指定层。</summary>
        public RectTransform GetLayer(GUILayer layer)
        {
            return uiRoot.GetLayer(layer);
        }

        /// <summary>将已创建面板移动到指定层；None 表示脱离 UI 层。</summary>
        public void InsertPanel(Panel panel, GUILayer layer)
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            RectTransform target = uiRoot.GetLayer(layer);
            panel.transform.SetParent(target, false);
        }

        /// <summary>关闭并释放服务持有的面板与资源；重复调用无副作用。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (Entry entry in new List<Entry>(entries.Values))
            {
                if (entry.Panel != null)
                {
                    entry.Panel.Closed -= OnPanelClosed;
                    entry.Panel.CloseImmediate();
                }
            }

            entries.Clear();
            if (panelLoader is IDisposable disposableLoader)
            {
                disposableLoader.Dispose();
            }
        }

        private async UniTask<TPanel> CreatePanelAsyncCore<TPanel>(
            string address,
            string instanceId,
            CancellationToken cancellationToken,
            Func<CancellationToken, UniTask<TPanel>> factory)
            where TPanel : Panel
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GUIService));
            }

            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("Panel address is required.", nameof(address));
            }

            string key = string.IsNullOrEmpty(instanceId) ? address : instanceId;
            if (entries.TryGetValue(key, out Entry existing))
            {
                EnsureType(existing, key, typeof(TPanel));
                Panel shared = await existing.Completion.Task;
                return shared as TPanel;
            }

            Entry entry = new Entry(typeof(TPanel));
            entries.Add(key, entry);
            PopulateEntryAsync<TPanel>(key, entry, cancellationToken, factory).Forget();
            Panel panel = await entry.Completion.Task;
            return panel as TPanel;
        }

        private async UniTaskVoid PopulateEntryAsync<TPanel>(
            string instanceId,
            Entry entry,
            CancellationToken cancellationToken,
            Func<CancellationToken, UniTask<TPanel>> factory)
            where TPanel : Panel
        {
            try
            {
                TPanel panel = await factory(cancellationToken);
                if (panel == null)
                {
                    throw new InvalidOperationException($"Panel factory returned no panel for instance '{instanceId}'.");
                }

                entry.Panel = panel;
                panel.Closed += OnPanelClosed;
                entry.Completion.TrySetResult(panel);
            }
            catch (Exception exception)
            {
                entries.Remove(instanceId);
                entry.Completion.TrySetException(exception);
            }
        }

        private void OnPanelClosed(Panel panel)
        {
            panel.Closed -= OnPanelClosed;
            string keyToRemove = null;
            foreach (KeyValuePair<string, Entry> pair in entries)
            {
                if (ReferenceEquals(pair.Value.Panel, panel))
                {
                    keyToRemove = pair.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                entries.Remove(keyToRemove);
            }
        }

        private static void EnsureType(Entry entry, string instanceId, Type requestedType)
        {
            if (entry.PanelType != requestedType)
            {
                throw new InvalidOperationException(
                    $"Panel instance '{instanceId}' has type conflict: {entry.PanelType.FullName} versus {requestedType.FullName}.");
            }
        }
    }
}
