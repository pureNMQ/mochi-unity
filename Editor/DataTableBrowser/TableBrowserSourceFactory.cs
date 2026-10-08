using System;
using Mochi.DataTable;
using UnityEditor;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 按文件表头构造浏览器表源:行类型经表头 rowType 完整类名在 TableRow 派生类型中解析,
    /// 文件即声明,浏览器不依赖任何代码侧目录。
    /// </summary>
    public static class TableBrowserSourceFactory
    {
        /// <summary>
        /// 从表头构造表源。
        /// </summary>
        /// <param name="header">文件表头。</param>
        /// <param name="path">表文件的项目相对路径,正斜杠分隔。</param>
        /// <exception cref="InvalidOperationException">表头 rowType 不指向任何 TableRow 派生类型。</exception>
        public static ITableBrowserSource CreateFromHeader(TableHeader header, string path)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }
            Type rowType = FindRowType(header.RowType);
            if (rowType == null)
            {
                throw new InvalidOperationException(
                    "表头行类型 " + header.RowType + " 不是已编译的 TableRow 派生类型。");
            }

            Type definitionType = typeof(TableDefinition<>).MakeGenericType(rowType);
            object definition = Activator.CreateInstance(
                definitionType, header.Table, Array.Empty<string>());
            Type sourceType = typeof(TableBrowserSource<>).MakeGenericType(rowType);
            return (ITableBrowserSource)Activator.CreateInstance(
                sourceType, definition, path, TableStorageKind.SingleFile, null);
        }

        /// <summary>在 TableRow 派生类型中按完整类名定位行类型;找不到返回 null,由调用方裁决失配。</summary>
        private static Type FindRowType(string rowTypeFullName)
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<TableRow>())
            {
                if (type.FullName == rowTypeFullName)
                {
                    return type;
                }
            }
            return null;
        }
    }
}
