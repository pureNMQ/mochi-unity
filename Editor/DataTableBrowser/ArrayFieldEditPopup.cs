using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 数组字段单元格的逐项文本编辑弹窗:元素以文本行呈现,可增删改;
    /// 每次修改经回调即时通知宿主窗口置脏并触发编辑期校验。
    /// </summary>
    internal sealed class ArrayFieldEditPopup : PopupWindowContent
    {
        private const float PopupWidth = 400f;
        private const float PopupHeight = 260f;

        private readonly JArray array;
        private readonly Action onChanged;
        private Vector2 scroll;

        public ArrayFieldEditPopup(JArray array, Action onChanged)
        {
            this.array = array;
            this.onChanged = onChanged;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(PopupWidth, PopupHeight);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField(
                "数组字段编辑(逐项文本,共 " + array.Count + " 项):", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int index = 0; index < array.Count; index++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                //非字符串元素也按文本编辑(AC18 字面为文本输入);类型改写由装载校验兜底
                string text = EditorGUILayout.TextField(ElementText(array[index]));
                if (EditorGUI.EndChangeCheck())
                {
                    array[index] = text;
                    NotifyChanged();
                }
                if (GUILayout.Button("移除", GUILayout.Width(44)))
                {
                    array.RemoveAt(index);
                    NotifyChanged();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加元素"))
            {
                array.Add("");
                NotifyChanged();
            }
            if (GUILayout.Button("完成"))
            {
                editorWindow.Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string ElementText(JToken element)
        {
            return element == null || element.Type == JTokenType.Null ? "" : element.ToString();
        }

        private void NotifyChanged()
        {
            onChanged?.Invoke();
        }
    }
}
