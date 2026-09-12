using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Mochi.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

using ILogger = Mochi.Logging.ILogger;

namespace Mochi.Unity.Preview.GUI
{
    public sealed class AddressablesPanelLoader : IPanelLoader, IDisposable
    {
        // 分类 Logger：分类名沿用模块路径（Preview.GUI）加类型角色（PanelLoader），便于按来源过滤和定位问题。
        // Writer 由项目日志初始化统一注册，Loader 只产生日志，不自行注册输出；
        // using 别名沿用项目惯例（如 BattleTestStartup），避免与 UnityEngine.ILogger 产生二义性。
        private static readonly ILogger logger = LogManager.GetLogger("Preview.GUI.PanelLoader");

        private sealed class Entry
        {
            public Entry(string key, AsyncOperationHandle<GameObject> handle)
            {
                Key = key;
                Handle = handle;
                LoadTask = handle.Task;
            }

            public string Key { get; }
            public GameObject Prefab { get; set; }
            public AsyncOperationHandle<GameObject> Handle { get; }
            public Task<GameObject> LoadTask { get; }
            public int WaiterCount { get; set; }
            public int ReferenceCount { get; set; }

            // 同一 Entry 的失败只记录一次日志，避免多个等待者共享同一失败结果时重复刷日志。
            public bool FailureLogged { get; set; }
        }

        private readonly Dictionary<string, Entry> entriesByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, Entry> entriesByPrefab = new Dictionary<GameObject, Entry>();
        private Task initializationTask;
        private bool initializationStarted;

        public async UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);

            // 尝试从缓存中获取预制体，如果不存在则登记新的Entry
            if (!entriesByKey.TryGetValue(key, out Entry entry))
            {
                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(key);
                entry = new Entry(key, handle);
                entriesByKey.Add(key, entry);
            }

            //如果预制体为null，则表示正在加载
            if (entry.Prefab != null)
            {
                entry.ReferenceCount++;
                return entry.Prefab;
            }

            entry.WaiterCount++;
            try
            {
                GameObject prefab = await entry.LoadTask.AsUniTask()
                    .AttachExternalCancellation(cancellationToken);

                if (entry.Handle.Status != AsyncOperationStatus.Succeeded || prefab == null)
                {
                    // 状态失败或返回无效资源：记录 key 与原因，避免留下无原因的加载失败。
                    LogLoadFailure(entry, DescribeStatusFailure(entry));
                    RemoveAndRelease(entry);
                    return null;
                }

                if (entry.Prefab == null)
                {
                    entry.Prefab = prefab;
                    entriesByPrefab.Add(prefab, entry);
                }

                entry.ReferenceCount++;
                return entry.Prefab;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 预期取消（如容器销毁）：不记录错误日志，保持取消向调用方传播。
                throw;
            }
            catch (Exception exception)
            {
                // 加载异常：记录 key 与异常 ToString（保留类型与堆栈），按既有契约返回 null。
                LogLoadFailure(entry, exception.ToString());
                RemoveAndRelease(entry);
                return null;
            }
            finally
            {
                entry.WaiterCount--;
                if (entry.WaiterCount == 0 && entry.Prefab == null)
                {
                    RemoveAndRelease(entry);
                }
            }
        }

        private async UniTask EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (!initializationStarted)
            {
                initializationStarted = true;
                initializationTask = Addressables.InitializeAsync().Task;
            }

            await initializationTask.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        public void Release(GameObject prefab)
        {
            if (prefab == null || !entriesByPrefab.TryGetValue(prefab, out Entry entry))
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
            {
                return;
            }

            entriesByKey.Remove(entry.Key);
            entriesByPrefab.Remove(prefab);
            ReleaseHandle(entry.Handle);
        }

        public void Dispose()
        {
            foreach (Entry entry in entriesByKey.Values)
            {
                ReleaseHandle(entry.Handle);
            }

            entriesByKey.Clear();
            entriesByPrefab.Clear();
        }

        private static void LogLoadFailure(Entry entry, string reason)
        {
            // 同一 Entry 只记录一次，防止并发等待者收到同一失败结果时重复输出。
            if (entry.FailureLogged)
            {
                return;
            }

            entry.FailureLogged = true;
            logger.Error($"加载面板资源失败 key={entry.Key} reason={reason}");
        }

        private static string DescribeStatusFailure(Entry entry)
        {
            // 状态分支失败：优先附带 Addressables 原始异常，保留类型与堆栈。
            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Exception operationException = entry.Handle.OperationException;
                if (operationException != null)
                {
                    return operationException.ToString();
                }

                return $"status={entry.Handle.Status}";
            }

            // 资源加载标记成功但结果为 null：返回无效资源，同样需要可定位的原因。
            return "status=Succeeded 但加载结果为 null";
        }

        private void RemoveAndRelease(Entry entry)
        {
            if (!entriesByKey.TryGetValue(entry.Key, out Entry currentEntry) ||
                !ReferenceEquals(currentEntry, entry))
            {
                return;
            }

            entriesByKey.Remove(entry.Key);
            if (entry.Prefab != null)
            {
                entriesByPrefab.Remove(entry.Prefab);
            }

            ReleaseHandle(entry.Handle);
        }

        private static void ReleaseHandle(AsyncOperationHandle<GameObject> handle)
        {
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }
    }
}
