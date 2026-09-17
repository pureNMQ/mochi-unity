using System;
using System.Threading;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class PanelContainer : Panel
    {
        private IPanelLoader loader;

        public bool IsBusy { get; protected set; }

        protected IPanelLoader Loader => loader;
        //Busy 接管与销毁共用此不可用标记
        public bool IsDisposed { get; protected set; }

        //导航器用于跳过对已初始化容器的重复 Initialize
        internal bool IsInitialized => loader != null;

        public virtual void Initialize(IPanelLoader panelLoader)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (panelLoader == null)
            {
                throw new ArgumentNullException(nameof(panelLoader));
            }

            if (loader != null)
            {
                throw new InvalidOperationException($"{GetType().Name} is already initialized.");
            }

            loader = panelLoader;
        }

        protected abstract CancellationToken LifetimeCancellationToken { get; }

        protected override void OnDestroy()
        {
            IsDisposed = true;
            base.OnDestroy();
        }
    }
}
