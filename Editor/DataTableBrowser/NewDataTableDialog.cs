using System;
using System.IO;
using Mochi.DataTable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 新建数据表对话框:填写表名并经可搜索下拉选取行类型(仅列 TableRow 派生类型),
    /// 确认后在指定位置生成含合法 "$table" 表头与空 rows 的 .json 文件,并把 Project 选中切到新文件,
    /// 浏览器随选中变化直接打开编辑。
    /// </summary>
    internal sealed class NewDataTableDialog : EditorWindow
    {
        private string tableName = "";
        private string selectedRowType;

        /// <summary>打开新建表对话框(工具栏「新建表」入口)。</summary>
        public static void Open()
        {
            GetWindow<NewDataTableDialog>(true, "新建数据表", true).minSize = new Vector2(460f, 150f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("新建数据表文件", EditorStyles.boldLabel);
            tableName = EditorGUILayout.TextField("表名", tableName);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("行类型", GUILayout.Width(45f));
            Rect typeRect = GUILayoutUtility.GetRect(
                new GUIContent(string.IsNullOrEmpty(selectedRowType) ? "未选择(点击选取)" : selectedRowType),
                EditorStyles.popup, GUILayout.ExpandWidth(true));
            if (UnityEngine.GUI.Button(typeRect,
                    string.IsNullOrEmpty(selectedRowType) ? "未选择(点击选取)" : selectedRowType,
                    EditorStyles.popup))
            {
                OpenRowTypePicker(typeRect);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("选择位置并创建", GUILayout.Width(140f)))
            {
                Create();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OpenRowTypePicker(Rect activator)
        {
            var options = new System.Collections.Generic.List<(string Value, bool Enabled)>();
            foreach (Type type in TableBrowserSourceFactory.ListRowTypes())
            {
                options.Add((type.FullName, true));
            }
            var dropdown = new TableValueDropdown(
                new AdvancedDropdownState(), "选取行类型", options,
                selected =>
                {
                    selectedRowType = selected;
                    Repaint();
                });
            dropdown.Show(new Rect(activator.x, activator.y,
                Mathf.Clamp(activator.width, 360f, 560f), activator.height));
        }

        private void Create()
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                EditorUtility.DisplayDialog("新建数据表", "表名不能为空。", "确定");
                return;
            }
            if (string.IsNullOrEmpty(selectedRowType))
            {
                EditorUtility.DisplayDialog("新建数据表", "请先选取行类型。", "确定");
                return;
            }

            string chosen = EditorUtility.SaveFilePanel("新建数据表", "Assets", tableName.Trim(), "json");
            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string full = Path.GetFullPath(chosen);
            if (!full.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("新建数据表", "请选择项目内的位置。", "确定");
                return;
            }
            string relative = full.Substring(projectRoot.Length + 1).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("新建数据表",
                    "数据表须生成在 Assets 目录内,才能被导入与浏览器打开。", "确定");
                return;
            }
            if (File.Exists(full) &&
                !EditorUtility.DisplayDialog("新建数据表",
                    "文件已存在,覆盖后将丢失原内容。继续?", "覆盖", "取消"))
            {
                return;
            }

            var root = new JObject
            {
                [TableHeader.FieldName] = new TableHeader(
                    tableName.Trim(), selectedRowType, TableHeader.CurrentFormatVersion).ToJson(),
                [TableHeader.RowsFieldName] = new JArray(),
            };
            File.WriteAllText(full, root.ToString(Formatting.Indented));
            AssetDatabase.ImportAsset(relative);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<TextAsset>(relative);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("新建数据表",
                    "文件已生成: " + relative + "\n在 Project 中选中它即可用数据表浏览器打开。", "确定");
            }
            else
            {
                //切换 Project 选中触发浏览器窗口的 OnSelectionChange,新表随即打开编辑
                Selection.activeObject = asset;
            }
            Close();
        }
    }
}
