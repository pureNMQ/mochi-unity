namespace Mochi.Unity.GUI
{
    // C# 单继承下 Panel<TData> 与 InteractivePanel<TData> 没有共同的泛型祖先可承载带类型数据的通道,
    // 通道只能作为契约由两条继承线各自实现,调用方才不需要按基类分型。
    // 接口必须公开:公开泛型入口的约束不能引用同程序集内部类型(CS0703),且跨程序集的调用方也要能通过约束校验。
    public interface IDataPanel<TData>
    {
        /// <summary>应用一份呈现数据，面板打开后可再次调用以替换当前数据。</summary>
        /// <remarks>
        /// 数据是每次呈现的输入而不是实例的构造参数：面板实例只创建一次，数据可能随每次呈现变更，
        /// 因此该通道必须可重复调用，且调用本身不得改变打开状态或触发显示与关闭生命周期。
        /// 实例级准备（资源、依赖）仍归 <see cref="Panel"/> 的准备通道，与数据交付是两件事。
        /// </remarks>
        void ApplyData(TData data);
    }
}
