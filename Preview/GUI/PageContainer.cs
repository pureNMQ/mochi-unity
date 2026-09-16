using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public class PageContainer : StackContainer
    {
        public IPanel Current => StackCount > 0 ? GetStackEntry(StackCount - 1).Panel : null;
        //公开导航口径一致检查不可用状态，销毁/Busy 关闭接管后不再允许返回
        public bool CanGoBack => !IsBusy && !IsDisposed && StackCount > 1;

        /// <summary>
        /// 不绑定实体数据的页面入栈
        /// </summary>
        /// <typeparam name="TPage">页面类型</typeparam>
        /// <param name="key">页面预制体的资源键值，根据使用的资源管理器而不同</param>
        /// <returns>创建的页面实例</returns>
        public UniTask<TPage> PushAsync<TPage>(string key) where TPage : Panel
        {
            return PushCoreAsync<TPage>(key, (page, token) => page.PrepareInternalAsync(token));
        }

        /// <summary>
        /// 绑定实体数据的页面入栈
        /// </summary>
        /// <typeparam name="TPage">页面类型</typeparam>
        /// <typeparam name="TData">数据类型</typeparam>
        /// <param name="key">页面预制体的资源键值，根据使用的资源管理器而不同</param>
        /// <param name="data">要绑定的实体数据</param>
        /// <returns>创建的页面实例</returns>
        public UniTask<TPage> PushAsync<TPage, TData>(string key, TData data)
            where TPage : Panel<TData>
        {
            return PushCoreAsync<TPage>(key, (page, token) => page.PrepareInternalAsync(data, token));
        }

        //Busy 仅覆盖加载+Prepare+逻辑提交，过渡由 Panel fire-and-forget，容器不等待
        private async UniTask<TPage> PushCoreAsync<TPage>(
            string key,
            Func<TPage, CancellationToken, UniTask> prepareAsync)
            where TPage : Panel
        {
            if (IsDisposed)
            {
                return null;
            }

            if (Loader == null)
            {
                throw new InvalidOperationException("PageContainer must be initialized before pushing a page.");
            }

            if (IsBusy)
            {
                return null;
            }

            IsBusy = true;
            StackEntry previousEntry = StackCount > 0 ? GetStackEntry(StackCount - 1) : null;
            Panel previousPage = previousEntry?.Panel;
            bool previousWasVisible = previousPage != null && previousPage.IsVisible;
            //committed 是本次 Push 的资源所有权转移点：置位前资源归本次操作，置位后归页面栈。
            //唯一 finally 依据它分流失败收尾；各检测分支只决定失败并按契约返回，不各自清理。
            TPage page = null;
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                if (previousEntry != null)
                {
                    previousPage.SetInteractable(false);
                }

                //工厂完成加载、绑定、Prepare 与 OnOpen；失败返回 null 或抛出并已自收尾
                page = await PanelFactory.CreateAsync<TPage>(Loader, key, transform, null, prepareAsync, cancellationToken);
                if (page == null)
                {
                    return null;
                }

                if (!SuppressVisuals)
                {
                    //新页逻辑显示的同一刻前页即时隐藏（焦点随停用保存），新页入场在下层已空状态播放
                    if (previousPage != null)
                    {
                        previousPage.Hide();
                    }

                    //显示即提交状态、启用输入与焦点，入场过渡由 Panel fire-and-forget
                    page.Show();
                }
                else
                {
                    //隐藏期：不执行 OnShow 与入场，保持实例失活；交互组同步禁用，不启用输入
                    page.SetInteractable(false);
                }

                AddStackEntry(page);
                committed = true;
                return page;
            }
            finally
            {
                //Busy 的解除属于整个操作的统一结束职责；先于失败收尾执行，
                //保证收尾过程中再抛出异常（如实例销毁回调失败）时 Busy 也已复位
                IsBusy = false;
                if (!committed)
                {
                    CleanupFailedPush(page);
                    RestorePreviousPage(previousPage, previousWasVisible);
                }
            }
        }

        //Push 失败收尾：集中清理未提交实例；工厂失败（page 为 null）时资源已由工厂处理。
        //Close 自毁接管资源与销毁，显式释放与销毁保证即时收回（不等退场动画）
        private void CleanupFailedPush(Panel page)
        {
            //按 Unity 对象有效性判断：实例已提前销毁时跳过生命周期调用与实例销毁
            if (page != null)
            {
                page.Close();
                page.ReleasePrefabHandleImmediate();
                Destroy(page.gameObject);
            }
        }

        //Push 失败后恢复前页状态；前页已随容器销毁（Unity 对象失效）时不再尝试恢复其显示
        private void RestorePreviousPage(Panel previousPage, bool previousWasVisible)
        {
            if (previousPage == null)
            {
                return;
            }

            //已被隐藏的前页由 Show 恢复显示（含输入与焦点）；
            //仍可见的前页 Show 为幂等空操作，显式补偿交互与焦点（重复同一焦点目标无副作用）
            if (previousWasVisible)
            {
                previousPage.Show();
                previousPage.SetInteractable(true);
                previousPage.RestoreFocusInternal();
            }
            else
            {
                previousPage.SetInteractable(previousWasVisible);
            }
        }

        //容器清理先行：条目移出栈解除关系后关闭面板（自毁流程接管资源与销毁），
        //再恢复前页；前页恢复抛错也不跳过当前页关闭（确定关闭、不回滚）
        public UniTask<bool> BackAsync()
        {
            //在Busy状态或栈中只剩1个页面时，无法执行返回操作
            if (!CanGoBack)
            {
                return UniTask.FromResult(false);
            }

            IsBusy = true;
            StackEntry currentEntry = GetStackEntry(StackCount - 1);
            StackEntry previousEntry = GetStackEntry(StackCount - 2);

            try
            {
                RemoveStackEntryAt(StackCount - 1);
                try
                {
                    //可见期恢复前页：显示即启用输入与焦点，当前页退场动画在其上方淡出；
                    //隐藏期不 Show 前页，保持逻辑隐藏，由下一次 ShowAsync 呈现流程统一恢复
                    if (!SuppressVisuals && previousEntry.Panel != null)
                    {
                        previousEntry.Panel.Show();
                    }
                }
                finally
                {
                    if (currentEntry.Panel != null)
                    {
                        currentEntry.Panel.Close();
                    }
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    IsBusy = false;
                }
            }

            return UniTask.FromResult(true);
        }

        //容器显示的呈现流程：按最终栈只呈现当前页（显示即启用输入与焦点，入场自行播放），
        //其余条目保持逻辑隐藏，不重放中间态
        protected override void PresentOnShow()
        {
            if (StackCount == 0)
            {
                return;
            }

            //仅显示当前页：栈内其余页按显示规则保持隐藏（已隐藏条目由 Hide 幂等兜底）
            for (int index = StackCount - 2; index >= 0; index--)
            {
                Panel lower = GetStackEntry(index).Panel;
                if (lower != null)
                {
                    lower.Hide();
                }
            }

            Panel current = GetStackEntry(StackCount - 1).Panel;
            if (current != null)
            {
                current.Show();
            }
        }

        protected override void OnClose()
        {
            //父容器关闭触发本容器 OnClose 且 Busy 时立即接管（不排队）：
            //先标记不可用并取消在途操作，清栈由 ClearPages 与销毁共用所有权规则
            if (IsBusy)
            {
                BeginBusyTakeOver();
            }

            //显式维护 Busy：清栈期间容器不接受新操作，完成后复位，容器本身继续可用
            IsBusy = true;
            try
            {
                ClearPages(immediateDestroy: false);
                base.OnClose();
            }
            finally
            {
                IsBusy = false;
            }
        }

        //共用清栈：从栈顶向下逐条目先解除关系（移出栈）再关闭面板——
        //immediateDestroy 为真时（销毁强杀）显式释放与销毁、不等退场动画
        private void ClearPages(bool immediateDestroy)
        {
            while (StackCount > 0)
            {
                StackEntry entry = GetStackEntry(StackCount - 1);
                RemoveStackEntryAt(StackCount - 1);

                //实例有效性检查：仅存活实例执行生命周期调用与销毁
                if (entry.Panel != null)
                {
                    entry.Panel.Close();
                    if (immediateDestroy)
                    {
                        entry.Panel.ReleasePrefabHandleImmediate();
                        Destroy(entry.Panel.gameObject);
                    }
                }
            }
        }

        //面板未经容器自行关闭：条目已抽出、其余条目顺序不变；
        //被关闭的是栈顶时按最终栈恢复新顶显示，中段抽出不打扰其余条目
        protected override void HandleSelfClosedPanel(Panel panel, bool wasTop)
        {
            if (!wasTop || SuppressVisuals || StackCount == 0)
            {
                return;
            }

            Panel top = GetStackEntry(StackCount - 1).Panel;
            if (top != null && !top.IsVisible)
            {
                top.Show();
            }
        }

        protected override void CleanupStackOnDestroy()
        {
            //销毁路径复用同一清栈方法（强杀口径：立即释放与销毁）；
            //销毁入口已先标记不可用并取消在途操作
            ClearPages(immediateDestroy: true);
        }
    }
}
