using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;

namespace Mochi.Unity.Saving
{
    public class SaveStorage
    {
        private const string manifestFileName = "SaveManifest.json";
        private readonly string basePath;
        private SaveManifest saveManifest;
        public SaveStorage(string directoryPath)
        {
            basePath = directoryPath;
        }

        public void Initialize()
        {
            //读取SaveManifest
            string manifestPath = Path.Combine(Application.persistentDataPath, basePath, manifestFileName);
            if (!File.Exists(manifestPath))
            {
                saveManifest = new SaveManifest();
            }
            else
            {
                string manifestJson = File.ReadAllText(manifestPath);
                saveManifest = JsonConvert.DeserializeObject<SaveManifest>(manifestJson);
            }
        }

        public void SaveManifest()
        {
            string manifestPath = Path.Combine(Application.persistentDataPath, basePath, manifestFileName);
            string manifestJson = JsonConvert.SerializeObject(saveManifest, Formatting.Indented);
            File.WriteAllText(manifestPath, manifestJson);
        }

        public async UniTask SaveData<T>(T data, string fileName, int key = 0)
        {
            if (data is null) return;
            await SaveDataInternal(data, fileName, key).AsUniTask();
        }

        public async UniTask<T> LoadData<T>(T data, string fileName, int key = 0)
        {
            if (!saveManifest.saveKeys.Contains(key)) return default;
            return await LoadDataInternal(data, fileName, key).AsUniTask();
        }

        public void DeleteData(int key)
        {
            if (!saveManifest.HasKey(key)) return;

            string directoryPath = Path.Combine(Application.persistentDataPath, basePath, key.ToString());
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }

            saveManifest.saveKeys.Remove(key);
            SaveManifest();
        }

        public List<int> GetAllSaveKeys()
        {
            List<int> keys = new List<int>(saveManifest.saveKeys);
            return keys;
        }

        public int GetNextSaveKey()
        {
            return saveManifest.nextSaveKey;
        }

        private async Task SaveDataInternal<T>(T data, string fileName, int key = 0)
        {
            string directoryPath = Path.Combine(Application.persistentDataPath, basePath, key.ToString());
            string filePath = Path.Combine(directoryPath, fileName + ".dat");
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using (var stream = File.Create(filePath))
            using (var writer = new StreamWriter(stream))
            using (var jsonWriter = new JsonTextWriter(writer))
            {
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                };
                var serializer = JsonSerializer.Create(settings);
                serializer.Serialize(jsonWriter, data);
                await writer.FlushAsync();
            }

            if (!saveManifest.saveKeys.Contains(key))
            {
                saveManifest.saveKeys.Add(key);
                saveManifest.nextSaveKey++;
            }

            SaveManifest();
        }

        private async Task<T> LoadDataInternal<T>(T data, string fileName, int key = 0)
        {
            string directoryPath = Path.Combine(Application.persistentDataPath, basePath, key.ToString());
            string filePath = Path.Combine(directoryPath, fileName + ".dat");
            Debug.Log($"加载文件：{filePath}");
            if (File.Exists(filePath))
            {
                using (var stream = File.OpenRead(filePath))
                using (var reader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    JsonSerializerSettings settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All,
                    };
                    var serializer = JsonSerializer.Create(settings);

                    await Task.Run(() =>
                    {
                        data = serializer.Deserialize<T>(jsonReader);
                    });

                    await UniTask.SwitchToMainThread();
                    return data;
                }
            }

            return default(T);
        }
    }
}
