using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mochi.Unity.GUI
{
    public interface IPanelTransition
    {
        UniTask EnterAsync(CancellationToken cancellationToken);
        UniTask ExitAsync(CancellationToken cancellationToken);
    }
}
