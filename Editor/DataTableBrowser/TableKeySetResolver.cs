using System.Collections.Generic;
using Mochi.DataTable;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 以表名到键集合的映射为底座的编辑期跨表键解析器。
    /// 键集合是调用方构建时的快照;底层数据变化后须重建,才能反映最新编辑态。
    /// </summary>
    public sealed class TableKeySetResolver : IEditTimeKeyResolver
    {
        private readonly Dictionary<string, HashSet<string>> keys =
            new Dictionary<string, HashSet<string>>();

        /// <summary>登记一个键。</summary>
        /// <param name="tableName">所属表名。</param>
        /// <param name="key">键的字符串形式。</param>
        public void AddKey(string tableName, string key)
        {
            if (!keys.TryGetValue(tableName, out HashSet<string> set))
            {
                set = new HashSet<string>();
                keys[tableName] = set;
            }
            set.Add(key);
        }

        /// <summary>查询表 tableName 中是否存在键 key。</summary>
        public bool ContainsKey(string tableName, string key)
        {
            return keys.TryGetValue(tableName, out HashSet<string> set) && set.Contains(key);
        }
    }
}
