using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Mochi.Unity.Tags
{
[System.Serializable]
public struct TagContainer
{
    [SerializeField] public List<string> presetTags;
    [SerializeField] private List<int> runtimeTags;

    public void Init()
    {
        runtimeTags = new List<int>();
        foreach (string tagName in presetTags)
        {
            int index = TagManager.Instance.GetTagIndexByName(tagName);
            if (index != -1)
            {
                runtimeTags.Add(index);
            }
            runtimeTags = runtimeTags.OrderBy(x => x).ToList();
        }
    }

    public bool HasTag(int tagIndex)
    {
        return TagManager.Instance.HasTag(runtimeTags, tagIndex);
    }

    public bool HasTag(string tagName)
    {
        int tagIndex = TagManager.Instance.GetTagIndexByName(tagName);
        return HasTag(tagIndex);
    }

    public bool HasTagExact(int tagIndex)
    {
        return TagManager.Instance.HasTagExact(runtimeTags, tagIndex);
    }

    public bool HasTagExact(string tagName)
    {
        int tagIndex = TagManager.Instance.GetTagIndexByName(tagName);
        return HasTagExact(tagIndex);
    }

    public void Insert(int tagIndex)
    {
        int pos = TagManager.Instance.FindTag(runtimeTags, tagIndex);
        if (pos < 0 && TagManager.Instance.HasTagDefine(tagIndex))
        {
            runtimeTags.Insert(~pos, tagIndex);
        }
    }

    public void Insert(string tagName)
    {
        int tagIndex = TagManager.Instance.GetTagIndexByName(tagName);
        Insert(tagIndex);
    }

    public void Remove(int tagIndex)
    {
        if (!TagManager.Instance.HasTagDefine(tagIndex)) return;
        int pos = TagManager.Instance.FindTag(runtimeTags, tagIndex);
        if (pos < 0)
        {
            pos = ~pos;
        }

        int range = TagManager.Instance.GetTagRange(tagIndex);
        int count = 0;
        while (pos + count < runtimeTags.Count && runtimeTags[pos + count] <= range)
        {
            count++;
        }

        runtimeTags.RemoveRange(pos, count);
    }

    public void Remove(string tagName)
    {
        int tagIndex = TagManager.Instance.GetTagIndexByName(tagName);
        Remove(tagIndex);
    }

    public void RemoveExact(int tagIndex)
    {
        if (!TagManager.Instance.HasTagDefine(tagIndex)) return;
        int pos = TagManager.Instance.FindTag(runtimeTags, tagIndex);

        if (pos >= 0)
        {
            runtimeTags.RemoveAt(pos);
        }
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder(base.ToString() + " Count:" + presetTags.Count + "\n");
        foreach (string tag in presetTags)
        {
            sb.AppendLine(tag.ToString());
        }

        if (runtimeTags != null)
        {
            foreach (int tag in runtimeTags)
            {
                sb.Append($"{tag},");
            }
        }

        return sb.ToString();
    }
}
}
