using System;

namespace Telescope.Logging
{
    /// <summary>
    /// Formats the overlay's filter-failure diagnostic line: the exact
    /// <c>[Telescope] filter failed: {ex.Message}</c> format the harness asserts on. The message is
    /// returned PREFIXED (self-contained) so a wrong logger cannot double-prefix or silently break
    /// the <c>filter failed:</c> contract; callers log it through <see cref="NeoVisualLog.Log"/>
    /// (which adds no prefix). n5 (BP-17): this self-contained-prefix seam is the documented
    /// contract — a caller must never re-prefix the returned string.
    /// </summary>
    internal static class FilterFailureLog
    {
        // m43 (BP-30): sanitize ex.Message (control chars -> spaces) so a newline can never split
        // the [Telescope] filter failed: {msg} line.
        public static string Format(Exception ex) => DiagnosticLog.Telescope + "filter failed: " + DiagnosticLog.SanitizeText(ex.Message);
    }
}
