using System.Collections;
using System.Collections.Generic;

namespace Mochi.Unity.Tags
{
public class TagDefine
{
    public readonly string name;
    public readonly int index;
    //高位编号边界
    public int range;
    public readonly int[] hierarchy;

    public List<int> children;

    public TagDefine(string name, int index, int[] hierarchy)
    {
        this.name = name;
        this.index = index;

        range = index;

        this.hierarchy = hierarchy;

        children = new List<int>();
    }


    public void AddChildren(int child)
    {
        range = child;
        children.Add(child);
    }

    public void SetHightIndex(int index)
    {
        range = index;
    }

    public void Freeze()
    {

    }
}
}
