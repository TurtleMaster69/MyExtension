using System;

namespace Telescope.Logging
{
    /// <summary>
    /// Formats the overlay's filter-failure diagnostic line: the exact
    /// <c>[Telescope] filter failed: {ex.Message}</c> format the harness asserts on. The message is
    /// returned PREFIXED (self-contained) so a wrong logger cannot silently break the
    /// <c>filter failed:</c> contract; callers log it through <see cref="NeoVisualLog.Log"/> (which
    /// adds no prefix).
    /// </summary>
    internal static class FilterFailureLog
    {
        public static string Format(Exception ex) => DiagnosticLog.Telescope + "filter failed: " + ex.Message;
    }
}
