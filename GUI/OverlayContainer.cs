using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    public sealed class OverlayContainer : PanelContainer
    {
        private readonly Dictionary<string, Panel> registeredPanels = new Dictionary<string, Panel>();
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();

        protected override CancellationToken LifetimeCancellationToken => lifetimeCancellation.Token;

        /// <summary>
        /// 在容器中打开一个面板实例，如果已存在则返回已存在的实例
        /// </summary>
        /// <typeparam name="TPanel">面板类型</typeparam>
        /// <param name="key">面板预制体的资源键值</param>
        /// <returns>创建（或既有）的面板实例；忙碌或不可用时返回 null</returns>
        public async UniTask<TPanel> OpenAsync<TPanel>(string key) where TPanel : Panel
        {
            if (TryGetRegistered(key, out Panel existing))
            {
                if (existing is TPanel typedExisting)
                {
                    return typedExisting;
                }

                return null;
            }

            if (IsDisposed)
            {
                return null;
            }

            if (Loader == null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} must be initialized before opening a panel.");
            }

            if (IsBusy)
            {
                return null;
            }

            IsBusy = true;
            try
            {
                TPanel panel = await PanelFactory.CreateAsync<TPanel>(
                    Loader, key, transform, LifetimeCancellationToken);
                if (panel == null)
                {
                    return null;
                }

                registeredPanels[key] = panel;
                panel.Closed += HandleRegisteredPanelClosed;

                panel.Show();
                return panel;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public bool TryGetPanel(string key, out Panel panel)
        {
            if (TryGetRegistered(key, out Panel existing))
            {
                panel = existing;
                return true;
            }

            panel = null;
            return false;
        }

        public bool TryGetPanel<TPanel>(string key, out TPanel panel) where TPanel : Panel
        {
            if (TryGetRegistered(key, out Panel existing) && existing is TPanel typed)
            {
                panel = typed;
                return true;
            }

            panel = null;
            return false;
        }

        /// <summary>关闭全部登记面板，容器保留可用；除名随各面板 Closed 事件完成</summary>
        public void Clear()
        {
            if (IsDisposed)
            {
                return;
            }

            //快照遍历：关闭过程会经 Closed 事件改写登记表
            List<Panel> owned = new List<Panel>(registeredPanels.Values);
            foreach (Panel panel in owned)
            {
                if (panel != null)
                {
                    panel.Close();
                }
            }
        }

        //已销毁实例视为未登记：Close 自毁后同一 key 可重建
        private bool TryGetRegistered(string key, out Panel panel)
        {
            return registeredPanels.TryGetValue(key, out panel) && panel != null;
        }

        //面板自行关闭（外部直接 Close 等）即除名：登记随逻辑关闭即时清理，不等延迟销毁
        private void HandleRegisteredPanelClosed(Panel panel)
        {
            panel.Closed -= HandleRegisteredPanelClosed;

            string key = null;
            foreach (KeyValuePair<string, Panel> entry in registeredPanels)
            {
                if (ReferenceEquals(entry.Value, panel))
                {
                    key = entry.Key;
                    break;
                }
            }

            if (key != null)
            {
                registeredPanels.Remove(key);
            }
        }

        protected override void OnDestroy()
        {
            if (IsDisposed)
            {
                lifetimeCancellation.Dispose();
                base.OnDestroy();
                return;
            }

            IsDisposed = true;
            IsBusy = false;
            lifetimeCancellation.Cancel();

            //强杀路径先解除监听再关闭，避免关闭事件回写登记表
            List<Panel> owned = new List<Panel>(registeredPanels.Values);
            foreach (Panel panel in owned)
            {
                if (panel != null)
                {
                    panel.Closed -= HandleRegisteredPanelClosed;
                    panel.CloseImmediate();
                }
            }

            registeredPanels.Clear();
            lifetimeCancellation.Dispose();
            base.OnDestroy();
        }
    }
}