using System;
using System.Globalization;
using System.IO;

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
    /// </summary>
    internal static class LogFileWriter
    {
        private static readonly object Sync = new object();
        private static bool _clearedThisProcess;

        /// <summary>Path of the NeoVisual structured log file.</summary>
        public static string LogPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyExtension",
            "neovisual.log");

        /// <summary>Path of the raw debug-output file (all Debug.WriteLine).</summary>
        public static string DebugLogPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyExtension",
            "neovisual-debug.log");

        /// <summary>Truncates both log files. Never throws.</summary>
        public static void Clear()
        {
            // Only truncate once per process — a package re-init (background load etc.) must not
            // wipe the lines already captured for this run.
            lock (Sync)
            {
                if (_clearedThisProcess)
                {
                    return;
                }
                _clearedThisProcess = true;

                ClearFile(LogPath);
                ClearFile(DebugLogPath);
            }
        }

        /// <summary>Appends a timestamped line to the NeoVisual structured log. Never throws.</summary>
        public static void Write(string message)
        {
            Append(LogPath, message);
        }

        /// <summary>Appends a timestamped line to the debug-output log. Never throws.</summary>
        public static void WriteDebug(string message)
        {
            Append(DebugLogPath, message);
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

        private static void Append(string path, string message)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} {1}", DateTime.Now, message);
            lock (Sync)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // never let logging break the extension
                }
            }
        }
    }
}