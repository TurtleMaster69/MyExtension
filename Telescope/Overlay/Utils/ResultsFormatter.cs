using System.Collections.Generic;

namespace Telescope.Overlay
{
    /// <summary>
    /// Pure helpers for the results diagnostics. Kept as a standalone internal class (no WPF / VS
    /// dependencies) so it can be unit-tested hermetically by <c>tests/Telescope.Tests</c>. The
    /// results host is a ListView (the old read-only TextBox was replaced by the columned list),
    /// so the legacy <c>&gt; </c>-marked text render (<c>ToText</c>) is gone; this class now owns
    /// the single pure seam the overlay's diagnostics need: the byte-stable <c>boxText=</c> length
    /// (the rendered row-text length of the visible cells). The <c>results columns=</c> id-list
    /// format is inlined at the call site (<c>string.Join(",", ids)</c> — n16/BP-9).
    /// </summary>
    internal static class ResultsFormatter
    {
        /// <summary>
        /// The rendered row-text length the <c>results count=N selected=M boxText=L</c> diagnostic
        /// reports: the SUM of the visible cell text lengths across all rows (BP-11/D9 — the
        /// legacy 2-char selection-marker allowance + the <c>'\n'</c> separators of the RETIRED
        /// TextBox render are gone; the value is now a real rendered length). Empty input → 0.
        /// </summary>
        internal static int RenderedTextLength(IReadOnlyList<string[]> rows)
        {
            if (rows == null || rows.Count == 0) return 0;
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                string[] cells = rows[i];
                if (cells != null)
                {
                    for (int c = 0; c < cells.Length; c++) total += cells[c]?.Length ?? 0;
                }
            }
            return total;
        }
    }
}
