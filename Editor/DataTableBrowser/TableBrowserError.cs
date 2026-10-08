using System;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 一条浏览器编辑期校验错误:定位到条目与字段,由窗口标红单元格、列错误清单与点击跳转消费。
    /// 校验不阻断保存,错误随每次编辑即时重算。
    /// </summary>
    public sealed class TableBrowserError
    {
        /// <summary>出错条目的键;条目键不可知时为 null。</summary>
        public string EntryId { get; }

        /// <summary>出错字段名;条目级整体错误为 null。</summary>
        public string FieldName { get; }

        /// <summary>错误消息。</summary>
        public string Message { get; }

        /// <summary>
        /// 构造一条浏览器校验错误。
        /// </summary>
        /// <param name="entryId">出错条目的键;不可知时传 null。</param>
        /// <param name="fieldName">出错字段名;条目级整体错误传 null。</param>
        /// <param name="message">错误消息。</param>
        /// <exception cref="ArgumentException">message 为 null 或空。</exception>
        public TableBrowserError(string entryId, string fieldName, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("错误消息不能为空。", nameof(message));
            }

            EntryId = entryId;
            FieldName = fieldName;
            Message = message;
        }

        /// <summary>按 条目/字段/消息 拼接的定位串。</summary>
        public override string ToString()
        {
            string location = "";
            if (EntryId != null)
            {
                location = "条目 " + EntryId + " ";
            }
            if (FieldName != null)
            {
                location += "字段 " + FieldName + " ";
            }
            return location + Message;
        }
    }
}
