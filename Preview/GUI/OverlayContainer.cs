using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    //统一覆盖层容器：职责仅挂载点、根 Layer 排序、实例登记与查询；
    //不托管 Show/Hide、不执行 Close、不池化、无策略配置、无 KeepAlive 概念，
    //经 Open 入口创建的 Panel 归属本容器（HUD/Notification 根 Layer 各持一实例，不派生子类型）
    public sealed class OverlayContainer : PanelContainer
    {
        private readonly Dictionary<string, Panel> registeredPanels = new Dictionary<string, Panel>();
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();

        protected override CancellationToken LifetimeCancellationToken => lifetimeCancellation.Token;

        /// <summary>
        /// 打开面板：经工厂完成创建到打开的全部流程，登记归属并初始即显示。
        /// 已登记的 key 幂等返回既有实例（需重建先对该实例 Close）。
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
                    Loader, key, transform, null, null, LifetimeCancellationToken);
                if (panel == null)
                {
                    return null;
                }

                registeredPanels[key] = panel;
                panel.Closed += HandleRegisteredPanelClosed;

                //创建即显示：显示即提交状态、启用输入与焦点，入场过渡由 Panel fire-and-forget；
                //后续显隐走 Panel 公开 API，容器不托管
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

        //不托管所登记 Panel 的显隐：整树隐藏随父级停用自然发生，逻辑显隐由 Panel 公开 API 决定
        protected override void PropagateHideToStack()
        {
        }

        //无栈可呈现：各 Panel 的显示恢复由其自身 API 执行
        protected override void PresentOnShow()
        {
        }

        protected override void OnDestroy()
        {
            if (IsDisposed)
            {
                lifetimeCancellation.Dispose();
                base.OnDestroy();
                return;
            }

            //入口最先标记不可用并取消在途操作，随后对所有归属 Panel 强杀：
            //立即销毁并释放句柄，不等退场动画；登记随销毁清空
            IsDisposed = true;
            IsBusy = false;
            lifetimeCancellation.Cancel();
            try
            {
                //快照遍历：强杀路径先解除监听再关闭，避免关闭事件回写登记表
                List<Panel> owned = new List<Panel>(registeredPanels.Values);
                foreach (Panel panel in owned)
                {
                    if (panel != null)
                    {
                        panel.Closed -= HandleRegisteredPanelClosed;
                        panel.Close();
                        panel.ReleasePrefabHandleImmediate();
                        Destroy(panel.gameObject);
                    }
                }
            }
            finally
            {
                registeredPanels.Clear();
                lifetimeCancellation.Dispose();
                base.OnDestroy();
            }
        }
    }
}