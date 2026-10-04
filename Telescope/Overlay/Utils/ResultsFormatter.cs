using System.Collections.Generic;

namespace Telescope.Overlay
{
    /// <summary>
    /// Pure helpers for the results diagnostics. Kept as a standalone internal class (no WPF / VS
    /// dependencies) so it can be unit-tested hermetically by <c>tests/Telescope.Tests</c>. The
    /// results host is a ListView (the old read-only TextBox was replaced by the columned list),
    /// so the legacy <c>&gt; </c>-marked text render (<c>ToText</c>) is gone; this class now owns
    /// the two pure seams the overlay's diagnostics need: the byte-stable <c>boxText=</c> length
    /// (the rendered row-text length of the visible cells) and the <c>results columns=</c> id-list
    /// format.
    /// </summary>
    internal static class ResultsFormatter
    {
        /// <summary>
        /// The rendered row-text length the <c>results count=N selected=M boxText=L</c> diagnostic
        /// reports: per row, the legacy 2-char selection-marker allowance plus the sum of the
        /// visible cell text lengths, plus one newline separator between rows (the legacy
        /// <c>ToText</c> layout). Empty input → 0. For a degenerate single-column finder whose
        /// cell equals the entry's Display, the value is byte-identical to the legacy
        /// <c>ToText(results, selectedIndex).Length</c>.
        /// </summary>
        internal static int RenderedTextLength(IReadOnlyList<string[]> rows)
        {
            if (rows == null || rows.Count == 0) return 0;
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) total++;   // the legacy '\n' separator
                total += 2;           // the legacy "> "/"  " marker width
                string[] cells = rows[i];
                if (cells != null)
                {
                    for (int c = 0; c < cells.Length; c++) total += cells[c]?.Length ?? 0;
                }
            }
            return total;
        }

        /// <summary>
        /// The <c>results columns=</c> id list: the visible column ids, comma-separated, no
        /// spaces, in catalog order. Empty set → empty string (the line reads
        /// <c>results columns=</c>).
        /// </summary>
        internal static string ColumnsIdList(IReadOnlyList<string> visibleIds)
        {
            if (visibleIds == null || visibleIds.Count == 0) return string.Empty;
            return string.Join(",", visibleIds);
        }
    }
}
