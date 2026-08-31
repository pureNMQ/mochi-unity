#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using Mochi.Logging;

namespace Mochi.Unity.Logging.Editor
{
    public static class ConsoleNavigationHook
    {
        private class FrameMapping
        {
            public readonly string scriptPath;
            public readonly string typeName;
            public int instanceID;

            public FrameMapping(string scriptPath, Type type)
            {
                this.scriptPath = scriptPath;
                this.typeName = type.FullName;
            }
        }

        private static FrameMapping[] _mappings = new FrameMapping[]
        {
            new FrameMapping(
                "UnityConsoleWriter.cs",
                typeof(UnityConsoleWriter)
            ),
            new FrameMapping(
                "UnityConsoleWriter.cs",
                typeof(LogManager)
            ),
            new FrameMapping(
                "UnityConsoleWriter.cs",
                typeof(Logger)
            ),
        };

        [UnityEditor.Callbacks.OnOpenAsset(-1)]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            for (int i = _mappings.Length - 1; i >= 0; --i)
            {
                var mapping = _mappings[i];
                CacheInstanceID(mapping);

                if (instanceID == mapping.instanceID)
                {
                    var stackTrace = GetStackTrace();
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        var frames = stackTrace.Split('\n');
                        var callerFrame = FindCallerFrame(frames);
                        if (!string.IsNullOrEmpty(callerFrame))
                        {
                            var fileLine = ParseFileLine(callerFrame);
                            var filePath = ParseFilePath(callerFrame);

                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
                            if (asset != null)
                            {
                                AssetDatabase.OpenAsset(asset, fileLine);
                                return true;
                            }
                        }
                    }

                    break;
                }
            }

            return false;
        }

        private static string ResolveMochiPath(string mochiRelative)
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript " + mochiRelative.Replace(".cs", ""));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(mochiRelative))
                {
                    return path;
                }
            }

            return null;
        }

        private static string GetStackTrace()
        {
            var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            var fieldInfo = consoleWindowType.GetField(
                "ms_ConsoleWindow",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            var consoleWindowInstance = fieldInfo.GetValue(null);

            if (consoleWindowInstance != null)
            {
                if ((object)EditorWindow.focusedWindow == consoleWindowInstance)
                {
                    fieldInfo = consoleWindowType.GetField(
                        "m_ActiveText",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    );
                    return fieldInfo.GetValue(consoleWindowInstance).ToString();
                }
            }

            return "";
        }

        private static void CacheInstanceID(FrameMapping mapping)
        {
            if (mapping.instanceID > 0)
            {
                return;
            }

            var resolvedPath = ResolveMochiPath(mapping.scriptPath);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedPath);
            if (asset == null)
            {
                throw new Exception("ConsoleNavigationHook: asset not found at path=" + resolvedPath);
            }

            mapping.instanceID = asset.GetInstanceID();
        }

        private static string FindCallerFrame(string[] frames)
        {
            for (int i = frames.Length - 1; i >= 0; --i)
            {
                foreach (var mapping in _mappings)
                {
                    if (frames[i].Contains(mapping.typeName))
                    {
                        if (i < frames.Length - 1)
                        {
                            return frames[i + 1];
                        }

                        return "";
                    }
                }
            }

            return "";
        }

        private static string ParseFilePath(string frame)
        {
            int start = frame.IndexOf("(at ") + "(at ".Length;
            int end = ParseFileLineStartIndex(frame) - 1;
            return frame.Substring(start, end - start);
        }

        private static int ParseFileLine(string frame)
        {
            int start = ParseFileLineStartIndex(frame);
            string digits = "";
            for (int i = start; i < frame.Length; ++i)
            {
                if (frame[i] < '0' || frame[i] > '9')
                {
                    break;
                }

                digits += frame[i];
            }

            return int.Parse(digits);
        }

        private static int ParseFileLineStartIndex(string frame)
        {
            int start = -1;
            for (int i = frame.Length - 1; i >= 0; --i)
            {
                if (frame[i] >= '0' && frame[i] <= '9')
                {
                    start = i;
                }
                else if (start != -1)
                {
                    break;
                }
            }

            return start;
        }
    }
}
#endif
