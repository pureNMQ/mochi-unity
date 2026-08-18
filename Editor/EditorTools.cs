using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEditor.ProjectWindowCallback;

namespace Mochi.Unity.Editor
{
    public static class EditorTools
    {
        public static string GetSelectedPath()
        {
            //无论是否选择文件，都会返回一个DefaultAsset的对象，这个对象路径就是当前打开文件夹的路径
            string selectedPath = AssetDatabase.GetAssetPath(Selection.GetFiltered<DefaultAsset>(SelectionMode.Assets)[0]);

            if (string.IsNullOrEmpty(selectedPath))
            {
                selectedPath = "Assets";
            }

            if (!AssetDatabase.IsValidFolder(selectedPath))
            {
                selectedPath = Path.GetDirectoryName(selectedPath);
            }

            return selectedPath;
        }

        public static string GenerateAssetPathInSelectedFolder(string assetName, string extension = "asset")
        {
            return AssetDatabase.GenerateUniqueAssetPath(GetSelectedPath() + "/" + assetName + "." + extension);
        }

        /// <summary>
        /// 在当前目录创建资产
        /// </summary>
        /// <param name="defaultName">资产默认名称,包含扩展名</param>
        /// <param name="icon">资产图标</param>
        /// <param name="onAssetEndNameEdited">资产命名结束回调,用于实现创建资产的具体实现</param>
        public static void RequestCreateAsset(string defaultName, Texture2D icon, AssetEndNameEdited onAssetEndNameEdited)
        {
            CreateAssetAction createAssetAction = new CreateAssetAction();
            createAssetAction.OnAssetEndNameEdited = onAssetEndNameEdited;

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, createAssetAction, defaultName, icon, null);
        }
    }

    public delegate void AssetEndNameEdited(int instanceId, string pathName, string resourceFile);

    internal class CreateAssetAction : EndNameEditAction
    {
        public AssetEndNameEdited OnAssetEndNameEdited;
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            OnAssetEndNameEdited?.Invoke(instanceId, pathName, resourceFile);
        }
    }
}
