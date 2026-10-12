using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    /// <summary>创建、挂载、登记并释放运行时面板实例。</summary>
    public sealed class GUIService : IDisposable
    {
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

        /// <summary>使用指定 UI 根、资源加载器和服务容器创建 GUI 服务。</summary>
        public GUIService(UIRoot uiRoot, IPanelLoader panelLoader, ServiceProvider services = null)
        {
            this.uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            this.panelLoader = panelLoader ?? throw new ArgumentNullException(nameof(panelLoader));
            this.services = services ?? Services.Root ?? new ServiceProvider();
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

        public UniTask<TPanel> CreatePanelAsync<TPanel, TData>(
            TData data,
            string address,
            string instanceId = null)
            where TPanel : Panel<TData>
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
