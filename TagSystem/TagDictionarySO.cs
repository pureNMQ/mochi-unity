using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mochi.Unity.Tags
{
[CreateAssetMenu(fileName = "TagDefinition", menuName = "")]
public class TagDictionarySO : ScriptableObject
{
    public List<string> tagNames = new List<string>();
    public bool isApply = true;

    public void AddTag(string name)
    {
        if (name.Contains("..") || name[0] == '.' || name[^1] == '.')
        {
            return;
        }

        string hierarchyName = name;

        do
        {
            Debug.Log("添加:" + hierarchyName);

            tagNames.Add(hierarchyName);
            int lastIndex = hierarchyName.LastIndexOf('.');
            if (lastIndex > 0)
            {
                hierarchyName = name.Substring(0, lastIndex);
            }
            else
            {
                break;
            }
        } while (!string.IsNullOrEmpty(hierarchyName) && !tagNames.Contains(hierarchyName));

        tagNames = tagNames.OrderBy(x => x).ToList();
    }

    public void RemoveTag(string name)
    {
        //删除所有子标签
        for (int i = tagNames.Count - 1; i >= 0; i--)
        {
            if (tagNames[i].StartsWith(name))
            {
                tagNames.RemoveAt(i);
            }
        }
    }
}
}
