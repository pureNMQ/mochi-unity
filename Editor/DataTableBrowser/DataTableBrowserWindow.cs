using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mochi.DataTable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 通用数据表浏览器:打开 Project 窗口当前选中的、带合法 "$table" 表头的表文件,
    /// 以类 Excel 网格呈现,列=行类型全部字段、行=条目、单元格直接编辑,支持列头排序、搜索过滤与撤销重做。
    /// 表名与行类型按文件表头识别,无任何代码侧目录;磁盘读写、演化整形状与编辑期校验全部经
    /// <see cref="ITableBrowserSource"/> 完成。表头行类型失配的文件以横幅呈现,可搜索重选类型修复。
    /// </summary>
    public sealed class DataTableBrowserWindow : EditorWindow
    {
        private const float RowHeight = 22f;
        private const float RowNumberColumnWidth = 46f;
        private const int UndoCapacity = 100;

        private static readonly Color AlternatingRowColor = new Color(0f, 0f, 0f, 0.05f);
        private static readonly Color SelectedRowColor = new Color(0.3f, 0.5f, 0.85f, 0.2f);
        private static readonly Color HeaderBackgroundColor = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color ErrorCellColor = new Color(0.8f, 0.15f, 0.15f, 0.35f);

        private static GUIStyle rowNumberStyle;

        private sealed class GridView
        {
            public string Search = "";
            public int SortColumn = -1;
            public bool SortDescending;
            public Vector2 Scroll;
            public List<string> Columns = new List<string>();
            public float[] ColumnWidths = Array.Empty<float>();
            public int SelectedRow = -1;
            public string PendingFocusCell;
            public readonly List<JArray> UndoEntries = new List<JArray>();
            public readonly List<JArray> RedoEntries = new List<JArray>();
        }

        private sealed class TableWorkspace
        {
            /// <summary>可编辑表源;null 表示表头失配态,只有 <see cref="Header"/> 与 <see cref="Path"/> 可用。</summary>
            public ITableBrowserSource Source;

            /// <summary>打开时从文件解析的表头;失配态下驱动横幅与重选。</summary>
            public TableHeader Header;

            /// <summary>表文件的项目相对路径;失配态没有 Source,单独保存。</summary>
            public string Path;

            public JArray Rows = new JArray();
            public bool Dirty;
            public string LoadError;
            public readonly List<TableBrowserError> Errors = new List<TableBrowserError>();
            public readonly Dictionary<string, string[]> EnumFields = new Dictionary<string, string[]>();
            public readonly GridView View = new GridView();
        }

        private readonly List<TableWorkspace> workspaces = new List<TableWorkspace>();
        private int displayedTable = -1;
        private Vector2 errorListScroll;

        //单元格撤销以「焦点会话」为粒度:控件获得焦点后首次值变化压入一次快照,
        //同一焦点期内的后续击键不再重复压栈;焦点移出即结束会话
        private string focusedCellControl = "";
        private bool undoPushedForFocusSession;

        /// <summary>单元格按期望类型渲染的种类,由字段 CLR 类型决定,与 JSON 值实际类型无关。</summary>
        private enum FieldEditKind
        {
            Text,
            Bool,
            Integer,
            Real,
            Array,
            ReadOnly
        }

        /// <summary>打开数据表浏览器窗口(菜单 Mochi/Data Table Browser)。</summary>
        [MenuItem("Mochi/Data Table Browser")]
        public static void Open()
        {
            GetWindow<DataTableBrowserWindow>("Data Table Browser");
        }

        private void OnEnable()
        {
            minSize = new Vector2(720f, 400f);
            //进/出 PlayMode 触发域重载时,OnEnable 在 EditorStyles 与 TypeCache 重建完成前执行,
            //此处按选中的表文件恢复打开会在其中抛空引用,因此统一推迟到编辑器空闲帧
            EditorApplication.delayCall += RestoreFromSelection;
        }

        private void RestoreFromSelection()
        {
            if (this == null || workspaces.Count > 0)
            {
                return;
            }
            OpenSelectionIfTable();
            Repaint();
        }

        private void OnSelectionChange()
        {
            HandleSelectionChanged();
            Repaint();
        }

        private void OnGUI()
        {
            TrackFocusSession();
            HandleUndoRedoKeys();
            DrawToolbar();

            TableWorkspace workspace = CurrentTable;
            if (workspace == null)
            {
                DrawHint();
                return;
            }

            if (workspace.Source == null)
            {
                DrawHeaderMismatch(workspace);
                return;
            }

            DrawTableHeader(workspace);

            if (workspace.LoadError != null)
            {
                EditorGUILayout.HelpBox("表文件读取失败: " + workspace.LoadError, MessageType.Error);
                if (GUILayout.Button("重新载入"))
                {
                    ReloadAll();
                }
                return;
            }

            DrawGrid(workspace);
            DrawErrorList(workspace);
        }

        private TableWorkspace CurrentTable =>
            displayedTable >= 0 && displayedTable < workspaces.Count ? workspaces[displayedTable] : null;

        private void TrackFocusSession()
        {
            string currentFocus = UnityEngine.GUI.GetNameOfFocusedControl();
            if (currentFocus == focusedCellControl)
            {
                return;
            }
            focusedCellControl = currentFocus;
            undoPushedForFocusSession = false;
        }

        private void HandleUndoRedoKeys()
        {
            //Mochi.Event 命名空间遮蔽 UnityEngine.Event,此处必须全限定
            UnityEngine.Event current = UnityEngine.Event.current;
            if (current.type != EventType.KeyDown || !(current.control || current.command))
            {
                return;
            }
            if (current.keyCode == KeyCode.Z)
            {
                UndoOne();
                current.Use();
            }
            else if (current.keyCode == KeyCode.Y)
            {
                RedoOne();
                current.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            TableWorkspace workspace = CurrentTable;
            bool editable = workspace != null && workspace.Source != null && workspace.LoadError == null;

            using (new EditorGUI.DisabledScope(!editable))
            {
                if (GUILayout.Button("新增行", EditorStyles.toolbarButton))
                {
                    AddRow(workspace);
                }
                using (new EditorGUI.DisabledScope(workspace == null ||
                    workspace.View.SelectedRow < 0 ||
                    workspace.View.SelectedRow >= workspace.Rows.Count))
                {
                    if (GUILayout.Button("删除行", EditorStyles.toolbarButton))
                    {
                        DeleteSelectedRow(workspace);
                    }
                }
                if (GUILayout.Button("保存当前表", EditorStyles.toolbarButton))
                {
                    SaveCurrentTable(workspace);
                }
                if (workspace != null)
                {
                    workspace.View.Search = EditorGUILayout.TextField(
                        workspace.View.Search, EditorStyles.toolbarTextField, GUILayout.MinWidth(160f));
                }
            }

            if (GUILayout.Button("从磁盘重新载入", EditorStyles.toolbarButton))
            {
                ReloadFromDiskWithConfirm();
            }

            GUILayout.FlexibleSpace();
            if (workspace != null)
            {
                string status;
                if (workspace.Source == null)
                {
                    status = "表头失配";
                }
                else if (workspace.LoadError != null)
                {
                    status = "读取失败";
                }
                else
                {
                    status = (workspace.Dirty ? "* 未保存" : "已保存") +
                        (workspace.Errors.Count > 0 ? "  |  校验错误 " + workspace.Errors.Count + " 条" : "");
                }
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableHeader(TableWorkspace workspace)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("表名:", EditorStyles.miniBoldLabel, GUILayout.Width(38f));
            EditorGUI.BeginChangeCheck();
            string edited = EditorGUILayout.DelayedTextField(workspace.Source.TableName);
            if (EditorGUI.EndChangeCheck())
            {
                string trimmed = edited.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    workspace.Source.RenameTable(trimmed);
                    MarkEdited(workspace);
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("行类型: " + workspace.Source.RowTypeFullName, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "条目 " + workspace.Rows.Count + (workspace.Dirty ? "  未保存" : "") + "  " + workspace.Source.Path,
                EditorStyles.miniLabel);
        }

        private void DrawHint()
        {
            EditorGUILayout.HelpBox(
                "在 Project 窗口选中一个带 $table 表头的数据表 .json 文件,即可在此打开编辑;或点击工具栏「新建表」创建新表。",
                MessageType.Info);
        }

        private void DrawHeaderMismatch(TableWorkspace workspace)
        {
            EditorGUILayout.HelpBox(
                "表头行类型失配: " + workspace.Header.RowType +
                " 不是已编译的 TableRow 派生类型,可能已被重命名或删除,文件当前不可编辑。\n" + workspace.Path,
                MessageType.Error);
            EditorGUILayout.BeginHorizontal();
            Rect buttonRect = GUILayoutUtility.GetRect(
                new GUIContent("重选行类型"), EditorStyles.miniButton, GUILayout.Width(160f));
            if (UnityEngine.GUI.Button(buttonRect, "重选行类型", EditorStyles.miniButton))
            {
                OpenRowTypePicker(buttonRect, workspace.Header.RowType,
                    selected => RewriteHeaderRowType(workspace, selected));
            }
            if (GUILayout.Button("关闭", EditorStyles.miniButton, GUILayout.Width(80f)))
            {
                workspaces.Clear();
                displayedTable = -1;
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>以可搜索下拉列出全部可用行类型;当前失配名仅作展示项置顶,不可选中。</summary>
        private void OpenRowTypePicker(Rect activator, string mismatchedRowType, Action<string> onSelected)
        {
            var options = new List<(string Value, bool Enabled)>
            {
                { (mismatchedRowType + " (当前值,不存在)", false) }
            };
            foreach (Type type in TableBrowserSourceFactory.ListRowTypes())
            {
                options.Add((type.FullName, true));
            }
            var dropdown = new TableValueDropdown(
                new AdvancedDropdownState(), "重选行类型", options, onSelected);
            dropdown.Show(new Rect(activator.x, activator.y, Mathf.Clamp(activator.width, 360f, 560f), activator.height));
        }

        private void RewriteHeaderRowType(TableWorkspace workspace, string rowTypeFullName)
        {
            string absolutePath = TableBrowserSource<TableRow>.ToAbsolutePath(workspace.Path);
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(absolutePath));
                ((JObject)root[TableHeader.FieldName])["rowType"] = rowTypeFullName;
                File.WriteAllText(absolutePath, root.ToString(Formatting.Indented));
                AssetDatabase.ImportAsset(workspace.Path);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("重选类型失败", ex.Message, "确定");
                return;
            }
            OpenTableFromPath(workspace.Path);
        }

        private void DrawGrid(TableWorkspace workspace)
        {
            ApplyPendingFocus(workspace);
            List<int> visibleRows = ComputeVisibleRows(workspace);

            workspace.View.Scroll = EditorGUILayout.BeginScrollView(workspace.View.Scroll, false, true);
            DrawHeaderRow(workspace);
            for (int viewIndex = 0; viewIndex < visibleRows.Count; viewIndex++)
            {
                DrawDataRow(workspace, visibleRows[viewIndex], viewIndex);
            }
            if (workspace.Rows.Count == 0)
            {
                EditorGUILayout.HelpBox("表为空。点击工具栏「新增行」添加条目。", MessageType.None);
            }
            else if (visibleRows.Count == 0)
            {
                EditorGUILayout.HelpBox("没有匹配搜索词的行。", MessageType.None);
            }
            EditorGUILayout.EndScrollView();
            TrackFocusedRow(workspace);
        }

        private void ApplyPendingFocus(TableWorkspace workspace)
        {
            if (string.IsNullOrEmpty(workspace.View.PendingFocusCell))
            {
                return;
            }
            UnityEngine.GUI.FocusControl(workspace.View.PendingFocusCell);
            EditorGUI.FocusTextInControl(workspace.View.PendingFocusCell);
            workspace.View.PendingFocusCell = null;
        }

        private void DrawHeaderRow(TableWorkspace workspace)
        {
            GridView view = workspace.View;
            Rect rect = GUILayoutUtility.GetRect(TotalGridWidth(view), RowHeight);
            EditorGUI.DrawRect(rect, HeaderBackgroundColor);
            UnityEngine.GUI.Label(
                new Rect(rect.x, rect.y, RowNumberColumnWidth, rect.height), "#", RowNumberStyle);
            float x = rect.x + RowNumberColumnWidth;
            for (int column = 0; column < view.Columns.Count; column++)
            {
                var headerRect = new Rect(x, rect.y, view.ColumnWidths[column], rect.height);
                string marker = view.SortColumn == column ? (view.SortDescending ? "  v" : "  ^") : "";
                if (UnityEngine.GUI.Button(headerRect, view.Columns[column] + marker, EditorStyles.miniButton))
                {
                    if (view.SortColumn == column)
                    {
                        view.SortDescending = !view.SortDescending;
                    }
                    else
                    {
                        view.SortColumn = column;
                        view.SortDescending = false;
                    }
                }
                x += view.ColumnWidths[column];
            }
        }

        private void DrawDataRow(TableWorkspace workspace, int storageIndex, int viewIndex)
        {
            GridView view = workspace.View;
            var row = (JObject)workspace.Rows[storageIndex];
            Rect rect = GUILayoutUtility.GetRect(TotalGridWidth(view), RowHeight);

            if (view.SelectedRow == storageIndex)
            {
                EditorGUI.DrawRect(rect, SelectedRowColor);
            }
            else if (viewIndex % 2 == 1)
            {
                EditorGUI.DrawRect(rect, AlternatingRowColor);
            }

            Rect numberRect = new Rect(rect.x, rect.y, RowNumberColumnWidth, rect.height);
            if (RowHasEntryLevelError(workspace, row, view))
            {
                EditorGUI.DrawRect(numberRect, ErrorCellColor);
            }
            UnityEngine.GUI.Label(numberRect, (viewIndex + 1).ToString(), RowNumberStyle);

            float x = rect.x + RowNumberColumnWidth;
            for (int column = 0; column < view.Columns.Count; column++)
            {
                var cellRect = new Rect(x, rect.y, view.ColumnWidths[column], rect.height);
                DrawCell(workspace, displayedTable, row, storageIndex, column, view.Columns[column], cellRect);
                x += view.ColumnWidths[column];
            }
        }

        private static GUIStyle RowNumberStyle =>
            rowNumberStyle ?? (rowNumberStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });

        private static float TotalGridWidth(GridView view)
        {
            return RowNumberColumnWidth + view.ColumnWidths.Sum();
        }

        private void DrawCell(
            TableWorkspace workspace,
            int tableIndex,
            JObject row,
            int storageIndex,
            int column,
            string field,
            Rect cellRect)
        {
            UnityEngine.GUI.SetNextControlName(
                CellControlName(tableIndex, column, storageIndex));
            TableRowField fieldMeta = workspace.Source.Fields[column];
            JToken value = row[field];

            List<string> cellErrors = FindCellErrors(workspace, row, field);
            if (cellErrors != null)
            {
                EditorGUI.DrawRect(cellRect, ErrorCellColor);
            }

            if (workspace.EnumFields.ContainsKey(field) && !IsListField(fieldMeta))
            {
                DrawEnumCell(workspace, row, field, value, cellRect);
            }
            else
            {
                switch (FieldEditKindOf(fieldMeta))
                {
                    case FieldEditKind.Text:
                        DrawTextCell(workspace, row, field, CellText(value), cellRect);
                        break;
                    case FieldEditKind.Bool:
                        DrawBoolCell(workspace, row, field, CellBool(value), cellRect);
                        break;
                    case FieldEditKind.Integer:
                        DrawLongCell(workspace, row, field, CellLong(value), cellRect);
                        break;
                    case FieldEditKind.Real:
                        DrawDoubleCell(workspace, row, field, CellDouble(value), cellRect);
                        break;
                    case FieldEditKind.Array:
                        if (value != null && value.Type == JTokenType.Array)
                        {
                            DrawArrayCell(workspace, storageIndex, field, (JArray)value, cellRect);
                        }
                        else
                        {
                            DrawReadOnlyCell(value ?? JValue.CreateNull(), cellRect);
                        }
                        break;
                    default:
                        DrawReadOnlyCell(value, cellRect);
                        break;
                }
            }

            if (cellErrors != null)
            {
                UnityEngine.GUI.Label(
                    cellRect, new GUIContent("", string.Join("\n", cellErrors.ToArray())));
            }
        }

        /// <summary>单元格按字段期望类型取显示值:实际 JSON 类型不符时显示该类型默认值,错误由校验标红指出。</summary>
        private static string CellText(JToken value)
        {
            return value != null && value.Type == JTokenType.String ? (string)value : "";
        }

        private static bool CellBool(JToken value)
        {
            return value != null && value.Type == JTokenType.Boolean && (bool)value;
        }

        private static long CellLong(JToken value)
        {
            return value != null && value.Type == JTokenType.Integer ? (long)value : 0;
        }

        private static double CellDouble(JToken value)
        {
            if (value == null)
            {
                return 0;
            }
            if (value.Type == JTokenType.Integer)
            {
                return (long)value;
            }
            return value.Type == JTokenType.Float ? (double)value : 0;
        }

        private static bool IsListField(TableRowField field)
        {
            return field.FieldType.IsGenericType &&
                field.FieldType.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static FieldEditKind FieldEditKindOf(TableRowField field)
        {
            Type type = field.FieldType;
            if (type == typeof(string) ||
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataRef<>)))
            {
                return FieldEditKind.Text;
            }
            if (type == typeof(bool))
            {
                return FieldEditKind.Bool;
            }
            if (type == typeof(int) || type == typeof(long))
            {
                return FieldEditKind.Integer;
            }
            if (type == typeof(float) || type == typeof(double))
            {
                return FieldEditKind.Real;
            }
            if (IsListField(field))
            {
                return FieldEditKind.Array;
            }
            return FieldEditKind.ReadOnly;
        }

        private void DrawTextCell(TableWorkspace workspace, JObject row, string field, string current, Rect cellRect)
        {
            EditorGUI.BeginChangeCheck();
            string text = EditorGUI.TextField(cellRect, current);
            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOncePerFocusSession(workspace);
                row[field] = text;
                MarkEdited(workspace);
            }
        }

        private void DrawBoolCell(TableWorkspace workspace, JObject row, string field, bool current, Rect cellRect)
        {
            EditorGUI.BeginChangeCheck();
            bool flag = EditorGUI.Toggle(cellRect, current);
            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOncePerFocusSession(workspace);
                row[field] = flag;
                MarkEdited(workspace);
            }
        }

        private void DrawLongCell(TableWorkspace workspace, JObject row, string field, long current, Rect cellRect)
        {
            EditorGUI.BeginChangeCheck();
            long integer = EditorGUI.LongField(cellRect, current);
            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOncePerFocusSession(workspace);
                row[field] = integer;
                MarkEdited(workspace);
            }
        }

        private void DrawDoubleCell(TableWorkspace workspace, JObject row, string field, double current, Rect cellRect)
        {
            EditorGUI.BeginChangeCheck();
            double real = EditorGUI.DoubleField(cellRect, current);
            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOncePerFocusSession(workspace);
                row[field] = real;
                MarkEdited(workspace);
            }
        }

        private void DrawEnumCell(
            TableWorkspace workspace, JObject row, string field, JToken value, Rect cellRect)
        {
            string current = CellText(value);
            if (UnityEngine.GUI.Button(
                    cellRect, string.IsNullOrEmpty(current) ? "(空)" : current, EditorStyles.popup))
            {
                OpenEnumValueDropdown(workspace, row, field, current, cellRect);
            }
        }

        private void OpenEnumValueDropdown(
            TableWorkspace workspace, JObject row, string field, string current, Rect cellRect)
        {
            string[] names = workspace.EnumFields[field];
            var options = new List<(string Value, bool Enabled)>();
            if (!string.IsNullOrEmpty(current) && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                //存量非法值仅作展示项,不可选中
                options.Add((current + " (当前值,不存在)", false));
            }
            foreach (string name in names)
            {
                options.Add((name, true));
            }
            var dropdown = new TableValueDropdown(
                new AdvancedDropdownState(),
                workspace.Source.DisplayName + "." + field,
                options,
                value => CommitDropdownSelection(workspace, row, field, value));
            //弹窗宽度跟随激活矩形,钳到固定区间避免贴到整个网格宽
            var activator = new Rect(
                cellRect.x, cellRect.y, Mathf.Clamp(cellRect.width, 200f, 360f), cellRect.height);
            dropdown.Show(activator);
        }

        private void CommitDropdownSelection(TableWorkspace workspace, JObject row, string field, string value)
        {
            if (value == CellText(row[field]))
            {
                return;
            }
            PushUndoSnapshot(workspace);
            row[field] = value;
            MarkEdited(workspace);
        }

        private void DrawArrayCell(
            TableWorkspace workspace, int storageIndex, string field, JArray array, Rect cellRect)
        {
            if (UnityEngine.GUI.Button(cellRect, BuildArraySummary(array), EditorStyles.miniButton))
            {
                workspace.View.SelectedRow = storageIndex;
                //弹窗内的多次修改以打开前的快照算作一步撤销,因此直接压栈而不走焦点会话
                PushUndoSnapshot(workspace);
                PopupWindow.Show(cellRect, new ArrayFieldEditPopup(array, () => MarkEdited(workspace)));
            }
        }

        private static void DrawReadOnlyCell(JToken value, Rect cellRect)
        {
            string summary = value == null
                ? ""
                : value.ToString(Newtonsoft.Json.Formatting.None);
            if (summary.Length > 80)
            {
                summary = summary.Substring(0, 80) + "...";
            }
            EditorGUI.LabelField(cellRect, summary, EditorStyles.miniLabel);
        }

        private static string BuildArraySummary(JArray array)
        {
            string joined = "[" + string.Join(", ", array
                .Select(element => element.Type == JTokenType.Null
                    ? "null"
                    : element.ToString(Newtonsoft.Json.Formatting.None))
                .ToArray()) + "]";
            return joined.Length > 60 ? joined.Substring(0, 60) + "..." : joined;
        }

        private static string CellControlName(int tableIndex, int column, int storageIndex)
        {
            //表索引编入控件名:切表后同名单元格换名,焦点会话随之结束,撤销压栈标志才能正确复位
            return "cell_" + tableIndex + "_" + column + "_" + storageIndex;
        }

        private void TrackFocusedRow(TableWorkspace workspace)
        {
            string focus = UnityEngine.GUI.GetNameOfFocusedControl();
            if (!focus.StartsWith("cell_", StringComparison.Ordinal))
            {
                return;
            }
            int lastUnderscore = focus.LastIndexOf('_');
            if (lastUnderscore > 0 &&
                int.TryParse(focus.Substring(lastUnderscore + 1), out int storageIndex) &&
                storageIndex >= 0 && storageIndex < workspace.Rows.Count)
            {
                workspace.View.SelectedRow = storageIndex;
            }
        }

        private List<string> FindCellErrors(TableWorkspace workspace, JObject row, string field)
        {
            string entryId = workspace.Source.ExtractKey(row);
            if (entryId == null)
            {
                return null;
            }
            List<string> messages = null;
            foreach (TableBrowserError error in workspace.Errors)
            {
                if (error.FieldName == field && error.EntryId == entryId)
                {
                    (messages ?? (messages = new List<string>())).Add(error.Message);
                }
            }
            return messages;
        }

        private bool RowHasEntryLevelError(TableWorkspace workspace, JObject row, GridView view)
        {
            string entryId = workspace.Source.ExtractKey(row);
            if (entryId == null)
            {
                return workspace.Errors.Any(error => error.EntryId == null);
            }
            return workspace.Errors.Any(error =>
                error.EntryId == entryId &&
                (error.FieldName == null || !view.Columns.Contains(error.FieldName)));
        }

        private void DrawErrorList(TableWorkspace workspace)
        {
            if (workspace.Errors.Count == 0)
            {
                return;
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxHeight(120f));
            EditorGUILayout.LabelField(
                "校验错误 " + workspace.Errors.Count + " 条(点击定位,修改后即时刷新,不阻断保存):",
                EditorStyles.boldLabel);
            errorListScroll = EditorGUILayout.BeginScrollView(errorListScroll);
            foreach (TableBrowserError error in workspace.Errors)
            {
                if (GUILayout.Button("  " + error, EditorStyles.label))
                {
                    LocateError(workspace, error);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void LocateError(TableWorkspace workspace, TableBrowserError error)
        {
            workspace.View.Search = "";
            if (error.EntryId != null)
            {
                for (int index = 0; index < workspace.Rows.Count; index++)
                {
                    if (workspace.Rows[index].Type != JTokenType.Object)
                    {
                        continue;
                    }
                    if (workspace.Source.ExtractKey((JObject)workspace.Rows[index]) == error.EntryId)
                    {
                        workspace.View.SelectedRow = index;
                        int column = workspace.View.Columns.IndexOf(error.FieldName);
                        if (column >= 0)
                        {
                            workspace.View.PendingFocusCell =
                                CellControlName(workspaces.IndexOf(workspace), column, index);
                        }
                        break;
                    }
                }
            }

            List<int> visible = ComputeVisibleRows(workspace);
            int viewIndex = workspace.View.SelectedRow >= 0
                ? visible.IndexOf(workspace.View.SelectedRow)
                : -1;
            if (viewIndex >= 0)
            {
                workspace.View.Scroll.y = Mathf.Max(0f, RowHeight * (viewIndex + 1) - 64f);
            }
            Repaint();
        }

        private void AddRow(TableWorkspace workspace)
        {
            PushUndoSnapshot(workspace);
            JObject template = workspace.Source.CreateEntryTemplate();
            template[workspace.Source.KeyFieldName] = GenerateUniqueKey(workspace);
            workspace.Rows.Add(template);
            int storageIndex = workspace.Rows.Count - 1;
            workspace.View.SelectedRow = storageIndex;
            workspace.View.PendingFocusCell = CellControlName(displayedTable, 0, storageIndex);
            MarkEdited(workspace);
        }

        private void DeleteSelectedRow(TableWorkspace workspace)
        {
            int index = workspace.View.SelectedRow;
            if (index < 0 || index >= workspace.Rows.Count)
            {
                return;
            }
            PushUndoSnapshot(workspace);
            workspace.Rows.RemoveAt(index);
            workspace.View.SelectedRow = Mathf.Min(index, workspace.Rows.Count - 1);
            MarkEdited(workspace);
        }

        private void PushUndoOncePerFocusSession(TableWorkspace workspace)
        {
            if (undoPushedForFocusSession)
            {
                return;
            }
            PushUndoSnapshot(workspace);
            undoPushedForFocusSession = true;
        }

        private void PushUndoSnapshot(TableWorkspace workspace)
        {
            workspace.View.UndoEntries.Add((JArray)workspace.Rows.DeepClone());
            if (workspace.View.UndoEntries.Count > UndoCapacity)
            {
                workspace.View.UndoEntries.RemoveAt(0);
            }
            workspace.View.RedoEntries.Clear();
        }

        private void UndoOne()
        {
            TableWorkspace workspace = CurrentTable;
            if (workspace == null || workspace.View.UndoEntries.Count == 0)
            {
                return;
            }
            workspace.View.RedoEntries.Add((JArray)workspace.Rows.DeepClone());
            workspace.Rows = workspace.View.UndoEntries[workspace.View.UndoEntries.Count - 1];
            workspace.View.UndoEntries.RemoveAt(workspace.View.UndoEntries.Count - 1);
            RestoreAfterHistoryJump(workspace);
        }

        private void RedoOne()
        {
            TableWorkspace workspace = CurrentTable;
            if (workspace == null || workspace.View.RedoEntries.Count == 0)
            {
                return;
            }
            workspace.View.UndoEntries.Add((JArray)workspace.Rows.DeepClone());
            workspace.Rows = workspace.View.RedoEntries[workspace.View.RedoEntries.Count - 1];
            workspace.View.RedoEntries.RemoveAt(workspace.View.RedoEntries.Count - 1);
            RestoreAfterHistoryJump(workspace);
        }

        private void RestoreAfterHistoryJump(TableWorkspace workspace)
        {
            UnityEngine.GUI.FocusControl(null);
            focusedCellControl = "";
            undoPushedForFocusSession = false;
            workspace.View.SelectedRow = -1;
            MarkEdited(workspace);
        }

        private void MarkEdited(TableWorkspace workspace)
        {
            workspace.Dirty = true;
            ValidateAll();
        }

        private void SaveCurrentTable(TableWorkspace workspace)
        {
            try
            {
                workspace.Source.Save(workspace.Rows);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("保存失败", ex.Message, "确定");
                return;
            }
            AssetDatabase.Refresh();
            workspace.Dirty = false;
        }

        private void DiscardChanges(TableWorkspace workspace)
        {
            workspace.View.UndoEntries.Clear();
            workspace.View.RedoEntries.Clear();
            try
            {
                workspace.Rows = workspace.Source.LoadRows();
                workspace.Source.ApplyEvolutionShape(workspace.Rows);
                workspace.LoadError = null;
            }
            catch (Exception ex)
            {
                workspace.LoadError = ex.Message;
            }
            workspace.Dirty = false;
            workspace.View.SelectedRow = -1;
            ValidateAll();
        }

        private void HandleSelectionChanged()
        {
            //窗口只被选中的表文件牵引:空选、非 json 或无合法表头的文件不抢已打开的表
            OpenSelectionIfTable();
        }

        private void OpenSelectionIfTable()
        {
            string path = SelectedJsonPath();
            if (path == null)
            {
                return;
            }
            TableWorkspace current = CurrentTable;
            if (current != null &&
                string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TryCreateSourceFromSelection(path, out ITableBrowserSource source, out TableHeader mismatchHeader))
            {
                if (mismatchHeader != null)
                {
                    if (ConfirmDiscardIfDirty(current))
                    {
                        OpenMismatchTable(mismatchHeader, path);
                    }
                }
                return;
            }
            if (ConfirmDiscardIfDirty(current))
            {
                OpenTable(source);
            }
        }

        /// <summary>当前表有未保存修改时先询问处置;返回是否可以切换,选「留在当前表」时返回 false。</summary>
        private bool ConfirmDiscardIfDirty(TableWorkspace current)
        {
            if (current == null || current.Source == null || !current.Dirty)
            {
                return true;
            }
            int choice = EditorUtility.DisplayDialogComplex(
                "未保存的修改",
                "表 " + current.Source.DisplayName + " 存在未保存的修改。切换选中文件前如何处理?",
                "保存并切换", "留在当前表", "放弃并切换");
            if (choice == 0)
            {
                SaveCurrentTable(current);
                return !current.Dirty;
            }
            if (choice == 2)
            {
                DiscardChanges(current);
                return true;
            }
            return false;
        }

        /// <summary>按路径打开表文件(重选类型后的重开路径);失败时仅静默返回,打开流程已呈现原因。</summary>
        private void OpenTableFromPath(string path)
        {
            if (!TryCreateSourceFromSelection(path, out ITableBrowserSource source, out _))
            {
                return;
            }
            OpenTable(source);
        }

        private static string SelectedJsonPath()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
            {
                return null;
            }
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// 解析选中文件并构造表源。JSON 损坏或表头非法时 log 警告且不进入编辑;
        /// 表头行类型不在已编译类型中时经 <paramref name="mismatchHeader"/> 交回失配态。
        /// </summary>
        private static bool TryCreateSourceFromSelection(
            string path,
            out ITableBrowserSource source,
            out TableHeader mismatchHeader)
        {
            source = null;
            mismatchHeader = null;
            JToken root;
            try
            {
                root = JToken.Parse(
                    File.ReadAllText(TableBrowserSource<TableRow>.ToAbsolutePath(path)));
            }
            catch (Exception)
            {
                Debug.LogWarning("[Mochi] 无法打开，可能非数据表文件或数据损坏: " + path);
                return false;
            }
            if (root.Type != JTokenType.Object ||
                !TableHeader.TryParse((JObject)root, out TableHeader header, out _))
            {
                Debug.LogWarning("[Mochi] 无法打开，可能非数据表文件或数据损坏: " + path);
                return false;
            }
            try
            {
                source = TableBrowserSourceFactory.CreateFromHeader(header, path);
                return true;
            }
            catch (InvalidOperationException)
            {
                //表头行类型不在已编译类型中(被重命名或删除),进入失配横幅与重选流程
                mismatchHeader = header;
                return false;
            }
        }

        private void OpenTable(ITableBrowserSource source)
        {
            var workspace = new TableWorkspace { Source = source, Path = source.Path };
            foreach (var pair in source.EnumFields)
            {
                workspace.EnumFields[pair.Key] = pair.Value;
            }
            try
            {
                workspace.Rows = source.LoadRows();
                source.ApplyEvolutionShape(workspace.Rows);
            }
            catch (Exception ex)
            {
                workspace.LoadError = ex.Message;
            }
            workspaces.Clear();
            workspaces.Add(workspace);
            displayedTable = 0;
            RebuildColumns(workspace);
            ValidateAll();
        }

        private void OpenMismatchTable(TableHeader header, string path)
        {
            var workspace = new TableWorkspace { Source = null, Header = header, Path = path };
            workspaces.Clear();
            workspaces.Add(workspace);
            displayedTable = 0;
        }

        private List<int> ComputeVisibleRows(TableWorkspace workspace)
        {
            IEnumerable<int> indices = Enumerable.Range(0, workspace.Rows.Count)
                .Where(index => workspace.Rows[index].Type == JTokenType.Object);
            if (!string.IsNullOrEmpty(workspace.View.Search))
            {
                indices = indices.Where(index =>
                    RowMatchesSearch((JObject)workspace.Rows[index], workspace.View.Search));
            }
            int sortColumn = workspace.View.SortColumn;
            if (sortColumn >= 0 && sortColumn < workspace.View.Columns.Count)
            {
                string field = workspace.View.Columns[sortColumn];
                indices = workspace.View.SortDescending
                    ? indices.OrderByDescending(index => workspace.Rows[index][field], TokenComparer.Instance)
                    : indices.OrderBy(index => workspace.Rows[index][field], TokenComparer.Instance);
            }
            return indices.ToList();
        }

        private static bool RowMatchesSearch(JObject row, string filter)
        {
            foreach (JProperty property in row.Properties())
            {
                if (property.Value.Type != JTokenType.String)
                {
                    continue;
                }
                string text = (string)property.Value ?? "";
                if (text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

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
            //当前窗口至多打开一张表,重新载入即重读这张表;无打开表时无可重载内容
            TableWorkspace workspace = CurrentTable;
            if (workspace == null)
            {
                return;
            }
            if (workspace.Source == null)
            {
                workspaces.Clear();
                displayedTable = -1;
                return;
            }
            DiscardChanges(workspace);
        }

        /// <summary>列只来自行类型当前字段:已删字段不再显示,保存时从文件移除;行数据里的陌生字段同理。</summary>
        private void RebuildColumns(TableWorkspace workspace)
        {
            workspace.View.Columns = workspace.Source.Fields
                .Select(field => field.Name)
                .ToList();
            var widths = new float[workspace.View.Columns.Count];
            for (int index = 0; index < workspace.View.Columns.Count; index++)
            {
                Vector2 size = EditorStyles.miniButton.CalcSize(
                    new GUIContent(workspace.View.Columns[index]));
                widths[index] = Mathf.Clamp(size.x + 34f, 110f, 300f);
            }
            workspace.View.ColumnWidths = widths;
            if (workspace.View.SortColumn >= workspace.View.Columns.Count)
            {
                workspace.View.SortColumn = -1;
            }
        }

        private void ValidateAll()
        {
            foreach (TableWorkspace workspace in workspaces)
            {
                workspace.Errors.Clear();
                if (workspace.Source != null && workspace.LoadError == null)
                {
                    workspace.Errors.AddRange(workspace.Source.Validate(workspace.Rows));
                }
            }
            Repaint();
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

        private sealed class TokenComparer : IComparer<JToken>
        {
            public static readonly TokenComparer Instance = new TokenComparer();

            public int Compare(JToken x, JToken y)
            {
                bool xMissing = x == null || x.Type == JTokenType.Null;
                bool yMissing = y == null || y.Type == JTokenType.Null;
                if (xMissing || yMissing)
                {
                    return xMissing ? (yMissing ? 0 : -1) : 1;
                }
                if (IsNumeric(x) && IsNumeric(y))
                {
                    return Convert.ToDouble(((JValue)x).Value)
                        .CompareTo(Convert.ToDouble(((JValue)y).Value));
                }
                return string.Compare(CellSortText(x), CellSortText(y), StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsNumeric(JToken token)
            {
                return token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
            }

            private static string CellSortText(JToken token)
            {
                if (token.Type == JTokenType.Array)
                {
                    return BuildArraySummary((JArray)token);
                }
                return token.ToString();
            }
        }
    }
}
