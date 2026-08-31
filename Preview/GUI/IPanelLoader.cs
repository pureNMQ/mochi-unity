using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mochi.Unity.Preview.GUI
{
    public interface IPanelLoader
    {
        UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken);
        void Release(GameObject prefab);
    }
}
