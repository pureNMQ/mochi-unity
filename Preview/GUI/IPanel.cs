namespace Mochi.Unity.Preview.GUI
{
    /// <summary>
    /// 面板公共契约：状态查询、交互控制与生命周期入口（幂等）。
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

        /// <summary>
        /// 设置面板是否可交互
        /// </summary>
        /// <param name="enabled"></param>
        void SetInteractable(bool enabled);
    }
}
