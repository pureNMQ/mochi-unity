using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochi.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.TestTools;

namespace Mochi.Unity.GUI.Tests
{
    /// <summary>
    /// R8：验证 AddressablesPanelLoader 的加载失败诊断日志。
    /// 失败仍返回 null（契约不变），但会通过 Mochi 日志产生包含资源 key 与异常信息的 Error 记录；
    /// 预期取消保持取消传播且不记录错误。
    /// 通过 LogManager.AddWriter / RemoveWriter 注册内存记录 writer；用例结束移除，避免污染其他测试。
    /// </summary>
    public class PanelLoaderErrorLoggingTests
    {
        // Mochi.Logging.LogLevel.Error 的数值（见 Packages/Mochi/Logging/LogLevel.cs）。
        private const int ErrorLevel = (int)LogLevel.Error;

        // 与 AddressablesPanelLoader 约定的日志分类名。
        private const string LoaderCategory = "Preview.GUI.PanelLoader";

        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }

            objectsToDestroy.Clear();
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingKey_ReturnsNullAndLogsSingleErrorWithKeyAndException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await Addressables.InitializeAsync().Task.AsUniTask();

                AddressablesPanelLoader loader = new AddressablesPanelLoader();
                var previousExceptionHandler = UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler;
                UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = null;
                CapturedLogs logs = CaptureLogs();

                try
                {
                    string missingKey = $"Tests/MissingPanel/{Guid.NewGuid():N}";
                    GameObject result = await loader.LoadAsync(missingKey, CancellationToken.None);

                    // 失败契约保持不变：返回 null 而不是抛出。
                    Assert.That(result, Is.Null);

                    List<CapturedLog> errors = GetLoaderErrorEntries(logs);
                    Assert.That(errors.Count, Is.EqualTo(1), "加载失败应恰好记录一条 Error 日志。");
                    Assert.That(errors[0].Category, Is.EqualTo(LoaderCategory));
                    Assert.That(errors[0].Message, Does.Contain(missingKey));
                    // 日志需包含原始异常文本（保留异常类型信息，无效 key 为 InvalidKeyException）。
                    Assert.That(errors[0].Message, Does.Contain("Exception"));
                }
                finally
                {
                    logs.Dispose();
                    UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = previousExceptionHandler;
                    loader.Dispose();
                }
            });
        }

        [UnityTest]
        public IEnumerator LoadAsync_ConcurrentWaitersOnFailedLoad_LogErrorOnceWithKeyAndException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await Addressables.InitializeAsync().Task.AsUniTask();

                const string key = "Tests/FailedLoggingPanel";
                GameObject failedAsset = Track(new GameObject("FailedLoggingPanelAsset"));
                DelayedFailingProvider provider = new DelayedFailingProvider(failedAsset);
                ResourceLocationMap locator = new ResourceLocationMap("FailedLoggingPanelLocator");
                locator.Add(key, new ResourceLocationBase(key, key, provider.ProviderId, typeof(GameObject)));
                Addressables.ResourceManager.ResourceProviders.Add(provider);
                Addressables.AddResourceLocator(locator);

                AddressablesPanelLoader loader = new AddressablesPanelLoader();
                CapturedLogs logs = CaptureLogs();

                //合成 Provider 失败会被 Addressables 内部记录到 Unity 控制台，属本用例预期噪音
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    UniTask<GameObject> firstLoad = loader.LoadAsync(key, CancellationToken.None);
                    UniTask<GameObject> secondLoad = loader.LoadAsync(key, CancellationToken.None);
                    await UniTask.WaitUntil(() => provider.ProvideCount == 1);

                    provider.Fail(new InvalidOperationException("Synthetic provider failure."));
                    (GameObject first, GameObject second) = await UniTask.WhenAll(firstLoad, secondLoad);

                    Assert.That(first, Is.Null);
                    Assert.That(second, Is.Null);

                    List<CapturedLog> errors = GetLoaderErrorEntries(logs);
                    Assert.That(errors.Count, Is.EqualTo(1), "并发等待者共享同一失败结果时应只记录一条 Error 日志。");
                    Assert.That(errors[0].Category, Is.EqualTo(LoaderCategory));
                    Assert.That(errors[0].Message, Does.Contain(key));
                    // 异常文本需包含注入的原始异常消息。
                    Assert.That(errors[0].Message, Does.Contain("Synthetic provider failure."));
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                    logs.Dispose();
                    loader.Dispose();
                    Addressables.RemoveResourceLocator(locator);
                    Addressables.ResourceManager.ResourceProviders.Remove(provider);
                }
            });
        }

        [UnityTest]
        public IEnumerator LoadAsync_SucceedsWithNullAsset_ReturnsNullAndLogsReason()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await Addressables.InitializeAsync().Task.AsUniTask();

                const string key = "Tests/NullAssetPanel";
                DelayedNullAssetProvider provider = new DelayedNullAssetProvider();
                ResourceLocationMap locator = new ResourceLocationMap("NullAssetPanelLocator");
                locator.Add(key, new ResourceLocationBase(key, key, provider.ProviderId, typeof(GameObject)));
                Addressables.ResourceManager.ResourceProviders.Add(provider);
                Addressables.AddResourceLocator(locator);

                AddressablesPanelLoader loader = new AddressablesPanelLoader();
                CapturedLogs logs = CaptureLogs();

                //成功但资源为 null 的路径也会触发 Addressables 内部错误日志，属本用例预期噪音
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    UniTask<GameObject> load = loader.LoadAsync(key, CancellationToken.None);
                    await UniTask.WaitUntil(() => provider.ProvideCount == 1);

                    provider.CompleteWithNull();
                    GameObject result = await load;

                    // 加载标记成功但资源为 null，同样按失败契约返回 null。
                    Assert.That(result, Is.Null);

                    List<CapturedLog> errors = GetLoaderErrorEntries(logs);
                    Assert.That(errors.Count, Is.EqualTo(1), "返回无效资源也应记录一条 Error 日志。");
                    Assert.That(errors[0].Category, Is.EqualTo(LoaderCategory));
                    Assert.That(errors[0].Message, Does.Contain(key));
                    Assert.That(errors[0].Message, Does.Contain("加载结果为 null"));
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                    logs.Dispose();
                    loader.Dispose();
                    Addressables.RemoveResourceLocator(locator);
                    Addressables.ResourceManager.ResourceProviders.Remove(provider);
                }
            });
        }

        [UnityTest]
        public IEnumerator LoadAsync_ExpectedCancellation_PropagatesAndLogsNoError()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await Addressables.InitializeAsync().Task.AsUniTask();

                const string key = "Tests/CancelledLoggingPanel";
                GameObject prefab = Track(new GameObject("CancelledLoggingPanelPrefab"));
                DelayedGameObjectProvider provider = new DelayedGameObjectProvider(prefab);
                ResourceLocationMap locator = new ResourceLocationMap("CancelledLoggingPanelLocator");
                locator.Add(key, new ResourceLocationBase(key, key, provider.ProviderId, typeof(GameObject)));
                Addressables.ResourceManager.ResourceProviders.Add(provider);
                Addressables.AddResourceLocator(locator);

                AddressablesPanelLoader loader = new AddressablesPanelLoader();
                CapturedLogs logs = CaptureLogs();
                CancellationTokenSource cancellation = new CancellationTokenSource();

                try
                {
                    UniTask<GameObject> load = loader.LoadAsync(key, cancellation.Token);
                    await UniTask.WaitUntil(() => provider.ProvideCount == 1);

                    cancellation.Cancel();
                    await ExpectCanceledAsync(load);

                    // 收尾：完成挂起的 Provide，让释放路径执行完毕，避免影响后续用例。
                    provider.Complete();
                    await UniTask.WaitUntil(() => provider.ReleaseCount > 0)
                        .Timeout(TimeSpan.FromSeconds(1));

                    Assert.That(GetLoaderErrorEntries(logs), Is.Empty, "预期取消不应记录加载错误。");
                }
                finally
                {
                    logs.Dispose();
                    cancellation.Dispose();
                    loader.Dispose();
                    Addressables.RemoveResourceLocator(locator);
                    Addressables.ResourceManager.ResourceProviders.Remove(provider);
                }
            });
        }

        // —— 日志捕获辅助 ——

        private static CapturedLogs CaptureLogs()
        {
            RecordingLogWriter writer = new RecordingLogWriter();
            LogManager.AddWriter(writer);
            return new CapturedLogs(writer);
        }

        private static List<CapturedLog> GetLoaderErrorEntries(CapturedLogs logs)
        {
            List<CapturedLog> errors = new List<CapturedLog>();
            foreach (CapturedLog captured in logs.Entries)
            {
                if (captured.Level == ErrorLevel && captured.Category == LoaderCategory)
                {
                    errors.Add(captured);
                }
            }

            return errors;
        }

        private static async UniTask ExpectCanceledAsync(UniTask<GameObject> load)
        {
            try
            {
                await load;
                Assert.Fail("Expected the load to be canceled.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }

        // —— Addressables 测试用 Provider（沿用现有集成测试的搭建方式） ——

        private sealed class DelayedGameObjectProvider : IResourceProvider
        {
            private readonly GameObject prefab;
            private readonly string providerId = $"Tests.DelayedGameObjectProvider.{Guid.NewGuid():N}";
            private ProvideHandle pendingProvideHandle;
            private bool hasPendingProvide;

            public DelayedGameObjectProvider(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public string ProviderId => providerId;
            public ProviderBehaviourFlags BehaviourFlags => ProviderBehaviourFlags.None;
            public int ProvideCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public Type GetDefaultType(IResourceLocation location)
            {
                return typeof(GameObject);
            }

            public bool CanProvide(Type type, IResourceLocation location)
            {
                return type == typeof(GameObject) && location.ProviderId == ProviderId;
            }

            public void Provide(ProvideHandle provideHandle)
            {
                ProvideCount++;
                pendingProvideHandle = provideHandle;
                hasPendingProvide = true;
            }

            public void Release(IResourceLocation location, object asset)
            {
                ReleaseCount++;
            }

            public void Complete()
            {
                if (!hasPendingProvide)
                {
                    throw new InvalidOperationException("There is no pending provide operation.");
                }

                hasPendingProvide = false;
                pendingProvideHandle.Complete(prefab, true, null);
            }
        }

        private sealed class DelayedFailingProvider : IResourceProvider
        {
            private readonly GameObject asset;
            private readonly string providerId = $"Tests.DelayedFailingProvider.{Guid.NewGuid():N}";
            private ProvideHandle pendingProvideHandle;
            private bool hasPendingProvide;

            public DelayedFailingProvider(GameObject asset)
            {
                this.asset = asset;
            }

            public string ProviderId => providerId;
            public ProviderBehaviourFlags BehaviourFlags => ProviderBehaviourFlags.None;
            public int ProvideCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public Type GetDefaultType(IResourceLocation location)
            {
                return typeof(GameObject);
            }

            public bool CanProvide(Type type, IResourceLocation location)
            {
                return type == typeof(GameObject) && location.ProviderId == ProviderId;
            }

            public void Provide(ProvideHandle provideHandle)
            {
                ProvideCount++;
                pendingProvideHandle = provideHandle;
                hasPendingProvide = true;
            }

            public void Release(IResourceLocation location, object asset)
            {
                ReleaseCount++;
            }

            public void Fail(Exception failure)
            {
                if (!hasPendingProvide)
                {
                    throw new InvalidOperationException("There is no pending provide operation.");
                }

                hasPendingProvide = false;
                pendingProvideHandle.Complete(asset, false, failure);
            }
        }

        private sealed class DelayedNullAssetProvider : IResourceProvider
        {
            private readonly string providerId = $"Tests.DelayedNullAssetProvider.{Guid.NewGuid():N}";
            private ProvideHandle pendingProvideHandle;
            private bool hasPendingProvide;

            public string ProviderId => providerId;
            public ProviderBehaviourFlags BehaviourFlags => ProviderBehaviourFlags.None;
            public int ProvideCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public Type GetDefaultType(IResourceLocation location)
            {
                return typeof(GameObject);
            }

            public bool CanProvide(Type type, IResourceLocation location)
            {
                return type == typeof(GameObject) && location.ProviderId == ProviderId;
            }

            public void Provide(ProvideHandle provideHandle)
            {
                ProvideCount++;
                pendingProvideHandle = provideHandle;
                hasPendingProvide = true;
            }

            public void Release(IResourceLocation location, object asset)
            {
                ReleaseCount++;
            }

            public void CompleteWithNull()
            {
                if (!hasPendingProvide)
                {
                    throw new InvalidOperationException("There is no pending provide operation.");
                }

                hasPendingProvide = false;
                // 标记加载成功但结果为 null：验证“返回无效资源”分支也有诊断日志。
                pendingProvideHandle.Complete<object>(null, true, null);
            }
        }

        // —— 日志捕获类型 ——

        private sealed class CapturedLogs : IDisposable
        {
            private readonly RecordingLogWriter writer;

            public CapturedLogs(RecordingLogWriter writer)
            {
                this.writer = writer;
            }

            public IReadOnlyList<CapturedLog> Entries
            {
                get { return writer.Entries; }
            }

            public void Dispose()
            {
                LogManager.RemoveWriter(writer);
            }
        }

        private readonly struct CapturedLog
        {
            public CapturedLog(string category, string message, int level)
            {
                Category = category;
                Message = message;
                Level = level;
            }

            public string Category { get; }
            public string Message { get; }
            public int Level { get; }
        }

        //ILogWriter 的内存记录实现：只记录条目，不产生任何输出
        private sealed class RecordingLogWriter : ILogWriter
        {
            public List<CapturedLog> Entries { get; } = new List<CapturedLog>();

            public LogLevel MinLevel { get; set; }

            public void WriteLog(LogEntry entry)
            {
                Entries.Add(new CapturedLog(entry.Category, entry.Message, (int)entry.Level));
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
