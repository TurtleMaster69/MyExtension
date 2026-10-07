using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.IO;

namespace Telescope.Logging
{
    /// <summary>
    /// The single diagnostic logger for the extension. Every line is written to:
    /// <list type="bullet">
    /// <item>the Visual Studio Output window pane <b>NeoVisual</b>, and</item>
    /// <item>a plain-text log file at <c>%APPDATA%\MyExtension\neovisual.log</c> (see <see cref="LogFileWriter"/>), and</item>
    /// <item>the debugger output (<see cref="Debug.WriteLine"/>) — opt-in via
    /// <c>NEOVISUAL_DEBUG_DUP=1</c> (N65/BP-61).</item>
    /// </list>
    ///
    /// <para/>
    /// <b>File per run:</b> call <see cref="Clear"/> once per process (package init only) so the
    /// file reflects only the current session — the solo harness
    /// (<c>tools/iterate-telescope.ps1</c>) clears it and greps it for assertions.
    ///
    /// <para/>
    /// <b>Threading:</b> the pane is created lazily on the UI thread; writes use
    /// <c>OutputStringThreadSafe</c>, so logging is safe from any thread. Logging never throws.
    /// </summary>
    public static class NeoVisualLog
    {
        private static readonly Guid PaneGuid = new Guid("0d4f65d8-2971-4c24-8e1d-6bafc905c97e");
        private static readonly object PaneSync = new object();
        private static readonly PaneFailureTracker _paneFailureTracker = new PaneFailureTracker();
        private static IVsOutputWindowPane? _pane;

        // N65/BP-61: the Debug.WriteLine duplication (-> NeoVisualTraceListener -> the debug-output
        // file) is opt-in so every NeoVisualLog line is not unconditionally duplicated.
        private static readonly bool DebugDuplicationEnabled = IsDebugDuplicationEnabled();

        private static bool IsDebugDuplicationEnabled()
        {
            string? value = Environment.GetEnvironmentVariable("NEOVISUAL_DEBUG_DUP");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

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
        /// main (debug output) and exp (NeoVisual log) files of each run together. The suffix is
        /// hardcoded here (NOT command-line-detected): <c>-exp.log</c> is the structured NeoVisual
        /// log the harness asserts on, <c>-main.log</c> is the raw debug-output stream. Never throws.
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
            // The structured line always goes to the NeoVisual file (LogFileWriter.Write). The
            // debug-output file receives it ONLY when DebugDuplicationEnabled is set (opt-in) —
            // the Debug.WriteLine duplication is gated, not unconditional.
            LogFileWriter.Write(message);
            if (DebugDuplicationEnabled)
            {
                System.Diagnostics.Debug.WriteLine(message);
            }
            WriteToPane(message);
        }

        private static void WriteToPane(string message)
        {
            try
            {
                EnsurePane();
                IVsOutputWindowPane? pane;
                lock (PaneSync)
                {
                    pane = _pane;
                }
                pane?.OutputStringThreadSafe(message + Environment.NewLine);
            }
            catch
            {
                // Pane unavailable — the file/debug output is still captured. Emit a one-time
                // fallback line to the FILE only (never Log, which would re-enter WriteToPane).
                if (_paneFailureTracker.ShouldEmit())
                {
                    LogFileWriter.Write(_paneFailureTracker.FallbackMessage("pane unavailable"));
                }
            }
        }

        private static void EnsurePane()
        {
            // n19 (BP-11): hold PaneSync across the pane creation so two threads cannot both
            // create the pane (the old double-checked locking released the lock between the
            // null-check and the creation — a benign race that could create two panes).
            lock (PaneSync)
            {
                if (_pane != null)
                {
                    return;
                }

                // Creating the pane requires the UI thread; if we're not on it yet, skip the pane
                // for this write (the file still gets the line) and let a later UI-thread call retry.
                if (!ThreadHelper.CheckAccess())
                {
                    return;
                }

                try
                {
                    var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                    if (outputWindow == null)
                    {
                        // M12: a null GetGlobalService result (pre-package-init) must not permanently
                        // disable the pane — a later UI-thread call retries.
                        return;
                    }
                    Guid paneGuid = PaneGuid;
                    outputWindow.CreatePane(ref paneGuid, "NeoVisual", fInitVisible: 1, fClearWithSolution: 1);
                    outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane? pane);
                    _pane = pane;
                }
                catch
                {
                    _pane = null;
                }
            }
        }
    }
}