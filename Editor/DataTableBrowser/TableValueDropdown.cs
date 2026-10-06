using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;

namespace Mochi.Unity.Editor.DataTableBrowser
{
    /// <summary>
    /// 可搜索取值下拉:以 AdvancedDropdown 承载枚举名/引用 id 选项,
    /// 自带搜索框、键盘导航与独立浮动弹窗;enabled=false 的项仅展示不可选,
    /// 选中经回调交付取值。
    /// </summary>
    internal sealed class TableValueDropdown : AdvancedDropdown
    {
        private readonly string title;
        private readonly List<(string Value, bool Enabled)> options;
        private readonly Action<string> onSelected;

        public TableValueDropdown(
            AdvancedDropdownState state,
            string title,
            List<(string Value, bool Enabled)> options,
            Action<string> onSelected)
            : base(state)
        {
            this.title = title;
            this.options = options;
            this.onSelected = onSelected;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(title);
            for (int index = 0; index < options.Count; index++)
            {
                root.AddChild(new AdvancedDropdownItem(options[index].Value)
                {
                    id = index,
                    enabled = options[index].Enabled
                });
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            //AdvancedDropdownItem.id 经属性初始化器赋值后回调侧读回不可靠,改按展示名匹配
            for (int index = 0; index < options.Count; index++)
            {
                if (options[index].Value == item.name)
                {
                    if (options[index].Enabled)
                    {
                        onSelected(options[index].Value);
                    }
                    return;
                }
            }
        }
    }
}
