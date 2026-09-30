using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Telescope
{
    /// <summary>
    /// Pure, dependency-free file writer for the diagnostic logs. No VS assemblies — kept separate
    /// so it can be unit-tested hermetically and reused without VS types loaded.
    ///
    /// <para/>
    /// Two files are written per run, both sharing the same run index:
    /// <list type="bullet">
    /// <item><see cref="DebugLogPath"/> — the raw debug output stream (every <c>Debug.WriteLine</c>).</item>
    /// <item><see cref="LogPath"/> — the structured NeoVisual log lines (<see cref="NeoVisualLog.Log"/>).</item>
    /// </list>
    ///
    /// <para/>
    /// <b>Buffered:</b> each file is written through a kept-open <see cref="StreamWriter"/> (lazily
    /// opened on first write, UTF-8 without BOM, <c>FileShare.Read</c> so the e2e harness can
    /// <c>Get-Content</c> the file while VS holds it open). Lines reach disk on an explicit
    /// <see cref="Flush"/>, <see cref="Close"/>, or the ~200ms background timer.
    /// </summary>
    internal static class LogFileWriter
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> _clearedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _failureMarkerWritten;

        private static StreamWriter? _logWriter;
        private static StreamWriter? _debugWriter;
        private static Timer? _flushTimer;

        /// <summary>Number of times the buffered writers have been flushed (test seam for the one-shot timer).</summary>
        internal static int FlushCount;

        /// <summary>Number of write failures swallowed by the never-throw contract (test seam).</summary>
        internal static int WriteFailureCount;

        private static string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyExtension",
            "neovisual.log");

        private static string _debugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyExtension",
            "neovisual-debug.log");

        /// <summary>Path of the NeoVisual structured log file. Changing it closes + reopens the writer.</summary>
        public static string LogPath
        {
            get => _logPath;
            set
            {
                lock (Sync)
                {
                    if (string.Equals(_logPath, value, StringComparison.Ordinal))
                    {
                        return;
                    }
                    CloseWriter(ref _logWriter);
                    _logPath = value;
                }
            }
        }

        /// <summary>Path of the raw debug-output file (all Debug.WriteLine). Changing it closes + reopens the writer.</summary>
        public static string DebugLogPath
        {
            get => _debugLogPath;
            set
            {
                lock (Sync)
                {
                    if (string.Equals(_debugLogPath, value, StringComparison.Ordinal))
                    {
                        return;
                    }
                    CloseWriter(ref _debugWriter);
                    _debugLogPath = value;
                }
            }
        }

        /// <summary>Truncates both log files. Never throws.</summary>
        public static void Clear()
        {
            // Per-path idempotent: each unique path truncates on its first Clear() for that
            // path, so a package re-init (background load etc.) never wipes the lines already
            // captured for this run, while tests can Clear() distinct temp paths in any order.
            lock (Sync)
            {
                FlushLocked();
                CloseWriter(ref _logWriter);
                CloseWriter(ref _debugWriter);
                ClearFileOnce(LogPath);
                ClearFileOnce(DebugLogPath);
            }
        }

        private static void ClearFileOnce(string path)
        {
            if (_clearedPaths.Add(path))
            {
                ClearFile(path);
            }
        }

        /// <summary>Appends a timestamped line to the NeoVisual structured log. Never throws.</summary>
        public static void Write(string message) => WriteTo(ref _logWriter, LogPath, message);

        /// <summary>Appends a timestamped line to the debug-output log. Never throws.</summary>
        public static void WriteDebug(string message) => WriteTo(ref _debugWriter, DebugLogPath, message);

        private static void WriteTo(ref StreamWriter? writer, string path, string message)
        {
            string line = FormatLine(message);
            lock (Sync)
            {
                try
                {
                    GetWriter(ref writer, path).Write(line + Environment.NewLine);
                }
                catch
                {
                    WriteFailureCount++;
                    WriteFailureMarker();
                }
            }
        }

        /// <summary>Flushes both buffered writers to disk. Never throws.</summary>
        internal static void Flush()
        {
            lock (Sync)
            {
                FlushLocked();
            }
        }

        /// <summary>Flushes + disposes both writers and the flush timer. Never throws.</summary>
        internal static void Close()
        {
            lock (Sync)
            {
                try
                {
                    _flushTimer?.Dispose();
                    _flushTimer = null;
                }
                catch
                {
                    // never let logging break the extension
                }
                CloseWriter(ref _logWriter);
                CloseWriter(ref _debugWriter);
            }
        }

        private static string FormatLine(string message)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} {1}", DateTime.Now, message);
        }

        private static void FlushLocked()
        {
            FlushCount++;
            try
            {
                _logWriter?.Flush();
                _debugWriter?.Flush();
            }
            catch
            {
                // never let logging break the extension
            }
        }

        private static StreamWriter GetWriter(ref StreamWriter? writer, string path)
        {
            if (writer == null)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                writer = new StreamWriter(
                    new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false));
                EnsureTimer();
            }
            _flushTimer?.Change(200, Timeout.Infinite);
            return writer;
        }

        private static void EnsureTimer()
        {
            if (_flushTimer != null)
            {
                return;
            }
            _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private static void WriteFailureMarker()
        {
            if (_failureMarkerWritten)
            {
                return;
            }
            _failureMarkerWritten = true;
            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    File.WriteAllText(Path.Combine(dir, "neovisual-write-failed"), "write failed");
                }
            }
            catch
            {
                // never let logging break the extension
            }
        }

        private static void CloseWriter(ref StreamWriter? writer)
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch
            {
                // never let logging break the extension
            }
            writer = null;
        }

        private static void ClearFile(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, string.Empty);
            }
            catch
            {
                // never let logging break the extension
            }
        }
    }
}
