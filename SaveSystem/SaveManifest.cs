using System.Collections;
using System.Collections.Generic;

namespace Mochi.Unity.Saving
{
    [System.Serializable]
    public class SaveManifest
    {
        public List<int> saveKeys = new List<int>();
        public int nextSaveKey = 0;

        public bool HasKey(int key)
        {
            return saveKeys.Contains(key);
        }
    }
}
