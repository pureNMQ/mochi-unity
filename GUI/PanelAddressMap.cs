using System;
using System.Collections.Generic;

namespace Mochi.Unity.GUI
{
    /// <summary>弹窗结果：区分「玩家作出选择」与「被外部路径关闭」两种收场。</summary>
    public readonly struct ModalResult<TResult>
    {
        private ModalResult(bool hasResult, TResult result)
        {
            HasResult = hasResult;
            Result = result;
        }

        /// <summary>按「是否带结果」构造，供服务在结果擦除为 object 后回填。</summary>
        internal static ModalResult<TResult> Create(bool hasResult, TResult result)
        {
            return new ModalResult<TResult>(hasResult, result);
        }

        /// <summary>真表示玩家以结果语义关闭了弹窗；假表示取消或外部关闭。</summary>
        public bool HasResult { get; }

        /// <summary>玩家选择的结果；<see cref="HasResult"/> 为假时无意义。</summary>
        public TResult Result { get; }

        /// <summary>构造带结果的成功收场。</summary>
        public static ModalResult<TResult> With(TResult result)
        {
            return new ModalResult<TResult>(true, result);
        }

        /// <summary>构造取消收场。</summary>
        public static ModalResult<TResult> Cancelled()
        {
            return new ModalResult<TResult>(false, default);
        }
    }

    /// <summary>面板资源地址映射：类型到 Addressables 地址；未登记的类型回落为类型名。</summary>
    public sealed class PanelAddressMap
    {
        private readonly Dictionary<Type, string> addresses = new Dictionary<Type, string>();

        /// <summary>登记面板类型的资源地址，返回自身以便连续登记。</summary>
        public PanelAddressMap Map<TPanel>(string address) where TPanel : Panel
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("Panel address must not be empty.", nameof(address));
            }

            addresses[typeof(TPanel)] = address;
            return this;
        }

        /// <summary>按类型取资源地址。</summary>
        public bool TryGetAddress(Type panelType, out string address)
        {
            if (panelType == null)
            {
                throw new ArgumentNullException(nameof(panelType));
            }

            return addresses.TryGetValue(panelType, out address);
        }
    }
}
