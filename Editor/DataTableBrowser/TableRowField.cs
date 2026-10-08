using System;
using System.Collections.Generic;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>行类型单个字段在浏览器侧的元数据:列渲染、演化整形状与结构类型校验共用。</summary>
    public sealed class TableRowField
    {
        /// <summary>字段当前名,即浏览器列名与保存时的 JSON 键。</summary>
        public string Name { get; }

        /// <summary>字段声明的 CLR 类型;单元格控件与类型校验按此裁决。</summary>
        public Type FieldType { get; }

        /// <summary>经 FormerNameAttribute 标注的旧字段名;读取时回退匹配,保存一律写当前名。</summary>
        public IReadOnlyList<string> FormerNames { get; }

        /// <summary>
        /// 构造字段元数据。
        /// </summary>
        /// <param name="name">字段当前名。</param>
        /// <param name="fieldType">字段 CLR 类型。</param>
        /// <param name="formerNames">旧字段名列表;无标注时传空列表。</param>
        public TableRowField(string name, Type fieldType, IReadOnlyList<string> formerNames)
        {
            Name = name;
            FieldType = fieldType;
            FormerNames = formerNames ?? Array.Empty<string>();
        }
    }
}
