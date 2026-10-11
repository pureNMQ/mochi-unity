using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.ServiceLocator;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Mochi.Unity.GUI
{
    public static class PanelFactory
    {
        /// <summary>
        /// 使用当前组合根容器或独立容器，在 Prepare 前完成注入；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            CancellationToken cancellationToken) where TPanel : Panel
        {
            return CreateAsync<TPanel>(loader, key, parent, Services.Root ?? new ServiceProvider(), cancellationToken);
        }

        /// <summary>
        /// 使用当前组合根容器或独立容器，在 Prepare 前完成注入；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel, TData>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            CancellationToken cancellationToken) where TPanel : Panel<TData>
        {
            return CreateAsync<TPanel, TData>(
                loader, key, parent, data, Services.Root ?? new ServiceProvider(), cancellationToken);
        }

        /// <summary>
        /// 使用当前组合根容器或独立容器，在 Prepare 前完成注入；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel, TData, TResult>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            CancellationToken cancellationToken) where TPanel : ModalPanel<TData, TResult>
        {
            return CreateAsync<TPanel, TData, TResult>(
                loader, key, parent, data, Services.Root ?? new ServiceProvider(), cancellationToken);
        }

        /// <summary>
        /// 创建并完成 Prepare 与 Open 的无数据面板；非空的 <paramref name="services"/> 于 Prepare 前绑定到面板；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            ServiceProvider services,
            CancellationToken cancellationToken) where TPanel : Panel
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                services,
                (panel, token) => panel.PrepareInternalAsync(token),
                cancellationToken);
        }

        /// <summary>
        /// 创建并完成 Prepare 与 Open 的 <typeparamref name="TData"/> 数据面板；
        /// 非空的 <paramref name="services"/> 于 Prepare 前绑定到面板；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel, TData>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            ServiceProvider services,
            CancellationToken cancellationToken) where TPanel : Panel<TData>
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                services,
                (panel, token) => panel.PrepareInternalAsync(data, token),
                cancellationToken);
        }

        /// <summary>
        /// 创建并完成 Prepare 与 Open 的 <typeparamref name="TData"/> 数据、
        /// <typeparamref name="TResult"/> 结果模态面板；非空的 <paramref name="services"/> 于 Prepare 前绑定到面板；返回后须另行调用 Show。
        /// </summary>
        public static UniTask<TPanel> CreateAsync<TPanel, TData, TResult>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            ServiceProvider services,
            CancellationToken cancellationToken) where TPanel : ModalPanel<TData, TResult>
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                services,
                (panel, token) => panel.PrepareInternalAsync(data, token),
                cancellationToken);
        }

        //重载只决定 Prepare 的数据形态，创建到打开的主体收敛在私有核心
        private static async UniTask<TPanel> CreateCoreAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            ServiceProvider services,
            Func<TPanel, CancellationToken, UniTask> prepareAsync,
            CancellationToken cancellationToken) where TPanel : Panel
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            GameObject prefab = await loader.LoadAsync(key, cancellationToken);
            if (prefab == null)
            {
                return null;
            }

            // 准备阶段可能等待资源，因此完成准备前保持失活。
            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.SetActive(false);

            TPanel panel = instance.GetComponent<TPanel>();
            if (panel == null)
            {
                Object.Destroy(instance);
                loader.Release(prefab);
                return null;
            }

            //句柄由 Panel 自持，销毁时自释放
            panel.BindPrefabHandle(loader, prefab);
            //服务容器出生即注入：Prepare 回调内即须可用，故绑定先于 Prepare
            panel.BindServices(services);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await prepareAsync(panel, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                panel.OpenInternal();
                return panel;
            }
            catch
            {
                //失败收尾走立即终结，异常原样上抛
                panel.CloseImmediate();
                throw;
            }
        }
    }
}
