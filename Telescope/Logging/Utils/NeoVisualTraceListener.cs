using System.Diagnostics;

namespace Telescope.Logging
{
    /// <summary>
    /// A <see cref="TraceListener"/> that forwards every <see cref="Debug.WriteLine"/> from the
    /// extension into the debug-output log file (<see cref="LogFileWriter.DebugLogPath"/>). Attached
    /// at package init so the per-run file captures BOTH the NeoVisual log lines AND the raw Debug
    /// output that other classes (InputHandler, WindowNavigator, ...) write directly.
    ///
    /// <para/>
    /// <b>No recursion:</b> <see cref="NeoVisualLog.Log"/> calls <c>Debug.WriteLine</c> (routed
    /// here) and writes to the Output pane, but this listener only calls
    /// <see cref="LogFileWriter.WriteDebug"/> — it never calls back into Debug or NeoVisualLog.
    /// </summary>
    internal sealed class NeoVisualTraceListener : TraceListener
    {
        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                LogFileWriter.WriteDebug(message);
            }
        }

        public override void WriteLine(string? message) => Write(message);
    }
}