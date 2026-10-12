using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// Toast 面板的消息契约：面板经 <see cref="GUIService"/> 创建并显示之后，由服务写入要展示的文本。
    /// 面板创建路径不接受业务数据，因此文本只能在显示后设置。
    /// </summary>
    public interface IToastContent
    {
        void SetMessage(string message);
    }

    public sealed class ToastService
    {
        private readonly GUIService gui;
        private int sequence;

        public ToastService(GUIService gui)
        {
            this.gui = gui ?? throw new ArgumentNullException(nameof(gui));
        }

        public async UniTask Show<TPanel>(string address, float durationSeconds = 2f)
            where TPanel : Panel
        {
            TPanel toast = await gui.CreateAndShowPanelAsync<TPanel>(address, GUILayer.Toast, NextInstanceId());
            await CloseAfterDelayAsync(toast, durationSeconds);
        }

        /// <summary>
        /// 显示一条带文本的 Toast：面板创建并显示后写入消息，到时自动关闭。
        /// </summary>
        /// <param name="address">Toast 面板的 Addressables 地址。</param>
        /// <param name="message">要展示的文本。</param>
        /// <param name="durationSeconds">停留秒数；不超过 0 表示不自动关闭。</param>
        public async UniTask Show<TPanel>(string address, string message, float durationSeconds = 2f)
            where TPanel : Panel, IToastContent
        {
            TPanel toast = await gui.CreateAndShowPanelAsync<TPanel>(address, GUILayer.Toast, NextInstanceId());
            if (toast == null)
            {
                return;
            }

            toast.SetMessage(message);
            await CloseAfterDelayAsync(toast, durationSeconds);
        }

        //每次调用独立实例：面板服务按地址或实例 id 去重，共用键会让后一条提示改写前一条的文本
        private string NextInstanceId()
        {
            return "MochiToast#" + (++sequence);
        }

        private static async UniTask CloseAfterDelayAsync(Panel toast, float durationSeconds)
        {
            if (toast == null)
            {
                return;
            }

            if (durationSeconds > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(durationSeconds));
            }

            if (toast != null)
            {
                toast.Close();
            }
        }
    }
}
