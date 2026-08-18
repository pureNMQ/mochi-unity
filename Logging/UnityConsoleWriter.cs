using UnityEngine;
using Mochi.Logging;

namespace Mochi.Unity.Logging
{
    public class UnityConsoleWriter : ILogWriter
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        public void WriteLog(LogEntry entry)
        {
            if (entry.Level < MinLevel) return;

            var msg = FormatMessage(entry);

            switch (entry.Level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(msg);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning(msg);
                    break;
                case LogLevel.Error:
                    Debug.LogError(msg);
                    break;
            }
        }

        public void Flush() { }

        public void Dispose() { }

        private static string FormatMessage(LogEntry entry)
        {
            return string.IsNullOrEmpty(entry.Category) || entry.Category == "General"
                ? entry.Message
                : $"[{entry.Category}] {entry.Message}";
        }
    }
}
