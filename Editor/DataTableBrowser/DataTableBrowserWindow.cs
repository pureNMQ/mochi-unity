using System;
using System.Collections.Generic;
using System.Linq;
using Mochi.DataTable;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 通用数据表浏览器:列出表目录声明的全部表,提供条目新增、编辑、删除与保存,
    /// 并在每次修改后对当前表即时执行编辑期校验(结构、跨表引用、领域规则)。
    /// 编辑中的未保存修改即时进入快照,参与其他表的引用解析。
    /// </summary>
    public sealed class DataTableBrowserWindow : EditorWindow
    {
        private sealed class TableWorkspace
        {
            public ITableBrowserSource Source;
            public JArray Rows = new JArray();
            public bool Dirty;
            public string LoadError;
            public readonly List<TableEditError> Errors = new List<TableEditError>();
        }

        private readonly List<TableWorkspace> workspaces = new List<TableWorkspace>();
        private TableKeySetResolver keyResolver = new TableKeySetResolver();
        private int selectedTable = -1;
        private int selectedEntry = -1;
        private Vector2 tableListScroll;
        private Vector2 entryListScroll;
        private Vector2 entryEditorScroll;

        [MenuItem("Mochi/Data Table Browser")]
        public static void Open()
        {
            GetWindow<DataTableBrowserWindow>("Data Table Browser");
        }

        private void OnEnable()
        {
            ReloadAll();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("从磁盘重新载入", EditorStyles.toolbarButton))
            {
                ReloadFromDiskWithConfirm();
            }
            GUILayout.FlexibleSpace();
            TableWorkspace current = Current;
            using (new EditorGUI.DisabledScope(current == null || current.LoadError != null))
            {
                if (GUILayout.Button("保存当前表", EditorStyles.toolbarButton))
                {
                    SaveCurrent();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (workspaces.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有发现任何表目录。请实现 ITableBrowserCatalog 并声明表源。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawTableList();
            DrawTablePanel();
            EditorGUILayout.EndHorizontal();
        }

        private TableWorkspace Current =>
            selectedTable >= 0 && selectedTable < workspaces.Count ? workspaces[selectedTable] : null;

        private void ReloadFromDiskWithConfirm()
        {
            if (workspaces.Any(workspace => workspace.Dirty) &&
                !EditorUtility.DisplayDialog(
                    "重新载入",
                    "存在未保存的修改,重新载入将丢弃这些修改。继续?",
                    "重新载入",
                    "取消"))
            {
                return;
            }
            ReloadAll();
        }

        private void ReloadAll()
        {
            workspaces.Clear();
            foreach (Type catalogType in TypeCache.GetTypesDerivedFrom<ITableBrowserCatalog>())
            {
                if (catalogType.IsAbstract)
                {
                    continue;
                }

                ITableBrowserCatalog catalog;
                try
                {
                    catalog = (ITableBrowserCatalog)Activator.CreateInstance(catalogType);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"表目录 {catalogType.Name} 实例化失败: {ex.Message}");
                    continue;
                }

                foreach (ITableBrowserSource source in catalog.CreateSources())
                {
                    var workspace = new TableWorkspace { Source = source };
                    try
                    {
                        workspace.Rows = source.LoadRows();
                    }
                    catch (Exception ex)
                    {
                        workspace.LoadError = ex.Message;
                    }
                    workspaces.Add(workspace);
                }
            }

            keyResolver = new TableKeySetResolver();
            selectedTable = workspaces.Count > 0 ? 0 : -1;
            selectedEntry = -1;
            ValidateAll();
        }

        private void SaveCurrent()
        {
            TableWorkspace workspace = Current;
            if (workspace == null || workspace.LoadError != null)
            {
                return;
            }
            workspace.Source.Save(workspace.Rows);
            AssetDatabase.Refresh();
            workspace.Dirty = false;
            ValidateAll();
        }

        private void ValidateAll()
        {
            RebuildKeyResolver();
            foreach (TableWorkspace workspace in workspaces)
            {
                workspace.Errors.Clear();
                if (workspace.LoadError == null)
                {
                    workspace.Errors.AddRange(workspace.Source.Validate(workspace.Rows, keyResolver));
                }
            }
        }

        private void RebuildKeyResolver()
        {
            //编辑中的未保存修改必须即时参与他表引用解析,因此每次校验前从全部工作区重建键集合
            keyResolver = new TableKeySetResolver();
            foreach (TableWorkspace workspace in workspaces)
            {
                foreach (JToken row in workspace.Rows)
                {
                    if (row.Type != JTokenType.Object)
                    {
                        continue;
                    }
                    string key = workspace.Source.ExtractKey((JObject)row);
                    if (!string.IsNullOrEmpty(key))
                    {
                        keyResolver.AddKey(workspace.Source.TableName, key);
                    }
                }
            }
        }

        private void DrawTableList()
        {
            tableListScroll = EditorGUILayout.BeginScrollView(tableListScroll, GUILayout.Width(230));
            for (int index = 0; index < workspaces.Count; index++)
            {
                TableWorkspace workspace = workspaces[index];
                string label = (workspace.Dirty ? "* " : "") + workspace.Source.DisplayName;
                if (workspace.LoadError != null || workspace.Errors.Count > 0)
                {
                    label += " (!)";
                }
                if (GUILayout.Button(label, index == selectedTable
                    ? EditorStyles.whiteBoldLabel
                    : EditorStyles.label))
                {
                    selectedTable = index;
                    selectedEntry = -1;
                    ValidateCurrent();
                    UnityEngine.GUI.FocusControl(null);
                    Repaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTablePanel()
        {
            TableWorkspace workspace = Current;
            if (workspace == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(
                $"{workspace.Source.DisplayName}  ({workspace.Source.StorageKind})  条目 {workspace.Rows.Count}" +
                (workspace.Dirty ? "  未保存" : ""),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(workspace.Source.Path, EditorStyles.miniLabel);

            if (workspace.LoadError != null)
            {
                EditorGUILayout.HelpBox("表文件读取失败: " + workspace.LoadError, MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新增条目"))
            {
                AddEntry(workspace);
            }
            using (new EditorGUI.DisabledScope(selectedEntry < 0 || selectedEntry >= workspace.Rows.Count))
            {
                if (GUILayout.Button("删除选中条目"))
                {
                    DeleteEntry(workspace);
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawEntryList(workspace);

            if (selectedEntry >= 0 && selectedEntry < workspace.Rows.Count)
            {
                entryEditorScroll = EditorGUILayout.BeginScrollView(entryEditorScroll);
                DrawEntryEditor(workspace, (JObject)workspace.Rows[selectedEntry]);
                EditorGUILayout.EndScrollView();
            }

            if (workspace.Errors.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"校验错误 {workspace.Errors.Count} 条(修改后即时刷新,不阻断保存):",
                    MessageType.Error);
                foreach (TableEditError error in workspace.Errors)
                {
                    EditorGUILayout.LabelField("  " + error);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEntryList(TableWorkspace workspace)
        {
            entryListScroll = EditorGUILayout.BeginScrollView(
                entryListScroll, GUILayout.Height(Mathf.Min(160, 24 * workspace.Rows.Count + 8)));
            for (int index = 0; index < workspace.Rows.Count; index++)
            {
                JObject row = (JObject)workspace.Rows[index];
                string key = workspace.Source.ExtractKey(row) ?? "(缺少键)";
                string summary = BuildEntrySummary(row, workspace.Source.KeyFieldName);
                string label = index == selectedEntry
                    ? $"> {key}  {summary}"
                    : $"  {key}  {summary}";
                if (GUILayout.Button(label, index == selectedEntry
                    ? EditorStyles.whiteBoldLabel
                    : EditorStyles.label))
                {
                    selectedEntry = index;
                    UnityEngine.GUI.FocusControl(null);
                    Repaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntryEditor(TableWorkspace workspace, JObject row)
        {
            EditorGUILayout.Space();
            foreach (JProperty property in row.Properties().ToList())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(property.Name);
                DrawFieldEditor(workspace, row, property);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFieldEditor(TableWorkspace workspace, JObject row, JProperty property)
        {
            switch (property.Value.Type)
            {
                case JTokenType.String:
                    EditorGUI.BeginChangeCheck();
                    string text = EditorGUILayout.TextField((string)property.Value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.Value = text;
                        MarkEdited(workspace);
                    }
                    break;
                case JTokenType.Boolean:
                    EditorGUI.BeginChangeCheck();
                    bool flag = EditorGUILayout.Toggle((bool)property.Value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.Value = flag;
                        MarkEdited(workspace);
                    }
                    break;
                case JTokenType.Integer:
                    EditorGUI.BeginChangeCheck();
                    long integer = EditorGUILayout.LongField((long)property.Value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.Value = integer;
                        MarkEdited(workspace);
                    }
                    break;
                case JTokenType.Float:
                    EditorGUI.BeginChangeCheck();
                    double real = EditorGUILayout.DoubleField((double)property.Value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.Value = real;
                        MarkEdited(workspace);
                    }
                    break;
                case JTokenType.Array:
                    DrawStringArrayEditor(workspace, property);
                    break;
                default:
                    EditorGUILayout.LabelField(
                        property.Value.ToString(Newtonsoft.Json.Formatting.Indented),
                        EditorStyles.wordWrappedMiniLabel);
                    break;
            }
        }

        private void DrawStringArrayEditor(TableWorkspace workspace, JProperty property)
        {
            var array = (JArray)property.Value;
            if (array.All(element => element.Type == JTokenType.String))
            {
                EditorGUILayout.BeginVertical();
                for (int index = 0; index < array.Count; index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    string element = EditorGUILayout.TextField((string)array[index]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        array[index] = element;
                        MarkEdited(workspace);
                    }
                    if (GUILayout.Button("移除", GUILayout.Width(44)))
                    {
                        array.RemoveAt(index);
                        MarkEdited(workspace);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("添加元素"))
                {
                    array.Add("");
                    MarkEdited(workspace);
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.LabelField(
                    property.Value.ToString(Newtonsoft.Json.Formatting.Indented),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void AddEntry(TableWorkspace workspace)
        {
            JObject template = workspace.Source.CreateEntryTemplate();
            template[workspace.Source.KeyFieldName] = GenerateUniqueKey(workspace);
            workspace.Rows.Add(template);
            selectedEntry = workspace.Rows.Count - 1;
            MarkEdited(workspace);
        }

        private void DeleteEntry(TableWorkspace workspace)
        {
            if (selectedEntry < 0 || selectedEntry >= workspace.Rows.Count)
            {
                return;
            }
            workspace.Rows.RemoveAt(selectedEntry);
            selectedEntry = Mathf.Min(selectedEntry, workspace.Rows.Count - 1);
            MarkEdited(workspace);
        }

        private void MarkEdited(TableWorkspace workspace)
        {
            workspace.Dirty = true;
            ValidateAll();
        }

        private void ValidateCurrent()
        {
            TableWorkspace workspace = Current;
            if (workspace == null)
            {
                return;
            }
            workspace.Errors.Clear();
            if (workspace.LoadError == null)
            {
                workspace.Errors.AddRange(workspace.Source.Validate(workspace.Rows, keyResolver));
            }
        }

        private string GenerateUniqueKey(TableWorkspace workspace)
        {
            string baseKey = "entry";
            int suffix = 1;
            string candidate = baseKey;
            var keys = new HashSet<string>(
                workspace.Rows.Select(row => workspace.Source.ExtractKey((JObject)row)));
            while (keys.Contains(candidate))
            {
                suffix++;
                candidate = baseKey + "_" + suffix;
            }
            return candidate;
        }

        private static string BuildEntrySummary(JObject row, string keyFieldName)
        {
            foreach (JProperty property in row.Properties())
            {
                if (property.Name == keyFieldName || property.Value.Type != JTokenType.String)
                {
                    continue;
                }
                string value = (string)property.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return "";
        }
    }
}
