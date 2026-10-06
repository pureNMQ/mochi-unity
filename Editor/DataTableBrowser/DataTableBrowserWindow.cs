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
    /// 通用数据表浏览器:内容跟随 Project 窗口当前选中的表文件,以类 Excel 网格呈现,
    /// 列=表全部字段、行=条目、单元格直接编辑,支持列头排序、搜索过滤与撤销重做。
    /// 磁盘读写与编辑期校验全部经 <see cref="ITableBrowserSource"/> 完成;
    /// 编辑中的未保存修改即时进入快照,参与其他表的引用解析。
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
            public ITableBrowserSource Source;
            public JArray Rows = new JArray();
            public bool Dirty;
            public string LoadError;
            public readonly List<TableEditError> Errors = new List<TableEditError>();
            public readonly List<string> ReferenceFields = new List<string>();
            public readonly Dictionary<string, string[]> EnumFields = new Dictionary<string, string[]>();
            public readonly GridView View = new GridView();
        }

        private sealed class ComboboxOption
        {
            public string Value;
            public string Display;
            public bool Committable;
        }

        private sealed class ComboboxState
        {
            public string ControlName;
            public int TableIndex;
            public int StorageIndex;
            public string Field;
            public JObject Row;
            public string OriginalValue;
            public string TypedText;
            public List<ComboboxOption> Options;
            public List<ComboboxOption> Filtered;
            public int ActiveIndex;
        }

        private readonly List<TableWorkspace> workspaces = new List<TableWorkspace>();
        private TableKeySetResolver keyResolver = new TableKeySetResolver();
        private int displayedTable = -1;
        private Vector2 errorListScroll;

        //单元格撤销以「焦点会话」为粒度:控件获得焦点后首次值变化压入一次快照,
        //同一焦点期内的后续击键不再重复压栈;焦点移出即结束会话
        private string focusedCellControl = "";
        private bool undoPushedForFocusSession;
        private ComboboxState combobox;

        /// <summary>打开数据表浏览器窗口(菜单 Mochi/Data Table Browser)。</summary>
        [MenuItem("Mochi/Data Table Browser")]
        public static void Open()
        {
            GetWindow<DataTableBrowserWindow>("Data Table Browser");
        }

        private void OnEnable()
        {
            minSize = new Vector2(720f, 400f);
            ReloadAll();
            if (workspaces.Count == 0)
            {
                //域重载后 OnEnable 可能早于 TypeCache 就绪执行,目录类型查不到会误报空目录;编辑器稳定后补载一次
                EditorApplication.delayCall += ReloadIfStillEmpty;
            }
        }

        private void ReloadIfStillEmpty()
        {
            if (this == null || workspaces.Count > 0)
            {
                return;
            }
            ReloadAll();
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
            UpdateComboboxForFocus(CurrentTable);
            HandleComboboxKeys();
            DrawToolbar();

            TableWorkspace workspace = CurrentTable;
            if (workspace == null)
            {
                DrawHint();
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
            bool editable = workspace != null && workspace.LoadError == null;

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
                workspace.View.Search = EditorGUILayout.TextField(
                    workspace.View.Search, EditorStyles.toolbarTextField, GUILayout.MinWidth(160f));
            }

            if (GUILayout.Button("从磁盘重新载入", EditorStyles.toolbarButton))
            {
                ReloadFromDiskWithConfirm();
            }

            GUILayout.FlexibleSpace();
            if (workspace != null)
            {
                string status = workspace.LoadError != null
                    ? "读取失败"
                    : (workspace.Dirty ? "* 未保存" : "已保存") +
                        (workspace.Errors.Count > 0 ? "  |  校验错误 " + workspace.Errors.Count + " 条" : "");
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableHeader(TableWorkspace workspace)
        {
            EditorGUILayout.LabelField(
                workspace.Source.DisplayName + "  (" + workspace.Source.StorageKind + ")  条目 " +
                workspace.Rows.Count + (workspace.Dirty ? "  未保存" : ""),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(workspace.Source.Path, EditorStyles.miniLabel);
        }

        private void DrawHint()
        {
            if (workspaces.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有发现任何表目录。请实现 ITableBrowserCatalog 并声明表源。",
                    MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox(
                "在 Project 窗口选中一个已声明的表文件,即可在此以网格打开该表:\n" +
                string.Join("\n", workspaces.Select(item => "  " + item.Source.Path).ToArray()),
                MessageType.Info);
        }

        private void DrawGrid(TableWorkspace workspace)
        {
            ApplyPendingFocus(workspace);
            List<int> visibleRows = ComputeVisibleRows(workspace);

            workspace.View.Scroll = EditorGUILayout.BeginScrollView(workspace.View.Scroll, false, true);
            DrawHeaderRow(workspace);
            for (int viewIndex = 0; viewIndex < visibleRows.Count; viewIndex++)
            {
                int storageIndex = visibleRows[viewIndex];
                DrawDataRow(workspace, storageIndex, viewIndex);
                if (combobox != null && combobox.TableIndex == displayedTable &&
                    combobox.StorageIndex == storageIndex)
                {
                    DrawComboboxDropdown(workspace);
                }
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
            JToken value = row[field];
            if (value == null)
            {
                //字段被手改数据整列删除时以空文本兜底,补上值后该行恢复完整字段
                EditorGUI.BeginChangeCheck();
                string restored = EditorGUI.TextField(cellRect, "");
                if (EditorGUI.EndChangeCheck())
                {
                    PushUndoOncePerFocusSession(workspace);
                    row[field] = restored;
                    MarkEdited(workspace);
                }
                return;
            }

            List<string> cellErrors = FindCellErrors(workspace, row, field);
            bool enumInvalid = IsInvalidEnumValue(workspace, field, value);
            if (cellErrors != null || enumInvalid)
            {
                EditorGUI.DrawRect(cellRect, ErrorCellColor);
            }

            if (value.Type != JTokenType.Array &&
                (workspace.EnumFields.ContainsKey(field) || workspace.ReferenceFields.Contains(field)))
            {
                DrawComboboxCell(workspace, row, field, cellRect);
            }
            else
            {
                switch (value.Type)
                {
                    case JTokenType.String:
                    case JTokenType.Null:
                        DrawTextCell(workspace, row, field,
                            value.Type == JTokenType.Null ? "" : (string)value, cellRect);
                        break;
                    case JTokenType.Boolean:
                        DrawBoolCell(workspace, row, field, (bool)value, cellRect);
                        break;
                    case JTokenType.Integer:
                        DrawLongCell(workspace, row, field, (long)value, cellRect);
                        break;
                    case JTokenType.Float:
                        DrawDoubleCell(workspace, row, field, (double)value, cellRect);
                        break;
                    case JTokenType.Array:
                        DrawArrayCell(workspace, storageIndex, field, (JArray)value, cellRect);
                        break;
                    default:
                        DrawReadOnlyCell(value, cellRect);
                        break;
                }
            }

            if (cellErrors != null || enumInvalid)
            {
                var messages = new List<string>();
                if (cellErrors != null)
                {
                    messages.AddRange(cellErrors);
                }
                if (enumInvalid)
                {
                    messages.Add("非法枚举名 " + value + ",不在合法值列表内。");
                }
                UnityEngine.GUI.Label(
                    cellRect, new GUIContent("", string.Join("\n", messages.ToArray())));
            }
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

        private void DrawComboboxCell(TableWorkspace workspace, JObject row, string field, Rect cellRect)
        {
            bool active = combobox != null && combobox.TableIndex == displayedTable &&
                combobox.Field == field && combobox.Row == row;
            string shown = active ? combobox.TypedText : ReadCellText(row, field);
            EditorGUI.BeginChangeCheck();
            string typed = EditorGUI.TextField(cellRect, shown);
            if (EditorGUI.EndChangeCheck() && active)
            {
                //击键只进输入缓冲参与过滤;模型写入只发生在提交有效选项时
                combobox.TypedText = typed;
            }
        }

        private void UpdateComboboxForFocus(TableWorkspace workspace)
        {
            if (combobox != null && focusedCellControl != combobox.ControlName)
            {
                combobox = null;
            }
            if (combobox != null || workspace == null || !focusedCellControl.StartsWith("cell_"))
            {
                return;
            }
            string[] parts = focusedCellControl.Split('_');
            if (parts.Length != 4 ||
                !int.TryParse(parts[1], out int tableIndex) || tableIndex != displayedTable ||
                !int.TryParse(parts[2], out int column) || column >= workspace.View.Columns.Count ||
                !int.TryParse(parts[3], out int storageIndex) ||
                storageIndex < 0 || storageIndex >= workspace.Rows.Count)
            {
                return;
            }
            string field = workspace.View.Columns[column];
            if (!workspace.EnumFields.ContainsKey(field) && !workspace.ReferenceFields.Contains(field))
            {
                return;
            }
            OpenCombobox(workspace, tableIndex, column, storageIndex, field);
        }

        private void OpenCombobox(
            TableWorkspace workspace, int tableIndex, int column, int storageIndex, string field)
        {
            var row = (JObject)workspace.Rows[storageIndex];
            string current = ReadCellText(row, field);
            var options = new List<ComboboxOption>();
            if (workspace.EnumFields.TryGetValue(field, out string[] names))
            {
                foreach (string name in names)
                {
                    options.Add(new ComboboxOption { Value = name, Display = name, Committable = true });
                }
            }
            else
            {
                TableReference reference = workspace.Source.References.First(item => item.FieldName == field);
                foreach (string id in ResolveTargetIds(reference.TargetTableName))
                {
                    options.Add(new ComboboxOption { Value = id, Display = id, Committable = true });
                }
            }
            if (!string.IsNullOrEmpty(current) && options.All(option => option.Value != current))
            {
                //存量悬空/非法值仅作展示项,不可经提交固化
                options.Insert(0, new ComboboxOption
                {
                    Value = current,
                    Display = current + " (当前值,不可提交)",
                    Committable = false
                });
            }
            combobox = new ComboboxState
            {
                ControlName = CellControlName(tableIndex, column, storageIndex),
                TableIndex = tableIndex,
                StorageIndex = storageIndex,
                Field = field,
                Row = row,
                OriginalValue = current,
                TypedText = current,
                Options = options,
                Filtered = new List<ComboboxOption>(options)
            };
        }

        private void DrawComboboxDropdown(TableWorkspace workspace)
        {
            combobox.Filtered = combobox.Options
                .Where(option => string.IsNullOrEmpty(combobox.TypedText) ||
                    option.Display.IndexOf(combobox.TypedText, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (combobox.ActiveIndex >= combobox.Filtered.Count)
            {
                combobox.ActiveIndex = combobox.Filtered.Count - 1;
            }
            if (combobox.ActiveIndex < 0)
            {
                combobox.ActiveIndex = 0;
            }

            float height = combobox.Filtered.Count == 0
                ? 22f
                : Mathf.Min(160f, combobox.Filtered.Count * 20f + 4f);
            Rect rect = GUILayoutUtility.GetRect(TotalGridWidth(workspace.View), height);
            UnityEngine.GUI.Box(rect, "");
            if (combobox.Filtered.Count == 0)
            {
                UnityEngine.GUI.Label(rect, "无匹配项", EditorStyles.miniLabel);
                return;
            }
            for (int index = 0; index < combobox.Filtered.Count; index++)
            {
                ComboboxOption option = combobox.Filtered[index];
                var optionRect = new Rect(rect.x + 2f, rect.y + 2f + index * 20f, rect.width - 4f, 20f);
                using (new EditorGUI.DisabledScope(!option.Committable))
                {
                    if (UnityEngine.GUI.Button(
                        optionRect,
                        (index == combobox.ActiveIndex ? "> " : "") + option.Display,
                        EditorStyles.miniButton))
                    {
                        CommitCombobox(workspace, option);
                    }
                }
            }
        }

        private void HandleComboboxKeys()
        {
            if (combobox == null)
            {
                return;
            }
            UnityEngine.Event current = UnityEngine.Event.current;
            if (current.type != EventType.KeyDown)
            {
                return;
            }
            if (current.keyCode == KeyCode.DownArrow)
            {
                combobox.ActiveIndex = Mathf.Min(
                    combobox.ActiveIndex + 1, Mathf.Max(combobox.Filtered.Count - 1, 0));
                current.Use();
            }
            else if (current.keyCode == KeyCode.UpArrow)
            {
                combobox.ActiveIndex = Mathf.Max(combobox.ActiveIndex - 1, 0);
                current.Use();
            }
            else if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                CommitActiveOption(CurrentTable);
                current.Use();
            }
            else if (current.keyCode == KeyCode.Escape)
            {
                CloseComboboxWithRevert();
                current.Use();
            }
        }

        private void CommitActiveOption(TableWorkspace workspace)
        {
            if (workspace == null || combobox == null || combobox.Filtered.Count == 0)
            {
                return;
            }
            CommitCombobox(workspace,
                combobox.Filtered[Mathf.Clamp(combobox.ActiveIndex, 0, combobox.Filtered.Count - 1)]);
        }

        private void CommitCombobox(TableWorkspace workspace, ComboboxOption option)
        {
            if (!option.Committable || option.Value == combobox.OriginalValue)
            {
                combobox.TypedText = option.Value;
                return;
            }
            PushUndoOncePerFocusSession(workspace);
            combobox.Row[combobox.Field] = option.Value;
            combobox.OriginalValue = option.Value;
            combobox.TypedText = option.Value;
            MarkEdited(workspace);
        }

        private void CloseComboboxWithRevert()
        {
            combobox = null;
            UnityEngine.GUI.FocusControl(null);
        }

        private static string ReadCellText(JObject row, string field)
        {
            JToken token = row[field];
            if (token == null || token.Type == JTokenType.Null)
            {
                return "";
            }
            return token.ToString();
        }

        private static bool IsInvalidEnumValue(TableWorkspace workspace, string field, JToken value)
        {
            return workspace.EnumFields.TryGetValue(field, out string[] names) &&
                (value.Type == JTokenType.String || value.Type == JTokenType.Null) &&
                !names.Contains(value.ToString(), StringComparer.OrdinalIgnoreCase);
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
            string summary = value.ToString(Newtonsoft.Json.Formatting.None);
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
            foreach (TableEditError error in workspace.Errors)
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
            foreach (TableEditError error in workspace.Errors)
            {
                if (GUILayout.Button("  " + error, EditorStyles.label))
                {
                    LocateError(workspace, error);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void LocateError(TableWorkspace workspace, TableEditError error)
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
            combobox = null;
            workspace.View.SelectedRow = -1;
            RebuildColumns(workspace);
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
                workspace.LoadError = null;
            }
            catch (Exception ex)
            {
                workspace.LoadError = ex.Message;
            }
            workspace.Dirty = false;
            workspace.View.SelectedRow = -1;
            RebuildColumns(workspace);
            ValidateAll();
        }

        private void HandleSelectionChanged()
        {
            int target = FindTableIndexForSelection();
            if (target == displayedTable)
            {
                return;
            }
            TableWorkspace current = CurrentTable;
            if (current == null || !current.Dirty)
            {
                displayedTable = target;
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "未保存的修改",
                "表 " + current.Source.DisplayName + " 存在未保存的修改。切换选中文件前如何处理?",
                "保存并切换", "留在当前表", "放弃并切换");
            if (choice == 0)
            {
                SaveCurrentTable(current);
                if (!current.Dirty)
                {
                    displayedTable = target;
                }
            }
            else if (choice == 2)
            {
                DiscardChanges(current);
                displayedTable = target;
            }
            //选「留在当前表」时保持显示当前表,不回改 Project 选中;下次选择变化会再次询问
        }

        private int FindTableIndexForSelection()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
            {
                return -1;
            }
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path))
            {
                return -1;
            }
            path = path.Replace('\\', '/');
            for (int index = 0; index < workspaces.Count; index++)
            {
                if (string.Equals(workspaces[index].Source.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            for (int index = 0; index < workspaces.Count; index++)
            {
                TableWorkspace workspace = workspaces[index];
                //FolderPerEntry 声明的是条目文件夹,选中其中任一条目文件同样视为选中该表
                if (workspace.Source.StorageKind == TableStorageKind.FolderPerEntry &&
                    path.StartsWith(workspace.Source.Path + "/", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }

        private List<string> ResolveTargetIds(string tableName)
        {
            var ids = new List<string>();
            TableWorkspace target = workspaces.FirstOrDefault(item => item.Source.TableName == tableName);
            if (target == null)
            {
                return ids;
            }
            foreach (JToken token in target.Rows)
            {
                if (token.Type != JTokenType.Object)
                {
                    continue;
                }
                string key = target.Source.ExtractKey((JObject)token);
                if (!string.IsNullOrEmpty(key) && !ids.Contains(key))
                {
                    ids.Add(key);
                }
            }
            return ids;
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
                    Debug.LogError("表目录 " + catalogType.Name + " 实例化失败: " + ex.Message);
                    continue;
                }
                foreach (ITableBrowserSource source in catalog.CreateSources())
                {
                    var workspace = new TableWorkspace { Source = source };
                    workspace.ReferenceFields.AddRange(
                        source.References.Select(reference => reference.FieldName).Distinct());
                    foreach (var pair in source.EnumFields)
                    {
                        workspace.EnumFields[pair.Key] = pair.Value;
                    }
                    try
                    {
                        workspace.Rows = source.LoadRows();
                    }
                    catch (Exception ex)
                    {
                        workspace.LoadError = ex.Message;
                    }
                    RebuildColumns(workspace);
                    workspaces.Add(workspace);
                }
            }

            keyResolver = new TableKeySetResolver();
            displayedTable = FindTableIndexForSelection();
            ValidateAll();
        }

        private void RebuildColumns(TableWorkspace workspace)
        {
            var columns = new List<string>();
            try
            {
                columns.AddRange(workspace.Source.CreateEntryTemplate()
                    .Properties()
                    .Select(property => property.Name));
            }
            catch (Exception)
            {
                //行类型无法实例化时退化为仅按行数据出现过的字段建列
            }
            foreach (JToken token in workspace.Rows)
            {
                if (token.Type != JTokenType.Object)
                {
                    continue;
                }
                foreach (JProperty property in ((JObject)token).Properties())
                {
                    if (!columns.Contains(property.Name))
                    {
                        columns.Add(property.Name);
                    }
                }
            }

            workspace.View.Columns = columns;
            var widths = new float[columns.Count];
            for (int index = 0; index < columns.Count; index++)
            {
                Vector2 size = EditorStyles.miniButton.CalcSize(new GUIContent(columns[index]));
                widths[index] = Mathf.Clamp(size.x + 34f, 110f, 300f);
            }
            workspace.View.ColumnWidths = widths;
            if (workspace.View.SortColumn >= columns.Count)
            {
                workspace.View.SortColumn = -1;
            }
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
            Repaint();
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
