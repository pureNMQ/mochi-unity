using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class ModalPanel<TResult> : ModalPanel
    {
        private ModalResultCallback<TResult> pushCallback;

        public event ModalResultCallback<TResult> ResultReturned;

        protected UniTask<bool> CompleteAsync(TResult result)
        {
            return Container == null
                ? UniTask.FromResult(false)
                : Container.CompleteAsync(this, result);
        }

        //强类型绑定结果回调：调用方已知 TResult，无需先擦除为 Delegate 再 as 转回，类型不匹配由编译期发现
        internal void BindResultCallback(ModalResultCallback<TResult> callback)
        {
            pushCallback = callback;
        }

        internal override Action CaptureResultNotification(bool hasResult, object result)
        {
            ModalResultCallback<TResult> callback = pushCallback;
            Delegate[] listeners = ResultReturned?.GetInvocationList();
            TResult typedResult = hasResult ? (TResult)result : default;

            pushCallback = null;
            ResultReturned = null;

            return callback == null && listeners == null
                ? null
                : () =>
                {
                    InvokeSafely(callback, hasResult, typedResult);
                    if (listeners == null)
                    {
                        return;
                    }

                    foreach (Delegate listener in listeners)
                    {
                        InvokeSafely((ModalResultCallback<TResult>)listener, hasResult, typedResult);
                    }
                };
        }

        internal override void ClearResultCallbacks()
        {
            pushCallback = null;
            ResultReturned = null;
        }

        private static void InvokeSafely(
            ModalResultCallback<TResult> callback,
            bool hasResult,
            TResult result)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(hasResult, result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    public abstract class ModalPanel<TData, TResult> : ModalPanel<TResult>
    {
        internal UniTask PrepareInternalAsync(TData data, CancellationToken cancellationToken)
        {
            return OnPrepareAsync(data, cancellationToken);
        }

        protected sealed override UniTask OnPrepareAsync(CancellationToken cancellationToken)
        {
            return UniTask.FromException(new InvalidOperationException(
                $"{GetType().Name} requires typed modal data."));
        }

        protected virtual UniTask OnPrepareAsync(TData data, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}
