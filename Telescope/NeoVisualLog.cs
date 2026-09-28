using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.IO;

namespace Telescope
{
    /// <summary>
    /// The single diagnostic logger for the extension. Every line is written to:
    /// <list type="bullet">
    /// <item>the Visual Studio Output window pane <b>NeoVisual</b>, and</item>
    /// <item>a plain-text log file at <c>%APPDATA%\MyExtension\neovisual.log</c> (see <see cref="LogFileWriter"/>), and</item>
    /// <item>the debugger output (<see cref="Debug.WriteLine"/>).</item>
    /// </list>
    ///
    /// <para/>
    /// <b>File per run:</b> call <see cref="Clear"/> at the start of each run (package init / overlay
    /// open) so the file reflects only the current session — the solo harness
    /// (<c>tools/iterate-telescope.ps1</c>) clears it and greps it for assertions.
    ///
    /// <para/>
    /// <b>Threading:</b> the pane is created lazily on the UI thread; writes use
    /// <c>OutputStringThreadSafe</c>, so logging is safe from any thread. Logging never throws.
    /// </summary>
    public static class NeoVisualLog
    {
        private static Guid PaneGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
        private static IVsOutputWindowPane? _pane;
        private static bool _paneInitTried;

        /// <summary>NeoVisual structured log file path (delegates to <see cref="LogFileWriter"/>).</summary>
        public static string LogPath
        {
            get => LogFileWriter.LogPath;
            set => LogFileWriter.LogPath = value;
        }

        /// <summary>Raw debug-output file path (delegates to <see cref="LogFileWriter"/>).</summary>
        public static string DebugLogPath
        {
            get => LogFileWriter.DebugLogPath;
            set => LogFileWriter.DebugLogPath = value;
        }

        /// <summary>
        /// Points both per-run log files at <c>&lt;dir&gt;\&lt;runIndex&gt;-neovisual-&lt;main|exp&gt;.log</c>.
        /// The run index is the leading filename component so sorting by name groups the paired
        /// main (debug output) and exp (NeoVisual log) files of each run together; the suffix
        /// distinguishes the two streams. Never throws.
        /// </summary>
        public static void ConfigureLogPath(string dir, string runIndex)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                DebugLogPath = Path.Combine(dir, $"{runIndex}-neovisual-main.log");
                LogPath = Path.Combine(dir, $"{runIndex}-neovisual-exp.log");
            }
            catch
            {
                // keep whatever path was already set
            }
        }

        /// <summary>Truncates the log file (start of a run). Never throws.</summary>
        public static void Clear() => LogFileWriter.Clear();

        /// <summary>
        /// Writes a diagnostic line through the full facade pipeline (structured log file, debug
        /// output, and the NeoVisual pane). A documented alias for <see cref="Log"/> — the Log body
        /// already routes through <see cref="Debug.WriteLine"/> internally, so this must NOT call
        /// <c>Debug.WriteLine</c> itself (that would double-write every message to the debug-output
        /// file). The text of each line is byte-identical to the old <c>Debug.WriteLine</c> call.
        /// </summary>
        public static void Debug(string message) => Log(message);

        /// <summary>Flushes and closes the log writers (package shutdown). Never throws.</summary>
        public static void Close() => LogFileWriter.Close();

        /// <summary>
        /// Attaches the debug-output trace listener so every <c>Debug.WriteLine</c> in the
        /// extension lands in the per-run log file too. Call once at package init (after
        /// <see cref="Clear"/>). Idempotent.
        /// </summary>
        public static void InstallDebugListener()
        {
            foreach (TraceListener listener in System.Diagnostics.Debug.Listeners)
            {
                if (listener is NeoVisualTraceListener)
                {
                    return;
                }
            }
            System.Diagnostics.Debug.Listeners.Add(new NeoVisualTraceListener());
        }

        /// <summary>Writes a timestamped line to the structured NeoVisual log file, the NeoVisual pane, and the debugger.</summary>
        public static void Log(string message)
        {
            // The structured log goes to the NeoVisual file (LogFileWriter.Write) AND the debugger
            // output (Debug.WriteLine -> NeoVisualTraceListener -> the debug-output file). So a
            // line here lands in BOTH per-run files: the exp (NeoVisual) file and the main
            // (debug) file, keeping them comparable.
            LogFileWriter.Write(message);
            System.Diagnostics.Debug.WriteLine(message);
            WriteToPane(message);
        }

        private static void WriteToPane(string message)
        {
            try
            {
                EnsurePane();
                _pane?.OutputStringThreadSafe(message + Environment.NewLine);
            }
            catch
            {
                // pane unavailable — file/debug output still captured
            }
        }

        private static void EnsurePane()
        {
            if (_pane != null || _paneInitTried)
            {
                return;
            }
            _paneInitTried = true;

            // Creating the pane requires the UI thread; if we're not on it yet, skip the pane
            // for this write (the file still gets the line) and let a later UI-thread call retry.
            if (!ThreadHelper.CheckAccess())
            {
                _paneInitTried = false;
                return;
            }

            try
            {
                var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow == null)
                {
                    return;
                }
                outputWindow.CreatePane(ref PaneGuid, "NeoVisual", 1, 1);
                outputWindow.GetPane(ref PaneGuid, out _pane);
            }
            catch
            {
                _pane = null;
            }
        }
    }
}