using System.IO;
using Mochi.Save;
using UnityEngine;

namespace Mochi.Unity.Saving
{
    /// <summary>
    /// 把 Mochi 核心的存档原语适配到 Unity 的 <see cref="Application.persistentDataPath"/>。
    /// 构造时确定存档目录并确保其存在；目录内的文档读写、备份与版本隔离由 <see cref="SaveFileStore"/> 承担。
    /// </summary>
    public sealed class SaveSystem
    {
        /// <summary>
        /// 以 <see cref="Application.persistentDataPath"/> 下的存档目录创建存储，并创建该目录（若不存在）。
        /// </summary>
        public SaveSystem()
        {
            string directoryPath = Path.Combine(Application.persistentDataPath, SaveFileStore.SaveDirectoryName);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            Store = new SaveFileStore(directoryPath);
        }

        /// <summary>
        /// 指向 persistentDataPath 存档目录的存储实例，创建后不变。
        /// </summary>
        public SaveFileStore Store { get; }
    }
}
