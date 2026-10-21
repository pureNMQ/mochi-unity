namespace Mochi.Unity.GUI
{
    public abstract class InteractivePanel<TData> : InteractivePanel, IDataPanel<TData>
    {
        /// <summary>应用一份呈现数据；面板已打开时再次调用即替换数据，不改变打开状态。</summary>
        public void ApplyData(TData data)
        {
            OnApplyData(data);
        }

        /// <summary>数据应用回调；工厂在激活实例前调用一次，外部重复应用时每次调用一次。</summary>
        protected virtual void OnApplyData(TData data)
        {
        }
    }
}
