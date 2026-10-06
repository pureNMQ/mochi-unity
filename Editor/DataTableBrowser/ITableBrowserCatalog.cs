using System.Collections.Generic;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 表目录:向浏览器提供本项目全部可编辑表。
    /// 由各项目实现,浏览器窗口经 TypeCache 自动发现实现类,无需注册调用。
    /// </summary>
    public interface ITableBrowserCatalog
    {
        /// <summary>创建全部表源;每次重载时调用,不应返回缓存实例。</summary>
        IEnumerable<ITableBrowserSource> CreateSources();
    }
}
