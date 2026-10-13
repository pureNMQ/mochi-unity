using System;
using System.Collections.Generic;
using System.IO;
using Mochi.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.SaveManager
{
    /// <summary>
    /// 存档管理窗口:呈现存档目录内的全部文件状态与主档的文档头、各节原始 JSON,
    /// 并提供清档与损坏注入两个动作,作为存档损坏路径的验收注入器。
    /// 只认识 <see cref="SaveDocument"/> 的通用结构,不认识任何具体业务节。
    /// </summary>
    public sealed class SaveManagerWindow : EditorWindow
    {
        private const int SectionJsonMaxLines = 12;
        private const float SectionJsonMinLines = 2f;

        private static readonly string[] SectionJsonLineSeparators = { "\r\n", "\n" };

        private Vector2 scroll;
        private readonly HashSet<string> expandedSections = new HashSet<string>();
        private string directoryPath;
        private string[] files = Array.Empty<string>();
        private SaveDocument mainDocument;
        private SaveDocument backupDocument;
        private SaveGuardResult guardVerdict;
        private string mainReadFailure;

        /// <summary>打开存档管理窗口(菜单 Mochi/Save Manager)。</summary>
        [MenuItem("Mochi/Save Manager")]
        public static void Open()
        {
            GetWindow<SaveManagerWindow>("存档管理");
        }

        /// <summary>默认存档目录:与运行时一致的 persistentDataPath/Save,不另立编辑器专用路径。</summary>
        public static string DefaultDirectoryPath =>
            Path.Combine(Application.persistentDataPath, SaveFileStore.SaveDirectoryName);

        private void OnEnable()
        {
            minSize = new Vector2(560f, 420f);
            TryRefresh();
        }

        private void OnGUI()
        {
            DrawToolbar();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawDirectory();
            DrawGuardVerdict();
            DrawMainDocument();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    TryRefresh();
                }

                if (GUILayout.Button("打开存档目录", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                {
                    OpenSaveDirectory();
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawDirectory()
        {
            EditorGUILayout.LabelField("存档目录", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                directoryPath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            SaveFileStore store = CreateStore();
            EditorGUILayout.LabelField("目录内文件总数", files.Length.ToString());
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("主档", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(SaveFileStore.MainFileName, DescribeFile(store.MainFilePath));
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("备份档", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(SaveFileStore.BackupFileName, DescribeFile(store.BackupFilePath));
            EditorGUILayout.Space();

            DrawQuarantineFiles();
            DrawOtherFiles();
            EditorGUILayout.Space();
            DrawActions(store);
            EditorGUILayout.Space();
        }

        private void DrawQuarantineFiles()
        {
            string[] quarantined = FindQuarantinedFiles();
            EditorGUILayout.LabelField($"隔离文件({quarantined.Length} 个)", EditorStyles.boldLabel);
            if (quarantined.Length == 0)
            {
                EditorGUILayout.LabelField("不存在");
                EditorGUILayout.Space();
                return;
            }

            EditorGUILayout.LabelField("清档不会删除隔离文件,它们是版本不符时留存的迁移材料。");
            foreach (string path in quarantined)
            {
                EditorGUILayout.LabelField(Path.GetFileName(path), DescribeFile(path));
            }

            EditorGUILayout.Space();
        }

        /// <summary>把不属于主档、备份与隔离件的文件也列出来,目录清单才可能完整。</summary>
        private void DrawOtherFiles()
        {
            var others = new List<string>();
            foreach (string path in files)
            {
                string fileName = Path.GetFileName(path);
                if (fileName == SaveFileStore.MainFileName
                    || fileName == SaveFileStore.BackupFileName
                    || IsQuarantined(fileName))
                {
                    continue;
                }
                others.Add(path);
            }

            if (others.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField($"其他文件({others.Count} 个)", EditorStyles.boldLabel);
            foreach (string path in others)
            {
                EditorGUILayout.LabelField(Path.GetFileName(path), DescribeFile(path));
            }
            EditorGUILayout.Space();
        }

        private void DrawActions(SaveFileStore store)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("清档", GUILayout.Width(120f)))
                {
                    ConfirmClearSave(store);
                }

                if (GUILayout.Button("损坏注入", GUILayout.Width(120f)))
                {
                    ConfirmInjectCorruption(store);
                }
            }

            EditorGUILayout.LabelField("清档只删主档与备份;损坏注入只把主档内容换成不可解析片段。");
        }

        private void DrawGuardVerdict()
        {
            EditorGUILayout.LabelField("可读性判定", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(DescribeGuardResult(guardVerdict));
            EditorGUILayout.LabelField("CanUse", guardVerdict.CanUse.ToString());
            EditorGUILayout.LabelField("RecoveredFromBackup", guardVerdict.RecoveredFromBackup.ToString());

            if (!string.IsNullOrEmpty(guardVerdict.Detail))
            {
                EditorGUILayout.LabelField("判定细节", guardVerdict.Detail);
            }

            EditorGUILayout.Space();
        }

        private void DrawMainDocument()
        {
            EditorGUILayout.LabelField("主档内容", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("当前代码支持的世代号", SaveCabinet.CurrentVersion.ToString());

            // 主档可解析不等于被采用:残档能解析成合法对象,却被版本保护拦下。只把头部数值摆出来,
            // 走查者会以为这就是当前生效的档
            EditorGUILayout.LabelField(DescribeMainDocument());

            if (mainDocument == null)
            {
                DrawBackupDocument();
                EditorGUILayout.Space();
                return;
            }

            EditorGUILayout.LabelField("世代号 Version", mainDocument.Version.ToString());
            EditorGUILayout.LabelField("写档时刻 SavedAt", NullToPlaceholder(mainDocument.SavedAt));
            EditorGUILayout.LabelField("构建版本 BuildVersion", NullToPlaceholder(mainDocument.BuildVersion));
            EditorGUILayout.LabelField("节数", mainDocument.Sections == null ? "0" : mainDocument.Sections.Count.ToString());

            if (mainDocument.Sections != null)
            {
                foreach (KeyValuePair<string, JToken> section in mainDocument.Sections)
                {
                    DrawSection(section.Key, section.Value);
                }
            }

            DrawBackupDocument();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 说明主档自身的处境。这里只陈述窗口直接观察到的事实(文件在不在、解析得出来解析不出来),
        /// 不重复判定可用性:可用与否以 <see cref="guardVerdict"/> 为唯一权威,否则两处判定迟早给出相反结论。
        /// </summary>
        private string DescribeMainDocument()
        {
            if (mainDocument == null)
            {
                return string.IsNullOrEmpty(mainReadFailure)
                    ? "主档不可读:没有主档文件"
                    : $"主档不可读:{mainReadFailure}";
            }

            // CanUse 只说"有一份可用档",不说是哪一份:回退备份时主档正是不被采用的那一份
            return guardVerdict.CanUse && !guardVerdict.RecoveredFromBackup
                ? "主档可解析且可用"
                : "主档可解析但未被采用(原因见可读性判定)";
        }

        /// <summary>主档读不出来时把备份的文档头与节也摆出来,否则窗口只剩一句"不可读",排障无从下手。</summary>
        private void DrawBackupDocument()
        {
            if (mainDocument != null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("备份档内容", EditorStyles.boldLabel);
            if (backupDocument == null)
            {
                EditorGUILayout.LabelField("备份档不可读:没有备份档或内容无法解析");
                return;
            }

            EditorGUILayout.LabelField("世代号 Version", backupDocument.Version.ToString());
            EditorGUILayout.LabelField("写档时刻 SavedAt", NullToPlaceholder(backupDocument.SavedAt));
            EditorGUILayout.LabelField("构建版本 BuildVersion", NullToPlaceholder(backupDocument.BuildVersion));
            EditorGUILayout.LabelField("节数", backupDocument.Sections == null ? "0" : backupDocument.Sections.Count.ToString());

            if (backupDocument.Sections == null)
            {
                return;
            }

            foreach (KeyValuePair<string, JToken> section in backupDocument.Sections)
            {
                DrawSection(section.Key, section.Value);
            }
        }

        private void DrawSection(string sectionId, JToken value)
        {
            string json = FormatSectionJson(value);
            bool expanded = expandedSections.Contains(sectionId);
            bool next = EditorGUILayout.Foldout(expanded, $"节 {sectionId}");
            if (next != expanded)
            {
                if (next)
                {
                    expandedSections.Add(sectionId);
                }
                else
                {
                    expandedSections.Remove(sectionId);
                }
            }

            if (!next)
            {
                return;
            }

            float height = EditorGUIUtility.singleLineHeight
                * Mathf.Clamp(CountLines(json), SectionJsonMinLines, SectionJsonMaxLines);
            EditorGUILayout.TextArea(json, GUILayout.Height(height));
            if (GUILayout.Button("复制本节 JSON", GUILayout.Width(140f)))
            {
                GUIUtility.systemCopyBuffer = json;
            }
        }

        private void ConfirmClearSave(SaveFileStore store)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "清档",
                "将删除主档与备份档。隔离文件(版本迁移材料)保留,不会被清档删除。",
                "删除",
                "取消");
            if (!confirmed)
            {
                return;
            }

            store.DeleteAll();
            TryRefresh();
        }

        private void ConfirmInjectCorruption(SaveFileStore store)
        {
            if (!store.Exists)
            {
                EditorUtility.DisplayDialog("损坏注入", "主档不存在,注入不会凭空造出档。", "确定");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "损坏注入",
                "将把主档内容换成不可解析片段。备份档与隔离文件不受影响。",
                "注入",
                "取消");
            if (!confirmed)
            {
                return;
            }

            store.InjectCorruption();
            TryRefresh();
        }

        private void Refresh()
        {
            directoryPath = DefaultDirectoryPath;
            var store = new SaveFileStore(directoryPath);
            files = Directory.Exists(directoryPath) ? ListFiles(directoryPath) : Array.Empty<string>();

            mainDocument = store.Read();
            backupDocument = store.ReadBackup();
            guardVerdict = new SaveVersionGuard(store, SaveCabinet.CurrentVersion).Inspect();

            // SaveFileStore.Read 只给出 null,区分"文件不存在"与"内容读不出来"才能对着窗口判断该查哪一侧
            if (mainDocument == null)
            {
                mainReadFailure = store.Exists ? "文件存在但内容无法解析" : "文件不存在";
            }
            else
            {
                mainReadFailure = null;
            }

            Repaint();
        }

        /// <summary>目录读不出来时窗口仍要能打开,排障入口不能因为文件系统问题而消失。</summary>
        private void TryRefresh()
        {
            try
            {
                Refresh();
            }
            catch (Exception exception)
            {
                files = Array.Empty<string>();
                mainDocument = null;
                backupDocument = null;
                guardVerdict = default;
                mainReadFailure = $"读取存档目录失败:{exception.Message}";
                Repaint();
            }
        }

        private static string[] ListFiles(string directoryPath)
        {
            try
            {
                return Directory.GetFiles(directoryPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private void OpenSaveDirectory()
        {
            if (!Directory.Exists(directoryPath))
            {
                EditorUtility.DisplayDialog("打开存档目录", $"目录尚不存在:{directoryPath}", "确定");
                return;
            }

            EditorUtility.RevealInFinder(directoryPath);
        }

        private SaveFileStore CreateStore()
        {
            return new SaveFileStore(directoryPath);
        }

        private string[] FindQuarantinedFiles()
        {
            var quarantined = new List<string>();
            foreach (string path in files)
            {
                if (IsQuarantined(Path.GetFileName(path)))
                {
                    quarantined.Add(path);
                }
            }
            return quarantined.ToArray();
        }

        private static bool IsQuarantined(string fileName)
        {
            return fileName.IndexOf(SaveFileStore.QuarantineInfix, StringComparison.Ordinal) >= 0;
        }

        private static string DescribeFile(string path)
        {
            if (!File.Exists(path))
            {
                return "不存在";
            }

            long bytes = new FileInfo(path).Length;
            return $"存在,{bytes} 字节({FormatSize(bytes)})";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:F1} KB";
            }
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        /// <summary>
        /// 把判定翻译成结论文案。只按 <see cref="SaveGuardReason"/> 推文案会把 AC7 的可恢复态说成不可用:
        /// 主档不可读而备份顶得上时 Reason 同样是 <see cref="SaveGuardReason.Corrupted"/>,故必须先看
        /// <see cref="SaveGuardResult.CanUse"/> 与 <see cref="SaveGuardResult.RecoveredFromBackup"/> 定分支,
        /// 再用 Reason 细分不可用的原因。
        /// </summary>
        /// <param name="result">判定结果,可用性与回退位都取自它。</param>
        private static string DescribeGuardResult(SaveGuardResult result)
        {
            if (result.CanUse)
            {
                return result.RecoveredFromBackup
                    ? "主档不可读,可回退备份(下方已列出备份档内容)"
                    : "主档可读";
            }

            switch (result.Reason)
            {
                case SaveGuardReason.VersionTooNew:
                    return "档世代号超出当前支持范围,继续入口置灰";
                case SaveGuardReason.Corrupted:
                    // 只说结果。Corrupted 覆盖两种处境:两份都在但都不可用,以及只有主档、连 .bak 都没有;
                    // 说成"备份也读不出来"会在后一种处境断言一个不存在的文件,与 AC7 那条文案缺陷同源
                    return "存档不可用:主档与备份都没有可用档";
                case SaveGuardReason.NoSave:
                    return "无档";
                default:
                    return result.Reason.ToString();
            }
        }

        private static string FormatSectionJson(JToken value)
        {
            if (value == null)
            {
                return "<节内容为 JSON null>";
            }

            try
            {
                return value.ToString(Formatting.Indented);
            }
            catch (Exception exception)
            {
                return $"<节内容无法格式化:{exception.Message}>";
            }
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            return text.Split(SectionJsonLineSeparators, StringSplitOptions.None).Length;
        }

        private static string NullToPlaceholder(string value)
        {
            return string.IsNullOrEmpty(value) ? "<空>" : value;
        }
    }
}
