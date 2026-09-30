using System;

namespace Telescope
{
    /// <summary>
    /// Formats the overlay's filter-failure diagnostic line: the exact
    /// <c>[Telescope] filter failed: {ex.Message}</c> format the harness asserts on. The prefix is
    /// included here (via <see cref="DiagnosticLog.Telescope"/>), so callers must log through
    /// <see cref="NeoVisualLog.Log"/> — NOT <see cref="TelescopeLog"/> (which would double-prefix).
    /// </summary>
    internal static class FilterFailureLog
    {
        public static string Format(Exception ex) => DiagnosticLog.Telescope + "filter failed: " + ex.Message;
    }
}
