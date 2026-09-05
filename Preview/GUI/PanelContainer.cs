using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class PanelContainer : Panel
    {
        private IPanelLoader loader;

        public bool IsBusy { get; protected set; }

        protected IPanelLoader Loader => loader;
        protected bool IsDisposed { get; private set; }

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

        protected static UniTask RunEnterTransitionAsync(Panel panel, CancellationToken cancellationToken)
        {
            IPanelTransition transition = panel.GetComponent<IPanelTransition>();
            return transition == null
                ? UniTask.CompletedTask
                : transition.EnterAsync(cancellationToken);
        }

        protected static UniTask RunExitTransitionAsync(Panel panel, CancellationToken cancellationToken)
        {
            IPanelTransition transition = panel.GetComponent<IPanelTransition>();
            return transition == null
                ? UniTask.CompletedTask
                : transition.ExitAsync(cancellationToken);
        }

        protected virtual void OnDestroy()
        {
            IsDisposed = true;
        }
    }
}
