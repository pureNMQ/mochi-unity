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

        /// <summary>
        /// 设置面板是否可交互
        /// </summary>
        /// <param name="enabled">是否可交互</param>
        void SetInteractable(bool enabled);
    }
}
