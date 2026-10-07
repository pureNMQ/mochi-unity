using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 数组字段单元格的逐项编辑弹窗:原始类型元素以文本行编辑,对象元素展开为逐字段控件;
    /// 每次修改经回调即时通知宿主窗口置脏并触发编辑期校验。
    /// </summary>
    internal sealed class ArrayFieldEditPopup : PopupWindowContent
    {
        private const float PopupWidth = 400f;
        private const float PopupHeight = 320f;

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
                "数组字段编辑(共 " + array.Count + " 项):", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int index = 0; index < array.Count; index++)
            {
                JToken element = array[index];
                if (element != null && element.Type == JTokenType.Object)
                {
                    DrawObjectElement((JObject)element, index);
                }
                else
                {
                    DrawTextElement(index);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加元素"))
            {
                //对象数组里追加字符串元素,保存后就是装载失败数据,新增元素的形状必须跟随既有元素
                array.Add(ContainsObjectElement() ? (JToken)new JObject() : "");
                NotifyChanged();
            }
            if (GUILayout.Button("完成"))
            {
                editorWindow.Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextElement(int index)
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
            DrawRemoveButton(index);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawObjectElement(JObject element, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("元素 " + index, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (DrawRemoveButton(index))
            {
                //元素已从数组移除,其字段不可再绘制
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            foreach (JProperty property in element.Properties())
            {
                DrawObjectField(element, property);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawObjectField(JObject element, JProperty property)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(property.Name);
            EditorGUI.BeginChangeCheck();
            JToken edited = DrawValueEditor(property.Value);
            if (EditorGUI.EndChangeCheck() && edited != null)
            {
                element[property.Name] = edited;
                NotifyChanged();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>按 JSON 值类型绘制编辑控件并返回编辑结果;只读形状返回 null,不产生修改。</summary>
        private static JToken DrawValueEditor(JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Boolean:
                    return EditorGUILayout.Toggle((bool)value);
                case JTokenType.Integer:
                    return EditorGUILayout.LongField((long)value);
                case JTokenType.Float:
                    return EditorGUILayout.DoubleField((double)value);
                case JTokenType.Object:
                case JTokenType.Array:
                    //组件数据是扁平对象,一层字段控件已覆盖;更深的嵌套只读展示
                    EditorGUILayout.LabelField(
                        value.ToString(Newtonsoft.Json.Formatting.None), EditorStyles.miniLabel);
                    return null;
                default:
                    //字符串与 null 都按文本编辑,null 一经提交就成为字符串,类型还原由装载校验兜底
                    return EditorGUILayout.TextField(value.Type == JTokenType.Null ? "" : (string)value);
            }
        }

        private bool DrawRemoveButton(int index)
        {
            if (!GUILayout.Button("移除", GUILayout.Width(44)))
            {
                return false;
            }
            array.RemoveAt(index);
            NotifyChanged();
            return true;
        }

        private bool ContainsObjectElement()
        {
            foreach (JToken element in array)
            {
                if (element != null && element.Type == JTokenType.Object)
                {
                    return true;
                }
            }
            return false;
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
