using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mochi.DataTable;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 从行类型与表名适配出的浏览器表源,承载磁盘读写、演化整形状与编辑期结构类型校验。
    /// 单文件形状按 {"$table":{...},"rows":[...]} 读写,表头行类型取泛型行类型完整类名。
    /// </summary>
    /// <typeparam name="TRow">行数据类型,必须继承 <see cref="TableRow"/>;新条目模板取其无参构造默认值的全部字段。</typeparam>
    public sealed class TableBrowserSource<TRow> : ITableBrowserSource
        where TRow : TableRow
    {
        private string tableName;
        private string displayName;
        private readonly JObject entryTemplate;

        /// <summary>
        /// 从表名与路径构造表源。
        /// </summary>
        /// <param name="tableName">表名,取自文件表头。</param>
        /// <param name="path">项目相对路径,正斜杠分隔;形状由 <paramref name="storageKind"/> 决定。</param>
        /// <param name="storageKind">磁盘组织形状。</param>
        /// <param name="displayName">浏览器显示名;传 null 时使用表名。</param>
        /// <exception cref="ArgumentNullException">tableName 或 path 为 null。</exception>
        /// <exception cref="ArgumentException">tableName 为空串。</exception>
        public TableBrowserSource(
            string tableName,
            string path,
            TableStorageKind storageKind,
            string displayName = null)
        {
            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentException("表名不能为空。", nameof(tableName));
            }
            this.tableName = tableName;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            StorageKind = storageKind;
            this.displayName = string.IsNullOrEmpty(displayName) ? tableName : displayName;
            entryTemplate = BuildEntryTemplate();
            Fields = BuildFields(entryTemplate);
            EnumFields = CollectEnumFields(typeof(TRow));
        }

        /// <summary>表名。</summary>
        public string TableName => tableName;

        /// <summary>浏览器显示名。</summary>
        public string DisplayName => displayName;

        /// <summary>磁盘组织形状。</summary>
        public TableStorageKind StorageKind { get; }

        /// <summary>项目相对路径,正斜杠分隔。</summary>
        public string Path { get; }

        /// <summary>键字段名。</summary>
        public string KeyFieldName => TableRow.IdFieldName;

        /// <summary>行类型的完整类名;表头信息栏只读展示用。</summary>
        public string RowTypeFullName => typeof(TRow).FullName;

        /// <summary>行类型全部字段的元数据,顺序即浏览器列序与保存时的字段序。</summary>
        public IReadOnlyList<TableRowField> Fields { get; }

        /// <summary>枚举字段元数据;构造时从行类型的公共枚举字段反射生成。</summary>
        public IReadOnlyDictionary<string, string[]> EnumFields { get; }

        /// <summary>从磁盘读取全部行;文件不可读或结构不符时抛出异常。</summary>
        /// <exception cref="InvalidOperationException">文件内容不是声明形状要求的 JSON 结构,或表头与本表源不符。</exception>
        public JArray LoadRows()
        {
            if (StorageKind == TableStorageKind.SingleFile)
            {
                JToken root = JToken.Parse(File.ReadAllText(ToAbsolutePath(Path)));
                if (root.Type != JTokenType.Object)
                {
                    throw new InvalidOperationException($"{Path} 顶层不是 JSON 对象,缺少 {TableHeader.FieldName} 表头。");
                }
                if (!TableHeader.TryParse((JObject)root, out TableHeader header, out string headerError))
                {
                    throw new InvalidOperationException($"{Path} 表头不合法: {headerError}");
                }
                if (header.Table != TableName || header.RowType != typeof(TRow).FullName)
                {
                    throw new InvalidOperationException(
                        $"{Path} 表头({header.Table}/{header.RowType})与本表源({TableName}/{typeof(TRow).FullName})不符。");
                }
                JToken rowsToken = root[TableHeader.RowsFieldName];
                if (rowsToken == null || rowsToken.Type != JTokenType.Array)
                {
                    throw new InvalidOperationException($"{Path} 缺少 {TableHeader.RowsFieldName} 行数组。");
                }
                return (JArray)rowsToken;
            }

            string folder = ToAbsolutePath(Path);
            var rows = new JArray();
            foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(name => name))
            {
                JToken entry = JToken.Parse(File.ReadAllText(file));
                if (entry.Type != JTokenType.Object)
                {
                    throw new InvalidOperationException($"{file} 的内容不是单个条目对象。");
                }
                rows.Add(entry);
            }
            return rows;
        }

        /// <summary>构造覆盖行类型全部字段的新条目模板,键字段为空串,由调用方赋唯一初值。</summary>
        public JObject CreateEntryTemplate()
        {
            return (JObject)entryTemplate.DeepClone();
        }

        /// <summary>
        /// 把行类型演化痕迹落到行数据,供网格以当前形状编辑:当前名缺失的字段先按旧名回退取值,
        /// 仍缺失则按模板补默认值;已从行类型删除的字段不动,保存时由 <see cref="Save"/> 从文件移除。
        /// </summary>
        /// <param name="rows">磁盘原始行数组,原地补齐。</param>
        /// <exception cref="ArgumentNullException">rows 为 null。</exception>
        public void ApplyEvolutionShape(JArray rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }
            foreach (JToken token in rows)
            {
                if (token.Type != JTokenType.Object)
                {
                    continue;
                }
                var row = (JObject)token;
                foreach (TableRowField field in Fields)
                {
                    if (row[field.Name] != null)
                    {
                        continue;
                    }
                    row[field.Name] = ReadShapedValue(row, field);
                }
            }
        }

        /// <summary>
        /// 对行数组执行结构与类型校验:行是 JSON 对象、键非空且唯一、基元字段类型匹配、枚举字段取合法值;
        /// 错误定位到条目与字段。不做跨表引用校验。
        /// </summary>
        /// <param name="rows">当前编辑中的行数组。</param>
        /// <exception cref="ArgumentNullException">rows 为 null。</exception>
        public IReadOnlyList<TableBrowserError> Validate(JArray rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var errors = new List<TableBrowserError>();
            var seenKeys = new HashSet<string>();
            int ordinal = 0;
            foreach (JToken entry in rows)
            {
                ordinal++;
                if (entry.Type != JTokenType.Object)
                {
                    errors.Add(new TableBrowserError(
                        null, null, $"第 {ordinal} 个条目不是 JSON 对象。"));
                    continue;
                }

                var row = (JObject)entry;
                string entryId = ExtractKey(row);
                if (string.IsNullOrEmpty(entryId))
                {
                    errors.Add(new TableBrowserError(
                        null, KeyFieldName, $"第 {ordinal} 个条目缺少键字段 {KeyFieldName} 或其值为空。"));
                }
                else if (!seenKeys.Add(entryId))
                {
                    errors.Add(new TableBrowserError(
                        entryId, KeyFieldName, $"与其他条目的键 {KeyFieldName} 重复。"));
                }

                foreach (TableRowField field in Fields)
                {
                    JToken value = row[field.Name];
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        continue;
                    }
                    EnumFields.TryGetValue(field.Name, out string[] enumNames);
                    string problem = CheckFieldValue(field, value, enumNames);
                    if (problem != null)
                    {
                        errors.Add(new TableBrowserError(entryId, field.Name, problem));
                    }
                }
            }
            return errors;
        }

        /// <summary>从行对象提取键的字符串形式;键缺失或为 null 时返回 null。</summary>
        public string ExtractKey(JObject row)
        {
            JToken token = row[KeyFieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            return token.ToString();
        }

        /// <summary>
        /// 修改表名,后续 <see cref="Save"/> 把新名写入 $table.table;文件名与路径不变。
        /// 投产表改名须同步代码注册表的 TableName 声明,装载期表名交叉验证兜底。
        /// </summary>
        /// <param name="newTableName">新表名。</param>
        /// <exception cref="ArgumentException">newTableName 为 null 或空。</exception>
        public void RenameTable(string newTableName)
        {
            if (string.IsNullOrEmpty(newTableName))
            {
                throw new ArgumentException("表名不能为空。", nameof(newTableName));
            }
            //显示名默认跟随表名,仅在未被定制时同步;改名提交即置脏,由保存写入表头
            if (displayName == tableName)
            {
                displayName = newTableName;
            }
            tableName = newTableName;
        }

        /// <summary>
        /// 按存储形状把行数组写回磁盘:写入前按行类型当前字段整形状,仅保留当前名字段
        /// (旧名值写入当前名下,已删字段随之从文件移除,缺失字段按模板补默认值),
        /// 非对象行原样保留。SingleFile 以当前表头加 rows 重写单文件;
        /// FolderPerEntry 逐条目写 {键}.json,并删除键已不存在的条目文件(经 AssetDatabase 连 .meta 一并删除)。
        /// </summary>
        public void Save(JArray rows)
        {
            JArray shaped = ShapeRowsForSave(rows);
            if (StorageKind == TableStorageKind.SingleFile)
            {
                var header = new TableHeader(TableName, typeof(TRow).FullName, TableHeader.CurrentFormatVersion);
                var root = new JObject
                {
                    [TableHeader.FieldName] = header.ToJson(),
                    [TableHeader.RowsFieldName] = shaped,
                };
                File.WriteAllText(ToAbsolutePath(Path), root.ToString(Formatting.Indented));
                return;
            }

            var representedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken entry in shaped)
            {
                if (entry.Type != JTokenType.Object)
                {
                    continue;
                }
                string key = ExtractKey((JObject)entry);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                string fileName = key + ".json";
                representedFiles.Add(fileName);
                File.WriteAllText(
                    ToAbsolutePath(System.IO.Path.Combine(Path, fileName)),
                    ((JObject)entry).ToString(Formatting.Indented));
            }

            foreach (string file in Directory.GetFiles(ToAbsolutePath(Path), "*.json"))
            {
                if (!representedFiles.Contains(System.IO.Path.GetFileName(file)))
                {
                    //经 AssetDatabase 删除以同步清掉 .meta,直接 File.Delete 会留下孤儿 meta
                    AssetDatabase.DeleteAsset(ToAssetDatabasePath(System.IO.Path.Combine(Path,
                        System.IO.Path.GetFileName(file))));
                }
            }
        }

        /// <summary>读取一个字段在整形状后的取值:当前名优先,旧名回退,仍无则取模板默认值。</summary>
        private JToken ReadShapedValue(JObject row, TableRowField field)
        {
            foreach (string formerName in field.FormerNames)
            {
                JToken old = row[formerName];
                if (old != null)
                {
                    return old.DeepClone();
                }
            }
            return entryTemplate[field.Name].DeepClone();
        }

        private JArray ShapeRowsForSave(JArray rows)
        {
            var shaped = new JArray();
            foreach (JToken entry in rows)
            {
                if (entry.Type != JTokenType.Object)
                {
                    //非对象行无法映射到行类型字段,原样保留交给结构与类型校验指出
                    shaped.Add(entry);
                    continue;
                }
                var row = (JObject)entry;
                var shapedRow = new JObject();
                foreach (TableRowField field in Fields)
                {
                    JToken value = row[field.Name] ?? ReadShapedValue(row, field);
                    shapedRow[field.Name] = value.DeepClone();
                }
                shaped.Add(shapedRow);
            }
            return shaped;
        }

        private static JObject BuildEntryTemplate()
        {
            //枚举按字符串写入模板,与项目内既有表文件的字符串写法保持一致;
            //反序列化对字符串与数字均兼容,保存沿编辑后的原样写出
            var serializer = new JsonSerializer();
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Converters.Add(new DataRefJsonConverter());

            TRow instance = Activator.CreateInstance<TRow>();
            FillNullReferenceFields(instance);
            JObject template = JObject.FromObject(instance, serializer);
            template[TableRow.IdFieldName] = "";
            return template;
        }

        /// <summary>
        /// 把未赋值的引用类型可编辑字段换成空值载体(null 字符串换空串、null List 换空列表)。
        /// null 经 JObject.FromObject 变成 JTokenType.Null,而浏览器字段编辑器对 null 只读,
        /// 新条目模板若保留 null 会出现整字段不可编辑。
        /// </summary>
        private static void FillNullReferenceFields(object instance)
        {
            Type type = instance.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(instance) != null)
                {
                    continue;
                }
                if (field.FieldType == typeof(string))
                {
                    field.SetValue(instance, "");
                }
                else if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    field.SetValue(instance, Activator.CreateInstance(field.FieldType));
                }
            }
        }

        private static IReadOnlyList<TableRowField> BuildFields(JObject template)
        {
            var fieldsByName = new Dictionary<string, FieldInfo>();
            foreach (FieldInfo field in typeof(TRow).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                fieldsByName[field.Name] = field;
            }

            var fields = new List<TableRowField>();
            //模板属性序即序列化输出序:基类字段在前,列序与保存字段序跟随,两侧不会漂移
            foreach (JProperty property in template.Properties())
            {
                if (fieldsByName.Remove(property.Name, out FieldInfo field))
                {
                    fields.Add(CreateFieldMetadata(field));
                }
            }
            foreach (FieldInfo field in fieldsByName.Values)
            {
                fields.Add(CreateFieldMetadata(field));
            }
            return fields;
        }

        private static TableRowField CreateFieldMetadata(FieldInfo field)
        {
            var formerNames = (FormerNameAttribute)field.GetCustomAttribute(
                typeof(FormerNameAttribute), false);
            string[] names = formerNames != null ? formerNames.Names.ToArray() : Array.Empty<string>();
            return new TableRowField(field.Name, field.FieldType, names);
        }

        private static IReadOnlyDictionary<string, string[]> CollectEnumFields(Type rowType)
        {
            var map = new Dictionary<string, string[]>();
            foreach (FieldInfo field in rowType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType.IsEnum)
                {
                    map[field.Name] = Enum.GetNames(field.FieldType);
                }
            }
            return map;
        }

        /// <summary>校验单个字段值与声明类型是否相容;相容返回 null,否则返回问题描述。</summary>
        private static string CheckFieldValue(TableRowField field, JToken value, string[] enumNames)
        {
            Type type = field.FieldType;
            if (type == typeof(string) || IsDataRef(type))
            {
                return value.Type == JTokenType.String
                    ? null
                    : "类型不匹配: 期望字符串,实际" + DescribeToken(value.Type) + "。";
            }
            if (type == typeof(bool))
            {
                return value.Type == JTokenType.Boolean
                    ? null
                    : "类型不匹配: 期望布尔,实际" + DescribeToken(value.Type) + "。";
            }
            if (type == typeof(int) || type == typeof(long))
            {
                return value.Type == JTokenType.Integer
                    ? null
                    : "类型不匹配: 期望整数,实际" + DescribeToken(value.Type) + "。";
            }
            if (type == typeof(float) || type == typeof(double))
            {
                return value.Type == JTokenType.Integer || value.Type == JTokenType.Float
                    ? null
                    : "类型不匹配: 期望数值,实际" + DescribeToken(value.Type) + "。";
            }
            if (type.IsEnum)
            {
                if (value.Type != JTokenType.String)
                {
                    return "类型不匹配: 期望枚举名(字符串),实际" + DescribeToken(value.Type) + "。";
                }
                return enumNames != null &&
                    enumNames.Contains(value.ToString(), StringComparer.OrdinalIgnoreCase)
                    ? null
                    : "非法枚举名 " + value + ",不在合法值列表内。";
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return value.Type == JTokenType.Array
                    ? null
                    : "类型不匹配: 期望数组,实际" + DescribeToken(value.Type) + "。";
            }
            //复合类型不做单元格级类型校验,装载管线的演化容错负责其成员级兜底
            return null;
        }

        private static bool IsDataRef(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataRef<>);
        }

        private static string DescribeToken(JTokenType type)
        {
            switch (type)
            {
                case JTokenType.String: return "字符串";
                case JTokenType.Boolean: return "布尔";
                case JTokenType.Integer: return "整数";
                case JTokenType.Float: return "浮点数";
                case JTokenType.Array: return "数组";
                case JTokenType.Object: return "对象";
                default: return type.ToString();
            }
        }

        internal static string ToAbsolutePath(string projectRelativePath)
        {
            string projectRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));
            return System.IO.Path.Combine(projectRoot, projectRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        private static string ToAssetDatabasePath(string projectRelativePath)
        {
            return projectRelativePath.Replace('\\', '/');
        }
    }
}
