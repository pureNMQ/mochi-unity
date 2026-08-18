using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Mochi;
//using YooAsset;

namespace Mochi.Unity.Tags
{
public sealed class TagManager : Singleton<TagManager>
{
    public List<TagDefine> TagDefines => tagDefines;

    private TagDictionarySO tagDictionary;
    private List<TagDefine> tagDefines;
    private Dictionary<string, TagDefine> nameDic;

    public TagManager()
    {
        //TODO TagDictionarySO读取
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            //tagDictionary = YooAssets.LoadAssetSync("Assets/GameRes/Config/TagDictionary.asset").AssetObject as TagDictionarySO;
        }
        else
        {
            tagDictionary = UnityEditor.AssetDatabase.LoadAssetAtPath<TagDictionarySO>("Assets/GameRes/Config/TagDictionary.asset");
        }

#else
        //tagDictionary = YooAssets.LoadAssetSync("Assets/GameRes/Config/TagDictionary.asset").AssetObject as TagDictionarySO;
#endif

        tagDefines = new List<TagDefine>();
        nameDic = new Dictionary<string, TagDefine>();

        if (tagDictionary == null) return;

        foreach (var item in tagDictionary.tagNames)
        {
            AddTagDefine(item);
        }

        for (int i = tagDefines.Count - 1; i >= 0; i--)
        {
            if (tagDefines[i].children.Count > 0)
            {
                tagDefines[i].range = tagDefines[tagDefines[i].children[^1]].range;
            }
        }

        Debug.Log("标签系统初始化");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTag(List<int> tagList, int index)
    {
        if (index >= TagDefines.Count || index < 0) return false;

        int range = tagDefines[index].range;
        int low = 0;
        int hight = tagList.Count - 1;
        while (low <= hight)
        {
            int mid = (low + hight) / 2;
            if (tagList[mid] >= index && tagList[mid] <= range)
            {
                return true;
            }
            else if (tagList[mid] < index)
            {
                low = mid + 1;
            }
            else if (tagList[mid] > range)
            {
                hight = mid - 1;
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTagExact(List<int> tagList, int index)
    {
        if (index >= TagDefines.Count || index < 0) return false;
        return FindTag(tagList, index) >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int FindTag(List<int> tagList, int index)
    {
        int low = 0;
        int hight = tagList.Count - 1;
        int mid = 0;
        while (low <= hight)
        {
            mid = (low + hight) / 2;

            if (tagList[mid] == index)
            {
                return mid;
            }
            else if (tagList[mid] < index)
            {
                low = mid + 1;
            }
            else
            {
                hight = mid - 1;
            }
        }

        return ~low;
    }

    /// <summary>
    /// 获取标签索引，如果结果为-1,表示该标签未定义
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public int GetTagIndexByName(string name)
    {
        if (nameDic.TryGetValue(name, out var tagDefine))
        {
            return tagDefine.index;
        }
        else
        {
            return -1;
        }
    }

    public string GetTagNameByIndex(int index)
    {
        if (index < 0 || index >= tagDefines.Count) return null;
        return tagDefines[index].name;
    }
    private void AddTagDefine(string name)
    {
        //字符串为空的情况不处理
        if (string.IsNullOrEmpty(name)) return;

        if (HasTagDefine(name)) return;

        int lastIndex = name.LastIndexOf('.');
        string parentName = null;
        if (lastIndex >= 0)
        {
            parentName = name.Substring(0, lastIndex);
        }

        int index = tagDefines.Count;
        TagDefine tagDefine;
        if (HasTagDefine(parentName))
        {
            TagDefine parent = GetTagDefine(parentName);
            int[] hierarchy = new int[parent.hierarchy.Length + 1];

            Array.Copy(parent.hierarchy, hierarchy, parent.hierarchy.Length);
            hierarchy[^1] = index;
            tagDefine = new TagDefine(name, index, hierarchy);

            parent.AddChildren(index);
        }
        else
        {
            tagDefine = new TagDefine(name, index, new int[index]);
        }

        tagDefines.Add(tagDefine);
        nameDic.Add(name, tagDefine);
    }

    private TagDefine GetTagDefine(string parentName)
    {
        if (HasTagDefine(parentName))
        {
            return nameDic[parentName];
        }
        else
        {
            return null;
        }
    }

    public bool HasTagDefine(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        else
        {
            return nameDic.ContainsKey(name);
        }
    }

    public bool HasTagDefine(int index)
    {
        return index >= 0 && index < tagDefines.Count;
    }

    public int GetTagRange(int tagIndex)
    {
        if (HasTagDefine(tagIndex))
        {
            return tagDefines[tagIndex].range;
        }
        else
        {
            return -1;
        }
    }

    public int GetTagIndex(string name)
    {
        if (nameDic.TryGetValue(name, out var tagDefine))
        {
            return tagDefine.index;
        }
        else
        {
            return -1;
        }
    }

    public void DebugPrint()
    {
        foreach (var item in tagDefines)
        {
            Debug.Log($"名称:{item.name}\t编号:{item.index}\t高位:{item.range}");
        }
    }
}
}
