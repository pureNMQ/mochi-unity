using System.Collections.Generic;
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
    /// 浏览器窗口只依赖本接口与 Mochi 核心公开类型,不接触各表的具体行类型;
    /// 结构与类型校验按 <see cref="Fields"/> 反射元数据执行,不做跨表引用校验。
    /// </summary>
    public interface ITableBrowserSource
    {
        /// <summary>表名,即数据表文件表头声明的名字。</summary>
        string TableName { get; }

        /// <summary>在浏览器列表中显示的名称。</summary>
        string DisplayName { get; }

        /// <summary>磁盘组织形状。</summary>
        TableStorageKind StorageKind { get; }

        /// <summary>项目相对路径,正斜杠分隔;SingleFile 指向表 json 文件,FolderPerEntry 指向条目文件夹。</summary>
        string Path { get; }

        /// <summary>键字段名。</summary>
        string KeyFieldName { get; }

        /// <summary>行类型全部字段的元数据,顺序即浏览器列序与保存时的字段序。</summary>
        IReadOnlyList<TableRowField> Fields { get; }

        /// <summary>枚举字段元数据:字段名到合法枚举名数组的映射;供编辑 UI 渲染枚举 combobox。</summary>
        IReadOnlyDictionary<string, string[]> EnumFields { get; }

        /// <summary>从磁盘读取全部行;文件不可读或结构不符时抛出异常,由调用方呈现。</summary>
        JArray LoadRows();

        /// <summary>构造一个覆盖行类型全部字段的新条目模板,键字段已被赋为可用初值。</summary>
        JObject CreateEntryTemplate();

        /// <summary>
        /// 把行类型演化痕迹落到行数据,供网格以当前形状编辑:当前名缺失的字段先按
        /// <see cref="TableRowField.FormerNames"/> 回退读取旧名值,仍缺失则按模板补默认值。
        /// 已从行类型删除的字段不动,保存时由 <see cref="Save"/> 从文件移除。
        /// </summary>
        /// <param name="rows">磁盘原始行数组,原地补齐。</param>
        void ApplyEvolutionShape(JArray rows);

        /// <summary>
        /// 对行数组执行结构与类型校验:行是 JSON 对象、键非空且唯一、基元字段类型匹配、枚举字段取合法值;
        /// 错误定位到条目与字段。不做跨表引用校验。
        /// </summary>
        /// <param name="rows">当前编辑中的行数组。</param>
        IReadOnlyList<TableBrowserError> Validate(JArray rows);

        /// <summary>从行对象提取键的字符串形式;键缺失时返回 null。</summary>
        string ExtractKey(JObject row);

        /// <summary>
        /// 按存储形状把行数组写回磁盘:写入前按行类型当前字段整形状,仅保留当前名字段
        /// (旧名值写入当前名下,已删字段随之从文件移除,缺失字段按模板补默认值),
        /// 非对象行原样保留。SingleFile 以当前表头加 rows 重写单文件;
        /// FolderPerEntry 逐条目写 {键}.json,并删除键已不存在的条目文件(经 AssetDatabase 连 .meta 一并删除)。
        /// </summary>
        void Save(JArray rows);
    }
}
