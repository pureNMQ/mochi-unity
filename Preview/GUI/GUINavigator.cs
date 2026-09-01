using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public sealed class GUINavigator : MonoBehaviour
    {
        [SerializeField] private PageContainer pageContainer;

        public PageContainer PageContainer => pageContainer;

        public void Initialize(IPanelLoader loader)
        {
            if (pageContainer == null)
            {
                throw new InvalidOperationException("GUINavigator requires one PageContainer.");
            }

            pageContainer.Initialize(loader);
        }

        public UniTask<bool> BackAsync()
        {
            return pageContainer == null ? UniTask.FromResult(false) : pageContainer.BackAsync();
        }

        public UniTask<bool> CloseAsync()
        {
            return pageContainer == null ? UniTask.FromResult(false) : pageContainer.CloseAsync();
        }
    }
}
