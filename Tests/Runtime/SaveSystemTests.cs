using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Mochi.Save;
using Mochi.Unity.Saving;
using NUnit.Framework;
using UnityEngine;

namespace Mochi.Unity.GUI.Tests
{
    public class SaveSystemTests
    {
        private const string RoundTripSectionId = "roundtrip.value";
        private const int RoundTripValue = 42;

        private static readonly string[] RetiredTypeNames = { "SaveStorage", "SaveManifest", "SaveItem" };

        private static readonly string[] RetiredTypeGuids =
        {
            "d226b0ce1d0c10943969c47fc6a2894c",
            "e51e4843ee934994e9dcb28aafe67f35",
            "2f822ba40b0c69b498aed615c99f7875"
        };

        // 支撑性测试，不锚定 AC：固化 SaveSystem 的路径适配契约（根为 persistentDataPath、末级为 Save、构造即建目录）。
        [Test]
        public void SaveSystem_Constructor_CreatesSaveDirectoryUnderPersistentDataPath()
        {
            SaveSystem saveSystem = new SaveSystem();

            string expectedDirectoryPath = Path.Combine(
                Application.persistentDataPath,
                SaveFileStore.SaveDirectoryName);
            Assert.That(saveSystem.Store, Is.Not.Null);
            Assert.That(saveSystem.Store.DirectoryPath, Is.EqualTo(expectedDirectoryPath));
            Assert.That(
                saveSystem.Store.DirectoryPath,
                Does.EndWith(Path.DirectorySeparatorChar + SaveFileStore.SaveDirectoryName));
            Assert.That(Directory.Exists(expectedDirectoryPath), Is.True);
        }

        // 支撑性测试，不锚定 AC：适配层回归。SaveSystem.Store 固定在 persistentDataPath，写读往返改用 core 的
        // SaveFileStore 指向临时目录，避免测试污染真实存档。
        [Test]
        public void SaveFileStore_TempDirectory_RoundTripsRegisteredSection()
        {
            string directoryPath = Path.Combine(
                Path.GetTempPath(),
                "MochiUnitySaveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);

            try
            {
                SaveCabinet cabinet = new SaveCabinet();
                RoundTripSection section = new RoundTripSection();
                cabinet.Register(section);
                SaveFileStore store = new SaveFileStore(directoryPath);

                store.WriteAtomic(cabinet.CaptureAll());
                Assert.That(store.Exists, Is.True);

                cabinet.RestoreAll(store.Read());

                Assert.That(section.LastRestored, Is.EqualTo(RoundTripValue));
            }
            finally
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, true);
                }
            }
        }

        // 验收条件 AC13:当本需求完成，则旧 SaveStorage/SaveManifest/SaveItem 三文件（含 .meta）从工程删除且全工程无引用残留。
        [Test]
        public void RetiredSaveTypes_AreDeletedAndUnreferenced()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty, "无法从 Application.dataPath 定位工程根目录。");

            string saveSystemDirectory = Path.Combine(projectRoot, "Packages", "MochiUnity", "SaveSystem");
            Assert.That(
                Directory.Exists(saveSystemDirectory),
                Is.True,
                $"存档实现目录不存在，无法判定退役结果：{saveSystemDirectory}");

            foreach (string retiredTypeName in RetiredTypeNames)
            {
                Assert.That(
                    File.Exists(Path.Combine(saveSystemDirectory, retiredTypeName + ".cs")),
                    Is.False,
                    $"AC13 要求 {retiredTypeName}.cs 已删除。");
                Assert.That(
                    File.Exists(Path.Combine(saveSystemDirectory, retiredTypeName + ".cs.meta")),
                    Is.False,
                    $"AC13 要求 {retiredTypeName}.cs.meta 已删除。");
            }

            // 本测试文件以字符串和注释保留被扫描的名字，自身不是引用，扫描时排除它才能覆盖其余全部源码。
            string thisSourcePath = GetThisSourcePath();
            Regex retiredTypeReference = new Regex(@"\b(" + string.Join("|", RetiredTypeNames) + @")\b");
            List<string> sourceOffenders = new List<string>();
            List<string> assetOffenders = new List<string>();

            foreach (string scanRoot in GetScanRoots(projectRoot))
            {
                foreach (string filePath in Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories))
                {
                    if (IsSamePath(filePath, thisSourcePath) || IsBuildOutputPath(filePath))
                    {
                        continue;
                    }

                    string extension = Path.GetExtension(filePath);
                    if (extension == ".cs")
                    {
                        if (retiredTypeReference.IsMatch(File.ReadAllText(filePath)))
                        {
                            sourceOffenders.Add(filePath);
                        }
                    }
                    else if (extension == ".meta" || extension == ".asset" || extension == ".prefab" || extension == ".unity")
                    {
                        string content = File.ReadAllText(filePath);
                        foreach (string retiredTypeGuid in RetiredTypeGuids)
                        {
                            if (content.Contains(retiredTypeGuid))
                            {
                                assetOffenders.Add(filePath);
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(
                sourceOffenders,
                Is.Empty,
                "AC13 要求退役类型无源码引用残留，命中：" + string.Join(", ", sourceOffenders));
            Assert.That(
                assetOffenders,
                Is.Empty,
                "AC13 要求退役类型无资产引用残留，命中：" + string.Join(", ", assetOffenders));
        }

        private static IEnumerable<string> GetScanRoots(string projectRoot)
        {
            string packagesDirectory = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packagesDirectory))
            {
                foreach (string packageDirectory in Directory.EnumerateDirectories(packagesDirectory))
                {
                    if (Path.GetFileName(packageDirectory).StartsWith("Mochi", StringComparison.Ordinal))
                    {
                        yield return packageDirectory;
                    }
                }
            }

            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assetsDirectory))
            {
                yield return assetsDirectory;
            }
        }

        private static bool IsBuildOutputPath(string filePath)
        {
            return filePath.Contains(@"\obj\") || filePath.Contains(@"\bin\");
        }

        private static bool IsSamePath(string filePath, string otherFilePath)
        {
            if (string.IsNullOrEmpty(otherFilePath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(otherFilePath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetThisSourcePath([CallerFilePath] string sourcePath = "")
        {
            return sourcePath;
        }

        private sealed class RoundTripSection : ISaveSection<int>
        {
            public string SectionId => RoundTripSectionId;

            public int LastRestored { get; private set; } = -1;

            public int Capture()
            {
                return RoundTripValue;
            }

            public void Restore(int data)
            {
                LastRestored = data;
            }
        }
    }
}
