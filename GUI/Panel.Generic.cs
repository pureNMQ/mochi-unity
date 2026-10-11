using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.GUI
{
    public abstract class Panel<TData> : Panel
    {
        internal UniTask PrepareInternalAsync(TData data, CancellationToken cancellationToken)
        {
            return OnPrepareAsync(data, cancellationToken);
        }

        protected sealed override UniTask OnPrepareAsync(CancellationToken cancellationToken)
        {
            return UniTask.FromException(new InvalidOperationException(
                $"{GetType().Name} requires typed page data."));
        }

        protected virtual UniTask OnPrepareAsync(TData data, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

    }
}
