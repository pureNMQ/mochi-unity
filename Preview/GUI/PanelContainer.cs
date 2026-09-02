using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.Preview.GUI
{
    public abstract class PanelContainer : Panel
    {
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
    }
}
