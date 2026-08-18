using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Mochi.Event;


namespace Mochi.Unity.Editor
{
    public class EventPreview : EditorWindow
    {
        [MenuItem("Event", menuItem = "Mochi/EventPreview")]

        static void ShowWindow()
        {
            var window = GetWindow<EventPreview>();
            window.name = "Event Preview";
            window.Show();
        }

        Dictionary<Type, EventInfoAttribute> Infos = null;
        private void OnEnable()
        {
            Infos = GetInfos();
        }

        private Vector2 posi = Vector2.zero;
        private string searchString = "";
        private void OnGUI()
        {
            //EventCache.instance.sliderValue = EditorGUILayout.Slider(EventCache.instance.sliderValue, 0, 1);
            EditorGUILayout.BeginVertical();

            if (!string.IsNullOrEmpty(searchString))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("已复制:" + searchString, MessageType.Info);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("事件名", GUILayout.Width(200));
            GUILayout.Label("描述");
            EditorGUILayout.EndHorizontal();

            posi = EditorGUILayout.BeginScrollView(posi);
            foreach (var info in Infos)
            {
                GUIStyle style = new GUIStyle(EditorStyles.helpBox)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 14,
                    wordWrap = false,
                };
                EditorGUILayout.BeginHorizontal();
                bool isClick = GUILayout.Button(info.Key.Name, style, GUILayout.Width(200));
                if (isClick)
                {
                    GUIUtility.systemCopyBuffer = info.Key.Name;
                    searchString = info.Key.Name;
                }
                GUILayout.Label(info.Value.Description, new GUIStyle(EditorStyles.textArea), GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }



        public static Dictionary<Type, EventInfoAttribute> GetInfos()
        {
            Dictionary<Type, EventInfoAttribute> pairs = new Dictionary<Type, EventInfoAttribute>();
            // 获取当前应用程序域中加载的所有程序集
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            // 遍历每个程序集，查找指定名称的类型
            foreach (Assembly assembly in assemblies)
            {
                Type[] types = assembly.GetTypes();
                foreach (Type type in types)
                {
                    if (type.IsAbstract) continue;
                    if (type.IsSubclassOf(typeof(GlobalEventBase)))
                    {
                        var info = type.GetCustomAttribute<EventInfoAttribute>();
                        if (info != null)
                        {
                            if (info.Ignore) continue;
                            pairs.Add(type, info);
                        }
                        else
                        {
                            pairs.Add(type, new EventInfoAttribute("None"));
                        }

                    }
                }
            }

            return pairs;
        }

    }
}
