using System;

namespace Telescope.Logging
{
    /// <summary>
    /// Formats the overlay's filter-failure diagnostic line: the exact
    /// <c>[Telescope] filter failed: {ex.Message}</c> format the harness asserts on. The message is
    /// returned UNPREFIXED — callers must log through <see cref="TelescopeLog.Log"/> (which adds the
    /// <c>[Telescope] </c> prefix). Logging through <see cref="NeoVisualLog.Log"/> directly would
    /// emit the line without the prefix.
    /// </summary>
    internal static class FilterFailureLog
    {
        public static string Format(Exception ex) => "filter failed: " + ex.Message;
    }
}
