using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Mochi.Logging;

namespace Mochi.Unity.Logging
{
    public class FileWriter : ILogWriter
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Info;

        private readonly string _filePath;
        private readonly ConcurrentQueue<LogEntry> _queue;
        private readonly AutoResetEvent _signal;
        private readonly Thread _writeThread;
        private volatile bool _running;

        public FileWriter(string directory, string fileName, LogLevel minLevel = LogLevel.Info)
        {
            MinLevel = minLevel;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _filePath = Path.Combine(directory, fileName);
            _queue = new ConcurrentQueue<LogEntry>();
            _signal = new AutoResetEvent(false);
            _running = true;

            _writeThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "LogFileWriter"
            };
            _writeThread.Start();

            Application.quitting += OnApplicationQuitting;
        }

        public void WriteLog(LogEntry entry)
        {
            if (entry.Level < MinLevel) return;

            _queue.Enqueue(entry);
            _signal.Set();
        }

        public void Flush()
        {
            DrainQueue();
        }

        public void Dispose()
        {
            Application.quitting -= OnApplicationQuitting;
            Shutdown();
        }

        private void OnApplicationQuitting()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (!_running) return;
            _running = false;
            _signal.Set();
            _writeThread.Join(3000);
            DrainQueue();
        }

        private void WriteLoop()
        {
            while (_running)
            {
                _signal.WaitOne(2000);
                DrainQueue();
            }
        }

        private void DrainQueue()
        {
            if (_queue.IsEmpty) return;

            var sb = new StringBuilder();
            while (_queue.TryDequeue(out var entry))
            {
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [");
                sb.Append(entry.Level.ToString());
                sb.Append("] [");
                sb.Append(entry.Category);
                sb.Append("]: ");
                sb.AppendLine(entry.Message);
            }

            var text = sb.ToString();
            if (text.Length > 0)
            {
                try
                {
                    File.AppendAllText(_filePath, text);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileWriter] Failed to write log: {ex.Message}");
                }
            }
        }
    }
}
