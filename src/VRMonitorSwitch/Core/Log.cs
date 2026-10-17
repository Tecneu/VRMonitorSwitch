using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace VRMonitorSwitch.Core
{
    /// <summary>
    /// Minimal thread-safe file logger. Writes to
    /// <c>%LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log</c>, rotates at 2 MB keeping a single
    /// <c>.1</c> backup, and raises <see cref="LineWritten"/> so the UI can show a live tail.
    /// Never throws: logging must not be able to break the application.
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 2L * 1024 * 1024;
        private const int RecentCapacity = 500;

        private static readonly object _gate = new object();
        private static readonly Queue<string> _recent = new Queue<string>(RecentCapacity);
        private static readonly string _filePath = ResolvePath();
        private static bool _directoryReady;

        /// <summary>Full path of the active log file.</summary>
        public static string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>Raised (on the calling thread) after every line is written. Handler exceptions are swallowed.</summary>
        public static event Action<string> LineWritten;

        /// <summary>Snapshot of the most recent lines (max 500) kept in memory, oldest first.</summary>
        public static IReadOnlyList<string> RecentLines
        {
            get
            {
                lock (_gate)
                {
                    return new List<string>(_recent);
                }
            }
        }

        public static void Info(string msg)
        {
            Write("INFO ", msg, null);
        }

        public static void Warn(string msg)
        {
            Write("WARN ", msg, null);
        }

        public static void Error(string msg, Exception ex = null)
        {
            Write("ERROR", msg, ex);
        }

        private static string ResolvePath()
        {
            try
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Path.GetTempPath();
                }
                return Path.Combine(baseDir, "VRMonitorSwitch", "logs", "vrmonitorswitch.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "VRMonitorSwitch", "vrmonitorswitch.log");
            }
        }

        private static void Write(string level, string msg, Exception ex)
        {
            string line;
            try
            {
                var sb = new StringBuilder(128);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.Append(" [").Append(level).Append("] [T");
                sb.Append(Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture).PadLeft(2));
                sb.Append("] ");
                sb.Append(msg ?? string.Empty);
                if (ex != null)
                {
                    sb.Append(Environment.NewLine).Append("    ").Append(ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    "));
                }
                line = sb.ToString();
            }
            catch
            {
                line = (msg ?? string.Empty);
            }

            lock (_gate)
            {
                try
                {
                    EnsureDirectory();
                    RotateIfNeeded();
                    File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Disk full, path locked, permissions... nothing sensible to do; keep the in-memory tail.
                }

                try
                {
                    if (_recent.Count >= RecentCapacity)
                    {
                        _recent.Dequeue();
                    }
                    _recent.Enqueue(line);
                }
                catch
                {
                }
            }

            Action<string> handler = LineWritten;
            if (handler != null)
            {
                try
                {
                    handler(line);
                }
                catch
                {
                    // A faulty UI subscriber must never take the logger down.
                }
            }
        }

        private static void EnsureDirectory()
        {
            if (_directoryReady)
            {
                return;
            }
            string dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _directoryReady = true;
        }

        private static void RotateIfNeeded()
        {
            var fi = new FileInfo(_filePath);
            if (!fi.Exists || fi.Length < MaxBytes)
            {
                return;
            }

            string backup = _filePath + ".1";
            try
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
                File.Move(_filePath, backup);
            }
            catch
            {
                // If we cannot rotate (file locked by a viewer), truncate instead so the log cannot grow forever.
                try
                {
                    using (var fs = new FileStream(_filePath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite))
                    {
                    }
                }
                catch
                {
                }
            }
        }
    }
}
