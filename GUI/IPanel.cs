namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 面板公共契约：状态查询与生命周期入口（幂等）。
    /// </summary>
    public interface IPanel
    {
        bool IsOpen { get; }
        bool IsVisible { get; }

        void Show();

        void Hide();

        void Close();

        /// <summary>显示面板，不播放过渡动画</summary>
        void ShowImmediate();

        /// <summary>隐藏面板，不播放过渡动画</summary>
        void HideImmediate();

        /// <summary>关闭面板，不播退场过渡，立即释放并销毁实例</summary>
        void CloseImmediate();
    }
}
