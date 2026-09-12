namespace Mochi.Unity.Preview.GUI
{
    /// <summary>
    /// 面板公共契约：状态查询与交互控制。
    /// 生命周期入口收敛在程序集内部的 IPanelLifecycle 契约上，
    /// 外部程序集只能经容器间接驱动生命周期（C2 边界）。
    /// </summary>
    public interface IPanel
    {
        bool IsOpen { get; }
        bool IsVisible { get; }

        /// <summary>
        /// 设置面板是否可交互
        /// </summary>
        /// <param name="enabled">是否可交互</param>
        void SetInteractable(bool enabled);
    }

    /// <summary>
    /// 面板生命周期契约（仅程序集内部可见）。
    /// 与 R3 幂等约定一致：Open/Close 按面板内部打开状态、Show/Hide 按内部可见状态判定，
    /// 同状态重复请求为空操作，不允许绕过状态管理直接触发 OnOpen/OnClose/OnShow/OnHide。
    /// 由 Panel 显式实现该契约，生命周期成员不出现在公共 API 表面；
    /// 接口类型本身为 internal，外部程序集不可见、无法转换或调用（C2 边界）。
    /// </summary>
    internal interface IPanelLifecycle
    {
        void OpenInternal();
        void CloseInternal();
        void ShowInternal();
        void HideInternal();
    }
}
