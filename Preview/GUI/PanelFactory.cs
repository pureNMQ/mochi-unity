using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Mochi.Unity.Preview.GUI
{
    //工厂只负责构造到 OnOpen；容器所有权绑定（如 Modal 的 Bind）由调用方在返回后完成
    public static class PanelFactory
    {
        public static UniTask<TPanel> CreateAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            CancellationToken cancellationToken) where TPanel : Panel
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                (panel, token) => panel.PrepareInternalAsync(token),
                cancellationToken);
        }

        public static UniTask<TPanel> CreateAsync<TPanel, TData>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            CancellationToken cancellationToken) where TPanel : Panel<TData>
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                (panel, token) => panel.PrepareInternalAsync(data, token),
                cancellationToken);
        }

        public static UniTask<TPanel> CreateAsync<TPanel, TData, TResult>(
            IPanelLoader loader,
            string key,
            Transform parent,
            TData data,
            CancellationToken cancellationToken) where TPanel : ModalPanel<TData, TResult>
        {
            return CreateCoreAsync<TPanel>(
                loader,
                key,
                parent,
                (panel, token) => panel.PrepareInternalAsync(data, token),
                cancellationToken);
        }

        //重载只决定 Prepare 的数据形态，创建到打开的主体收敛在私有核心
        private static async UniTask<TPanel> CreateCoreAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            Func<TPanel, CancellationToken, UniTask> prepareAsync,
            CancellationToken cancellationToken) where TPanel : Panel
        {
            GameObject prefab = await loader.LoadAsync(key, cancellationToken);
            if (prefab == null)
            {
                return null;
            }

            //实例化即失活：显示与入场由调用方决定
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
