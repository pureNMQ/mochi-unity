using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mochi.DataTable;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 从泛型表声明适配出的浏览器表源,承载磁盘读写与编辑期校验。
    /// </summary>
    /// <typeparam name="TKey">表键类型。</typeparam>
    /// <typeparam name="TValue">行数据类型;新条目模板取其无参构造默认值的全部字段。</typeparam>
    public sealed class TableBrowserSource<TKey, TValue> : ITableBrowserSource
    {
        private readonly TableDefinition<TKey, TValue> definition;
        private readonly string displayName;

        /// <summary>
        /// 从表声明构造表源。
        /// </summary>
        /// <param name="definition">表声明。</param>
        /// <param name="path">项目相对路径,正斜杠分隔;形状由 <paramref name="storageKind"/> 决定。</param>
        /// <param name="storageKind">磁盘组织形状。</param>
        /// <param name="displayName">浏览器显示名;传 null 时使用表名。</param>
        /// <exception cref="ArgumentNullException">definition 或 path 为 null。</exception>
        public TableBrowserSource(
            TableDefinition<TKey, TValue> definition,
            string path,
            TableStorageKind storageKind,
            string displayName = null)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            StorageKind = storageKind;
            this.displayName = string.IsNullOrEmpty(displayName) ? definition.TableName : displayName;
        }

        /// <summary>表名。</summary>
        public string TableName => definition.TableName;

        /// <summary>浏览器显示名。</summary>
        public string DisplayName => displayName;

        /// <summary>磁盘组织形状。</summary>
        public TableStorageKind StorageKind { get; }

        /// <summary>项目相对路径,正斜杠分隔。</summary>
        public string Path { get; }

        /// <summary>键字段名。</summary>
        public string KeyFieldName => definition.KeyFieldName;

        /// <summary>从磁盘读取全部行;文件不可读或结构不符时抛出异常。</summary>
        /// <exception cref="InvalidOperationException">文件内容不是声明形状要求的 JSON 结构。</exception>
        public JArray LoadRows()
        {
            if (StorageKind == TableStorageKind.SingleFileArray)
            {
                JToken root = JToken.Parse(File.ReadAllText(ToAbsolutePath(Path)));
                if (root.Type != JTokenType.Array)
                {
                    throw new InvalidOperationException($"{Path} 的顶层不是行对象数组。");
                }
                return (JArray)root;
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
            //枚举按字符串写入模板,与项目内既有表文件的字符串写法保持一致;
            //反序列化对字符串与数字均兼容,保存沿编辑后的原样写出。
            var serializer = new JsonSerializer();
            serializer.Converters.Add(new StringEnumConverter());

            TValue instance = Activator.CreateInstance<TValue>();
            JObject template = JObject.FromObject(instance, serializer);
            template[KeyFieldName] = "";
            return template;
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

        /// <summary>对行数组执行编辑期校验(结构、跨表引用、领域规则)。</summary>
        public IReadOnlyList<TableEditError> Validate(JArray rows, IEditTimeKeyResolver keyResolver)
        {
            return TableEditValidator.ValidateTable(definition, rows, keyResolver);
        }

        /// <summary>
        /// 按存储形状写回磁盘:SingleFileArray 重写单文件;
        /// FolderPerEntry 逐条目写 {键}.json,并删除键已不存在的条目文件(经 AssetDatabase 连 .meta 一并删除)。
        /// </summary>
        public void Save(JArray rows)
        {
            if (StorageKind == TableStorageKind.SingleFileArray)
            {
                File.WriteAllText(ToAbsolutePath(Path), rows.ToString(Formatting.Indented));
                return;
            }

            var representedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken entry in rows)
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

        private static string ToAbsolutePath(string projectRelativePath)
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
