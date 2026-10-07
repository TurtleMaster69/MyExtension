using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// m5 (BP-9): the results-log-on-change gate. Tracks the last-logged
    /// columns/count/selected/boxText and reports whether a change occurred, so the
    /// <c>results columns=</c>/<c>results count=</c> diagnostics fire on change only (a
    /// selection-only render that changes none of the four values no longer re-logs).
    /// Dependency-free (no WPF/VS) so it stays unit-testable.
    /// </summary>
    internal sealed class ResultsLogGate
    {
        private string? _columns;
        private int _count;
        private int _selected;
        private int _boxText;
        private bool _hasLogged;

        /// <summary>Returns true when the state differs from the last-logged state (or nothing
        /// has been logged yet); false when unchanged.</summary>
        public bool ShouldLog(string columns, int count, int selected, int boxText)
        {
            if (!_hasLogged
                || !string.Equals(_columns, columns, StringComparison.Ordinal)
                || _count != count
                || _selected != selected
                || _boxText != boxText)
            {
                _hasLogged = true;
                _columns = columns;
                _count = count;
                _selected = selected;
                _boxText = boxText;
                return true;
            }
            return false;
        }
    }
}
