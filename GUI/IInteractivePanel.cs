namespace Mochi.Unity.GUI
{
    /// <summary>
    /// 可交互面板契约：在面板生命周期之上增加交互开关，面板栈与焦点交接只接受这一层类型。
    /// </summary>
    public interface IInteractivePanel : IPanel
    {
        /// <summary>切换面板自身的指针与导航可用性；关闭时其上的控件不收射线也不接收输入。</summary>
        void SetInteractable(bool enabled);
    }
}
