using System.IO;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEngine;

namespace Mochi.Unity.Editor
{
    public static class UIPrefabCreator
    {
        [MenuItem("Assets/Create/GUI/UI Prefab", false, 5)]
        public static void CreateUIPrefab()
        {
            EditorTools.RequestCreateAsset(
                "New UI Prefab.prefab",
            EditorGUIUtility.IconContent("d_Prefab Icon").image as Texture2D,
            CreateUIPrefabInternal);
        }

        private static void CreateUIPrefabInternal(int instanceId, string pathName, string resourceFile)
        {
            var go = new GameObject("New UI Prefab");
            RectTransform rectTransform = go.AddComponent<RectTransform>();
            go.AddComponent<CanvasRenderer>();
            go.layer = LayerMask.NameToLayer("UI");

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            PrefabUtility.SaveAsPrefabAsset(go, pathName);
            GameObject.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
        }
    }

}
