using System;
using System.IO;
using Mochi.DataTable;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 表头失配检查的双钩子:资产导入钩子检查本次变化的 .json,脚本编译完成钩子全量扫描
    /// Assets 下全部数据表文件。表头形状合法但 rowType 不再指向任何已编译 TableRow 派生类型时
    /// 以 Console 错误指出文件与失配类型,修复入口是浏览器的重选行类型。
    /// </summary>
    [InitializeOnLoad]
    internal static class TableHeaderMismatchChecker
    {
        private const string PendingScanKey = "Mochi.DataTableBrowser.PendingHeaderScan";
        private const string StartupScanDoneKey = "Mochi.DataTableBrowser.StartupHeaderScanDone";

        static TableHeaderMismatchChecker()
        {
            CompilationPipeline.assemblyCompilationFinished +=
                (_, __) => SessionState.SetBool(PendingScanKey, true);
            //编译后的域重载会重跑本静态构造,此时 TypeCache 已可用,统一推迟到空闲帧扫描
            EditorApplication.delayCall += RunDueScans;
        }

        private static void RunDueScans()
        {
            bool pending = SessionState.GetBool(PendingScanKey, false);
            bool startupDone = SessionState.GetBool(StartupScanDoneKey, false);
            if (pending)
            {
                SessionState.SetBool(PendingScanKey, false);
            }
            if (pending || !startupDone)
            {
                SessionState.SetBool(StartupScanDoneKey, true);
                ScanAllTableFiles();
            }
        }

        private static void ScanAllTableFiles()
        {
            string assetsRoot = TableBrowserSource<TableRow>.ToAbsolutePath("Assets");
            if (!Directory.Exists(assetsRoot))
            {
                return;
            }
            string projectRoot = Directory.GetParent(assetsRoot).FullName;
            foreach (string file in Directory.GetFiles(assetsRoot, "*.json", SearchOption.AllDirectories))
            {
                string displayPath = file.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(projectRoot.Length + 1).Replace('\\', '/')
                    : file;
                CheckFile(file, displayPath);
            }
        }

        /// <summary>检查一个 json 文件:表头形状合法但行类型失配时报错;非表文件与损坏文件不属本检查范围。</summary>
        private static void CheckFile(string absolutePath, string displayPath)
        {
            JObject root = TryReadTableObject(absolutePath);
            if (root == null ||
                !TableHeader.TryParse(root, out TableHeader header, out _))
            {
                return;
            }
            if (TableBrowserSourceFactory.FindRowType(header.RowType) != null)
            {
                return;
            }
            Debug.LogError(
                "[Mochi] 数据表 " + displayPath + " 的表头行类型 " + header.RowType +
                " 失配: 不是已编译的 TableRow 派生类型(可能已被重命名或删除),文件当前不可用。" +
                "在数据表浏览器中打开该文件并重选行类型可修复。");
        }

        private static JObject TryReadTableObject(string absolutePath)
        {
            try
            {
                return JToken.Parse(File.ReadAllText(absolutePath)) as JObject;
            }
            catch
            {
                //json 语法错误在选中打开时另行警告,这里静默跳过
                return null;
            }
        }

        /// <summary>资产导入钩子:只检查本次导入或移动的 .json,全量扫描归编译与启动钩子。</summary>
        private sealed class ImportHook : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                foreach (string assetPath in importedAssets)
                {
                    CheckAssetPath(assetPath);
                }
                foreach (string assetPath in movedAssets)
                {
                    CheckAssetPath(assetPath);
                }
            }
        }

        private static void CheckAssetPath(string assetPath)
        {
            if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            CheckFile(TableBrowserSource<TableRow>.ToAbsolutePath(assetPath), assetPath);
        }
    }
}
