using System;
using System.Collections.Generic;
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
        /// <exception cref="ArgumentNullException">header 为 null。</exception>
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

            Type sourceType = typeof(TableBrowserSource<>).MakeGenericType(rowType);
            return (ITableBrowserSource)Activator.CreateInstance(
                sourceType, header.Table, path, TableStorageKind.SingleFile, null);
        }

        /// <summary>
        /// 列出全部可直接实例化的 TableRow 派生类型,按完整类名排序;
        /// 供新建表入口与表头失配重选下拉枚举候选项。
        /// </summary>
        public static IReadOnlyList<Type> ListRowTypes()
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<TableRow>())
            {
                if (!type.IsAbstract && !type.IsGenericTypeDefinition)
                {
                    types.Add(type);
                }
            }
            types.Sort((left, right) => string.Compare(
                left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase));
            return types;
        }

        /// <summary>
        /// 在 TableRow 派生类型中按完整类名定位行类型;找不到返回 null,
        /// 由打开流程呈现失配与重选,由双钩子检查器判定失配。
        /// </summary>
        public static Type FindRowType(string rowTypeFullName)
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
