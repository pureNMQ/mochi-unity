using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace Mochi.Unity.Logging
{
    public class ScreenLog : MonoBehaviour
    {
        private static ScreenLog instance;
        [RuntimeInitializeOnLoadMethod]
        private static void Setup()
        {
            var screenLog = new GameObject("ScreenLog");
            instance = screenLog.AddComponent<ScreenLog>();
            DontDestroyOnLoad(screenLog);
        }

        private const float BaseDPI = 144;
        private const int BaseFontSize = 24;

        private bool isShow = false;

        private float currentDpi = 0;
        private float logItemHeight = 32;
        private int maxLogCount = 10;
        private GUIStyle logStyle;
        private GUIStyle unityLogStyle;
        private GUIStyle warnStyle;
        private GUIStyle errorStyle;

        private List<ScreenLogItem> logItems = new List<ScreenLogItem>();
        private List<ScreenLogItem> logItemsToRemove = new List<ScreenLogItem>();

        public static void Log(string condition, float duration = 2f, string identifier = null)
        {
            Debug.Log(condition);
            if (instance == null) return;
            instance.AddLogItem(condition, duration, identifier, ScreenLogType.Log);
        }
        public static void LogWarning(string condition, float duration = 2f, string identifier = null)
        {
            //Debug.LogWarning(condition);
            if (instance == null) return;
            instance.AddLogItem(condition, duration, identifier, ScreenLogType.Warning);
        }
        public static void LogError(string condition, float duration = 2f, string identifier = null)
        {
            //Debug.LogError(condition);
            if (instance == null) return;
            instance.AddLogItem(condition, duration, identifier, ScreenLogType.Error);
        }

        private void Awake()
        {
            RebuildLogStyle();
            isShow = true;
            //Application.logMessageReceived += OnUnityLogReceived;
        }

        private void Update()
        {
            //输入控制
            if (Input.GetKeyUp(KeyCode.F3))
            {
                isShow = !isShow;
                Debug.Log($"ScreenLog:{isShow}");
            }
        }

        private void OnGUI()
        {
            //屏幕dpi变化时，重新构建日志样式
            if (isShow && currentDpi != Screen.dpi)
            {
                RebuildLogStyle();
            }
            //渲染日志项
            if (isShow)
            {
                for (int i = 0; i < logItems.Count; i++)
                {
                    Rect rect = new Rect(10, i * logItemHeight, 200, logItemHeight);
                    if (i >= maxLogCount - 1)
                    {
                        UnityEngine.GUI.Label(rect, "More logs...", unityLogStyle);
                        break;
                    }
                    UnityEngine.GUI.Label(rect, logItems[i].Condition, logItems[i].Style);
                }
            }

            logItemsToRemove.Clear();
            //标记过期日志项
            for (int i = 0; i < logItems.Count; i++)
            {
                if (Time.time >= logItems[i].EndTime)
                {
                    logItemsToRemove.Add(logItems[i]);
                }
            }

            //移除过期日志项
            for (int i = 0; i < logItemsToRemove.Count; i++)
            {
                logItems.Remove(logItemsToRemove[i]);
            }

        }

        private void AddLogItem(string condition, float duration, string identifier, ScreenLogType type, Color? color = null)
        {
            ScreenLogItem logItem = null;
            GUIStyle style = type switch
            {
                ScreenLogType.Log => logStyle,
                ScreenLogType.Warning => warnStyle,
                ScreenLogType.Error => errorStyle,
                _ => logStyle,
            };

            if (color != null)
            {
                style = new GUIStyle(style)
                {
                    normal = { textColor = color.Value },
                };
            }

            if (!string.IsNullOrEmpty(identifier))
            {
                logItem = logItems.FirstOrDefault(x => x.Identifier == identifier);
                if (logItem != null)
                {
                    logItem.Condition = condition;
                    logItem.Time = DateTime.Now;
                    logItem.Style = style;
                    logItem.Type = type;
                    logItem.EndTime = Time.time + duration;
                    return;
                }
            }

            logItem = new ScreenLogItem()
            {
                Condition = condition,
                Type = type,
                Identifier = identifier,
                Style = style,
                Time = DateTime.Now,
                EndTime = Time.time + duration,
            };
            logItems.Add(logItem);
        }

        private void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            //ScreenLog希望开发者能够快速锁定需要关注的log项，Unity.log内容经常过多，隐藏默认忽略
            //TODO 增加可调整显示内容的调试开关
            if (type == LogType.Log) return;

            var logItem = new ScreenLogItem()
            {
                Condition = $"[UNITY] {condition}",
                Type = type switch
                {
                    LogType.Log => ScreenLogType.Log,
                    LogType.Warning => ScreenLogType.Warning,
                    LogType.Error => ScreenLogType.Error,
                    LogType.Exception => ScreenLogType.Error,
                    LogType.Assert => ScreenLogType.Error,
                    _ => ScreenLogType.Log,
                },
                IsUnityLog = true,
                Style = unityLogStyle,
                Time = DateTime.Now,
                EndTime = Time.time + 2f,
            };
            logItems.Add(logItem);
        }

        private void RebuildLogStyle()
        {
            currentDpi = Screen.dpi;
            logItemHeight = BaseFontSize * BaseDPI / Screen.dpi;
            //Debug.Log($"DPI: {Screen.dpi}, logItemHeight: {logItemHeight}");
            maxLogCount = (int)(Screen.height / logItemHeight);

            logStyle = new GUIStyle()
            {
                fontSize = (int)logItemHeight,
                fontStyle = FontStyle.Normal,
                normal = new GUIStyleState()
                {
                    textColor = Color.white,
                }
            };

            unityLogStyle = new GUIStyle()
            {
                fontSize = (int)logItemHeight,
                fontStyle = FontStyle.Normal,
                normal = new GUIStyleState()
                {
                    textColor = Color.grey,
                }
            };

            warnStyle = new GUIStyle()
            {
                fontSize = (int)logItemHeight,
                fontStyle = FontStyle.Normal,
                normal = new GUIStyleState()
                {
                    textColor = Color.yellow,
                }
            };

            errorStyle = new GUIStyle()
            {
                fontSize = (int)logItemHeight,
                fontStyle = FontStyle.Normal,
                normal = new GUIStyleState()
                {
                    textColor = Color.red,
                }
            };
        }
    }

    public class ScreenLogItem
    {
        public string Condition { get; set; }
        public ScreenLogType Type { get; set; }
        public bool IsUnityLog { get; set; } = false;
        public string Identifier { get; set; }
        public GUIStyle Style;
        public DateTime Time { get; set; }
        public float EndTime { get; set; }
    }


    public enum ScreenLogType
    {
        Log,
        Warning,
        Error,
    }
}
