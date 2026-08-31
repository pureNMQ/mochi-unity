using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mochi.Unity.Preview.GUI
{
    public sealed class AddressablesPanelLoader : IPanelLoader, IDisposable
    {
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
                throw;
            }
            catch
            {
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
