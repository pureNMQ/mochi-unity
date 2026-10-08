using System.Collections.Generic;
using Mochi.DataTable;
using Newtonsoft.Json.Linq;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>表文件在磁盘上的组织形状。</summary>
    public enum TableStorageKind
    {
        /// <summary>单个 JSON 文件:顶层为 {"$table":{...},"rows":[...]} 表对象。</summary>
        SingleFile,

        /// <summary>一个文件夹,每个条目一个 JSON 文件,文件名(不含扩展名)即条目键。</summary>
        FolderPerEntry
    }

    /// <summary>
    /// 浏览器可编辑的一张表的非泛型门面,由 <see cref="TableBrowserSource{TRow}"/> 从表头与行类型适配而来。
    /// 浏览器窗口只依赖本接口与 Mochi 核心公开类型,不接触各表的具体行类型。
    /// </summary>
    public interface ITableBrowserSource
    {
        /// <summary>表名,跨表引用按此名字寻址。</summary>
        string TableName { get; }

        /// <summary>在浏览器列表中显示的名称。</summary>
        string DisplayName { get; }

        /// <summary>磁盘组织形状。</summary>
        TableStorageKind StorageKind { get; }

        /// <summary>项目相对路径,正斜杠分隔;SingleFile 指向表 json 文件,FolderPerEntry 指向条目文件夹。</summary>
        string Path { get; }

        /// <summary>键字段名。</summary>
        string KeyFieldName { get; }

        /// <summary>跨表引用声明;供编辑 UI 渲染引用字段的下拉选项,内容来自表声明。</summary>
        IReadOnlyList<TableReference> References { get; }

        /// <summary>枚举字段元数据:字段名到合法枚举名数组的映射;供编辑 UI 渲染枚举 combobox。</summary>
        IReadOnlyDictionary<string, string[]> EnumFields { get; }

        /// <summary>从磁盘读取全部行;文件不可读或结构不符时抛出异常,由调用方呈现。</summary>
        JArray LoadRows();

        /// <summary>构造一个覆盖行类型全部字段的新条目模板,键字段已被赋为可用初值。</summary>
        JObject CreateEntryTemplate();

        /// <summary>从行对象提取键的字符串形式;键缺失时返回 null。</summary>
        string ExtractKey(JObject row);

        /// <summary>对行数组执行编辑期校验(结构、跨表引用、领域规则)。</summary>
        IReadOnlyList<TableEditError> Validate(JArray rows, IEditTimeKeyResolver keyResolver);

        /// <summary>
        /// 按存储形状把行数组写回磁盘:SingleFile 以表头加 rows 重写单文件;
        /// FolderPerEntry 逐条目写 {键}.json 并删除不再存在的条目文件。
        /// </summary>
        void Save(JArray rows);
    }
}
