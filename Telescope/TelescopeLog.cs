namespace Telescope
{
    /// <summary>
    /// One-line prefix helper for the Telescope structured log: every call emits a
    /// <c>[Telescope] </c>-prefixed line via <see cref="NeoVisualLog"/>. The prefix is
    /// centralized in <see cref="DiagnosticLog.Telescope"/>, so the emitted text is
    /// byte-identical to the previous hand-written concatenation.
    /// </summary>
    internal static class TelescopeLog
    {
        public static void Log(string message) => NeoVisualLog.Log(DiagnosticLog.Telescope + message);
    }
}
