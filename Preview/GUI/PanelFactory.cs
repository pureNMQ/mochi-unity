using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Mochi.Unity.Preview.GUI
{
    public static class PanelFactory
    {
        // 创建到打开的全部流程归工厂：configure 供调用方在 Prepare 前完成自有绑定（如 Modal 的 Bind）
        public static async UniTask<TPanel> CreateAsync<TPanel>(
            IPanelLoader loader,
            string key,
            Transform parent,
            Action<TPanel> configure,
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
                configure?.Invoke(panel);

                if (prepareAsync != null)
                {
                    await prepareAsync(panel, cancellationToken);
                }
                else
                {
                    await panel.PrepareInternalAsync(cancellationToken);
                }

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
