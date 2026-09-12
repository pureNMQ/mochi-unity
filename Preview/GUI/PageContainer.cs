using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public class PageContainer : StackContainer
    {
        public IPanel Current => StackCount > 0 ? GetStackEntry(StackCount - 1).Panel : null;
        //F4：公开导航口径一致检查不可用状态，销毁/Busy 关闭接管后不再允许返回
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
            //保存原始 instance 引用：类型不匹配导致 page 为空时，实例仍需由统一收尾销毁
            GameObject prefab = null;
            GameObject instance = null;
            TPage page = null;
            //committed 是本次 Push 的资源所有权转移点：置位前资源归本次操作，置位后归页面栈。
            //唯一 finally 依据它分流失败收尾；各检测分支只决定失败并按契约返回，不各自清理。
            bool committed = false;
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                if (previousEntry != null)
                {
                    previousPage.SetInteractable(false);
                }

                //加载预制体
                prefab = await Loader.LoadAsync(key, cancellationToken);
                //检查取消请求，如果已经取消，则抛出异常
                cancellationToken.ThrowIfCancellationRequested();
                if (prefab == null)
                {
                    return null;
                }

                //实例化页面
                instance = Instantiate(prefab, transform, false);
                instance.SetActive(false);
                //检查页面实例类型是否为指定类型，不匹配时交由统一收尾销毁实例并释放预制体
                page = instance.GetComponent<TPage>();
                if (page == null)
                {
                    return null;
                }

                //调用生命期方法进行页面准备
                await prepareAsync(page, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                page.OpenInternal();

                //C1：可见期先 Show 再入场（既有顺序）；隐藏期新页保持逻辑隐藏——
                //不先 Show 再 Hide、不触发 OnShow、不播放入场过渡
                if (!SuppressVisuals)
                {
                    page.ShowInternal();
                    page.SetInteractable(false);

                    //执行页面进入过渡动画（C1：经视觉作用域等待，Hide 停止视觉后按隐藏期规则继续）
                    await WaitVisualTransitionAsync(
                        RunEnterTransitionAsync(page, VisualCancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    //C1 隐藏期：不执行 OnShow 与入场，保持实例失活；交互组同步禁用，不启用输入
                    page.SetInteractable(false);
                }

                //隐藏上一个页面。先把当前选择交还给前页的焦点记忆目标再隐藏，
                //避免 HideInternal 保存焦点时把已交给新页的焦点覆盖进前页的焦点记忆，
                //保证之后 Back 或失败恢复重新显示前页时能找回其自身焦点。
                //C1：隐藏期不做焦点操作，前页由 HideInternal 幂等保持隐藏
                if (previousPage != null)
                {
                    if (previousWasVisible && !SuppressVisuals)
                    {
                        previousPage.RestoreFocusInternal();
                    }

                    previousPage.HideInternal();
                }

                //C1：可见期启用当前页交互并显式交接焦点；隐藏期保持逻辑隐藏、不启用输入。
                //隐藏期入栈且容器中途恢复显示的页面不在此补播（IsVisible 为假），
                //由下一次 ShowAsync 的呈现流程按最终栈统一补齐
                if (!SuppressVisuals && page.IsVisible)
                {
                    page.SetInteractable(true);
                    page.FocusDefaultInternal();
                }

                //C1：可见期 Push 的入场动画被 Hide 中断时，页面已执行过 Show——
                //按隐藏期规则收回逻辑隐藏（恰好一次 OnHide），保证隐藏期完成的导航最终不可见；
                //显示期由 ShowAsync 呈现流程按最终栈补齐
                if (SuppressVisuals && page.IsVisible)
                {
                    page.HideInternal();
                }

                AddStackEntry(page, prefab);
                committed = true;
                return page;
            }
            finally
            {
                //Busy 的解除属于整个操作的统一结束职责；先于失败收尾执行，
                //保证收尾过程中再抛出异常（如 Release 失败）时 Busy 也已复位
                IsBusy = false;
                if (!committed)
                {
                    CleanupFailedPush(instance, page, prefab);
                    RestorePreviousPage(previousPage, previousWasVisible);
                }
            }
        }

        //Push 失败收尾：集中清理本次实际取得且仍持有的资源。
        //committed 之前的任何失败（加载失败、组件不匹配、准备失败、进入失败及取消）都只经过这里，
        //依赖 Panel 内部幂等，调用方不判断 IsOpen/IsVisible。
        private void CleanupFailedPush(GameObject instance, Panel page, GameObject prefab)
        {
            //按 Unity 对象有效性判断：实例已提前销毁时跳过生命周期调用与实例销毁，仍释放本次持有的资源引用
            if (page != null)
            {
                page.HideInternal();
                page.CloseInternal();
            }

            if (instance != null)
            {
                Destroy(instance);
            }

            //只释放本次成功加载且仍持有的引用；Loader 返回 null 时不存在的可释放引用不会走到这里
            if (prefab != null)
            {
                Loader.Release(prefab);
            }
        }

        //Push 失败后恢复前页状态；前页已随容器销毁（Unity 对象失效）时不再尝试恢复其显示
        private void RestorePreviousPage(Panel previousPage, bool previousWasVisible)
        {
            if (previousPage == null)
            {
                return;
            }

            if (previousWasVisible)
            {
                //仍可见的前页 ShowInternal 为空操作，幂等化后焦点不再隐式恢复，这里显式补偿；
                //已被隐藏的前页由 ShowInternal 恢复显示并恢复焦点，随后的显式恢复重复同一目标无副作用
                previousPage.ShowInternal();
                previousPage.RestoreFocusInternal();
            }

            previousPage.SetInteractable(previousWasVisible);
        }

        public async UniTask<bool> BackAsync()
        {
            //在Busy状态或栈中只剩1个页面时，无法执行返回操作
            if (!CanGoBack)
            {
                return false;
            }

            IsBusy = true;
            StackEntry currentEntry = GetStackEntry(StackCount - 1);
            StackEntry previousEntry = GetStackEntry(StackCount - 2);
            CancellationToken cancellationToken = LifetimeCancellationToken;

            try
            {
                currentEntry.Panel.SetInteractable(false);

                //C1：可见期显示前页并播放退出过渡；隐藏期不 Show 前页、不播放退出/进入动画，
                //前页保持逻辑隐藏，条目清理、资源释放与移出栈仍正常完成
                if (!SuppressVisuals)
                {
                    previousEntry.Panel.ShowInternal();
                    previousEntry.Panel.SetInteractable(false);

                    //C1：经视觉作用域等待，Hide 停止视觉后按隐藏期规则继续
                    await WaitVisualTransitionAsync(
                        RunExitTransitionAsync(currentEntry.Panel, VisualCancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                currentEntry.Panel.HideInternal();
                currentEntry.Panel.CloseInternal();
            }
            finally
            {
                //F4：销毁/Busy 关闭接管（IsDisposed 前移）已接管整栈时，Back 的异步收尾
                //不再清理条目、移除索引、恢复前页，防止重复释放与错误索引
                if (!IsDisposed)
                {
                    //所有权转移：同步收尾开始先把目标条目移出栈（销毁清栈不再拿到同一条目），
                    //随后同步销毁实例并释放引用，中间不 await
                    RemoveStackEntryAt(StackCount - 1);

                    //R2 实例有效性检查：实例可能在过渡期间被外部销毁，仅存活时销毁；
                    //资源释放不放在实例分支内，无论实例是否存活都恰好释放一次
                    if (currentEntry.Panel != null)
                    {
                        Destroy(currentEntry.Panel.gameObject);
                    }

                    if (currentEntry.Prefab != null)
                    {
                        Loader.Release(currentEntry.Prefab);
                    }

                    //C1：仅可见期恢复前页输入；隐藏期不启用输入，由下一次 ShowAsync 的呈现流程统一启用
                    if (!SuppressVisuals)
                    {
                        previousEntry.Panel.SetInteractable(true);
                    }

                    IsBusy = false;
                }
            }

            return true;
        }

        //C1：容器显示的呈现流程——按最终栈只呈现当前页；隐藏期推进的导航在此一次性补齐视觉，
        //不重放隐藏期中间态。OnShow 保持同步通知（由 ShowInternal 触发），转场在本流程内 await，
        //动画结束后再启用当前页输入
        protected override async UniTask PresentOnShowAsync()
        {
            if (StackCount == 0)
            {
                return;
            }

            //仅显示当前页：栈内其余页按显示规则保持隐藏（已隐藏条目由 HideInternal 幂等兜底）；
            //沿用 R2 风格 Unity 判空，已被外部销毁的条目实例不再执行生命周期调用
            for (int index = StackCount - 2; index >= 0; index--)
            {
                Panel lower = GetStackEntry(index).Panel;
                if (lower != null)
                {
                    lower.HideInternal();
                }
            }

            Panel current = GetStackEntry(StackCount - 1).Panel;
            //隐藏期入栈的当前页在此执行 OnShow；已显示条目由幂等空操作，
            //入场同理不再重复播放（与 Panel Show 幂等语义对齐，重复 ShowAsync 只重断输入状态）
            bool currentWasVisible = current.IsVisible;
            current.ShowInternal();
            current.SetInteractable(false);

            //呈现中再次隐藏：剩余视觉不再执行、输入保持禁用，由下一次 ShowAsync 呈现最终态
            if (!IsVisible)
            {
                return;
            }

            if (!currentWasVisible)
            {
                await WaitVisualTransitionAsync(
                    RunEnterTransitionAsync(current, VisualCancellationToken),
                    LifetimeCancellationToken);
            }

            //动画结束后再启用输入；呈现中再次隐藏/接管则不启用（销毁路径按 F4 以取消异常向上传播）
            if (IsVisible && !IsDisposed)
            {
                current.SetInteractable(true);
                current.FocusDefaultInternal();
            }
        }

        protected override void OnClose()
        {
            //F4 收尾（2026-09-12）：父容器关闭触发本容器 OnClose 且本容器 Busy 时立即接管，
            //不排队/挂起：先标记不可用并取消在途操作，在途 Back/Push 的异步收尾据此跳过条目清理，
            //清栈由下方 ClearPages 与销毁共用同一套所有权规则
            if (IsBusy)
            {
                BeginBusyTakeOver();
            }

            //显式维护 Busy：清栈期间容器不接受新操作，完成后复位，容器本身继续可用
            IsBusy = true;
            try
            {
                ClearPages();
                base.OnClose();
            }
            finally
            {
                IsBusy = false;
            }
        }

        //R4 共用清栈：从栈顶向下逐条目先移出栈（所有权转移）再同步清理——
        //关闭仍存活实例、销毁实例、释放条目 Prefab 引用；移出后的条目不会被后续清栈重复处理，
        //正常 Close 后再销毁即空操作（无需 cleaned 标记）。生命周期调用不做 IsVisible/IsOpen
        //包裹判断，由 Panel 内部幂等与 Unity 有效性检查决定（R2/R3）
        private void ClearPages()
        {
            while (StackCount > 0)
            {
                StackEntry entry = GetStackEntry(StackCount - 1);
                RemoveStackEntryAt(StackCount - 1);

                //R2 实例有效性检查：仅存活实例执行生命周期调用与销毁；资源释放不放在实例分支内
                if (entry.Panel != null)
                {
                    entry.Panel.HideInternal();
                    entry.Panel.CloseInternal();
                    Destroy(entry.Panel.gameObject);
                }

                if (entry.Prefab != null)
                {
                    Loader.Release(entry.Prefab);
                }
            }
        }

        protected override void CleanupStackOnDestroy()
        {
            //R4：销毁路径复用同一清栈方法；销毁入口（StackContainer.OnDestroy）已先标记
            //不可用并取消在途操作，清空栈与生命周期资源释放由其统一收尾
            ClearPages();
        }
    }
}
