using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    public sealed class ToastService
    {
        private readonly GUIService gui;

        public ToastService(GUIService gui)
        {
            this.gui = gui ?? throw new ArgumentNullException(nameof(gui));
        }

        public async UniTask Show<TPanel>(string address, float durationSeconds = 2f)
            where TPanel : Panel
        {
            TPanel toast = await gui.CreateAndShowPanelAsync<TPanel>(address, GUILayer.Toast);
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
