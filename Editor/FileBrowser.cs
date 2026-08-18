using UnityEditor;
using UnityEngine;
using System.IO;

namespace Mochi.Unity.Editor
{
    public class FileBrowserWindow : EditorWindow
    {
        [MenuItem("Mochi/Save Browser")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(FileBrowserWindow), false, "Save Browser");
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("存档根目录: " + Application.persistentDataPath, GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.7f));

            if (GUILayout.Button("打开"))
            {
                Application.OpenURL(Application.persistentDataPath);
            }
            GUILayout.EndHorizontal();

            ListFilesInPersistentDataPath();

        }

        private void ListFilesInPersistentDataPath()
        {
            string persistentDataPath = Application.persistentDataPath;
            EditorGUILayout.BeginScrollView(Vector2.zero);
            if (Directory.Exists(persistentDataPath))
            {
                int fileCount = ListFiles(persistentDataPath);

                if (fileCount > 0)
                {
                    GUILayout.Label("存档根目录下共找到 " + fileCount + " 个存档文件");
                }
                else
                {
                    GUILayout.Label("存档根目录下没有找到任何存档文件");
                }
            }
            else
            {
                GUILayout.Label("根目录不存在");
            }
            EditorGUILayout.EndScrollView();
        }


        private int ListFiles(string path)
        {
            int fileCount = 0;
            string[] paths = Directory.GetDirectories(path); // 获取所有子目录

            if (paths.Length > 0)
            {
                foreach (string subPath in paths)
                {
                    if (Path.GetFileNameWithoutExtension(subPath) == "Unity") break;
                    fileCount += ListFiles(subPath);
                }
            }

            string[] files = Directory.GetFiles(path); // 获取当前目录下的所有文件

            if (files.Length > 0)
            {
                foreach (string file in files)
                {
                    string filePath = Path.GetRelativePath(Application.persistentDataPath, file);

                    GUILayout.BeginHorizontal();
                    var color = UnityEngine.GUI.color;
                    //GUI.color = Color.yellow;
                    GUILayout.Label(filePath, new GUIStyle(EditorStyles.textArea), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.7f));

                    //GUI.color = Color.green;
                    if (GUILayout.Button("打开"))
                    {
                        Application.OpenURL(file);
                    }
                    //GUI.color = Color.red;
                    if (GUILayout.Button("删除"))
                    {
                        File.Delete(file);
                    }
                    UnityEngine.GUI.color = color;
                    GUILayout.EndHorizontal();

                    fileCount++;
                }
            }

            return fileCount;
        }
    }
}
