using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.GUI
{
    //激活与停用归 Panel 管，本组件只负责 alpha 过渡
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FadePanelTransition : MonoBehaviour, IPanelTransition
    {
        [SerializeField, Min(0f)] private float duration = 0.2f;
        [SerializeField] private CanvasGroup canvasGroup;

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0f, value);
        }

        public UniTask EnterAsync(CancellationToken cancellationToken)
        {
            return FadeAsync(1f, cancellationToken);
        }

        public UniTask ExitAsync(CancellationToken cancellationToken)
        {
            return FadeAsync(0f, cancellationToken);
        }

        private async UniTask FadeAsync(float targetAlpha, CancellationToken cancellationToken)
        {
            CanvasGroup target = canvasGroup != null ? canvasGroup : GetComponent<CanvasGroup>();
            float startAlpha = target.alpha;

            if (duration <= 0f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target.alpha = targetAlpha;
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                target.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            target.alpha = targetAlpha;
        }
    }
}
