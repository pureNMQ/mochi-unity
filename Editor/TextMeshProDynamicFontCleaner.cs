using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor
{
    /// <summary>
    /// 清理 TMP 动态图集字体资产中可再生成的字形与图集数据。
    /// Dynamic 图集会在编辑器里把生成的字形写回 .asset，使字体资产随 UI 文案反复变动并持续膨胀版本库，因此保存前只保留字体配置。
    /// </summary>
    internal static class TextMeshProDynamicFontCleaner
    {
        private const string MenuRoot = "Tools/Mochi/TextMeshPro/";
        private const string CleanAllMenuPath = MenuRoot + "清理动态字体数据";
        private const string SaveHookMenuPath = MenuRoot + "保存时自动清理动态字体数据";

        private const string SaveHookEnabledKey = "Mochi.TextMeshPro.CleanDynamicFontDataOnSave";

        /// <summary>
        /// 保存钩子是否启用，默认为 true。放在 EditorPrefs 以便个别项目保留已生成字形。
        /// </summary>
        internal static bool SaveHookEnabled
        {
            get { return EditorPrefs.GetBool(SaveHookEnabledKey, true); }
            set { EditorPrefs.SetBool(SaveHookEnabledKey, value); }
        }

        [MenuItem(CleanAllMenuPath, false, 100)]
        private static void CleanAllDynamicFontAssetsMenu()
        {
            int cleanedCount = CleanAllDynamicFontAssets();
            Debug.Log($"动态字体数据清理完成，共清理 {cleanedCount} 个动态图集字体资产");
        }

        [MenuItem(SaveHookMenuPath, false, 101)]
        private static void ToggleSaveHook()
        {
            SaveHookEnabled = !SaveHookEnabled;
        }

        [MenuItem(SaveHookMenuPath, true)]
        private static bool ToggleSaveHookValidate()
        {
            Menu.SetChecked(SaveHookMenuPath, SaveHookEnabled);
            return true;
        }

        private static int CleanAllDynamicFontAssets()
        {
            int cleanedCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t: TMP_FontAsset"))
            {
                // 菜单路径不经过保存流程，只有先标脏再 SaveAssets 才会真正写盘
                if (TryCleanDynamicFontAsset(AssetDatabase.GUIDToAssetPath(guid), markDirty: true))
                {
                    cleanedCount++;
                }
            }

            if (cleanedCount > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return cleanedCount;
        }

        internal static bool TryCleanDynamicFontAsset(string path, bool markDirty)
        {
            try
            {
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);

                if (assetType == null || !typeof(TMP_FontAsset).IsAssignableFrom(assetType))
                {
                    return false;
                }

                TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

                if (fontAsset == null || !IsDynamicAtlasMode(fontAsset.atlasPopulationMode))
                {
                    return false;
                }

                fontAsset.ClearFontAssetData(true);

                if (markDirty)
                {
                    EditorUtility.SetDirty(fontAsset);
                }

                return true;
            }
            catch (Exception exception)
            {
                // 单个资产清理失败不能中断整轮保存或整批清理，否则其余资产也无法落盘
                Debug.LogError($"清理动态字体资产失败：{path}\n{exception}");
                return false;
            }
        }

        private static bool IsDynamicAtlasMode(AtlasPopulationMode populationMode)
        {
            if (populationMode == AtlasPopulationMode.Dynamic)
            {
                return true;
            }

#if TMP_ATLAS_MODE_DYNAMIC_OS
            // DynamicOS 自 ugui 2.0.0 的 TMP 起才有，低版本给该包编译时不能引用这个枚举值
            return populationMode == AtlasPopulationMode.DynamicOS;
#else
            return false;
#endif
        }
    }

    internal class TextMeshProDynamicFontSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (!TextMeshProDynamicFontCleaner.SaveHookEnabled)
            {
                return paths;
            }

            foreach (string path in paths)
            {
                TextMeshProDynamicFontCleaner.TryCleanDynamicFontAsset(path, markDirty: false);
            }

            return paths;
        }
    }
}
