using System.Collections.Generic;
using Telescope.Finders;

namespace Telescope.Overlay
{
    /// <summary>
    /// Computes one row's cell texts from a <see cref="FinderEntry"/>'s payload via the
    /// visible columns' getters. Pure — the ListView binding (Section B) delegates here,
    /// mirroring the OverlayKeyHandler pattern (the UI delegates to a dependency-free
    /// model). Display is untouched: fzf still filters the Display strings (plan D3).
    /// </summary>
    internal static class ResultRowCells
    {
        /// <summary>
        /// The ordered cell texts for one row: one string per visible column, same order.
        /// A null entry or a null/foreign payload yields empty cells (the getters never
        /// throw) — the row count stays aligned with the visible column count.
        /// </summary>
        internal static IReadOnlyList<string> Compute(FinderEntry? entry, IReadOnlyList<ResultColumn> visibleColumns)
        {
            object? payload = entry?.Payload;
            var cells = new string[visibleColumns.Count];
            for (int i = 0; i < visibleColumns.Count; i++)
            {
                cells[i] = visibleColumns[i].Getter(payload);
            }

            return cells;
        }

        /// <summary>
        /// The truncating overload (plan D4): the raw cells, then each cell shortened to its
        /// column's computed char width by the column's truncation kind. <paramref name="charWidths"/>
        /// is aligned with <paramref name="visibleColumns"/> (the ColumnWidths.Compute px widths
        /// divided by PixelsPerChar). A missing/short charWidths entry means NO truncation for
        /// that column (defensive).
        /// </summary>
        internal static IReadOnlyList<string> Compute(
            FinderEntry? entry,
            IReadOnlyList<ResultColumn> visibleColumns,
            IReadOnlyList<int> charWidths)
        {
            object? payload = entry?.Payload;
            var cells = new string[visibleColumns.Count];
            for (int i = 0; i < visibleColumns.Count; i++)
            {
                string raw = visibleColumns[i].Getter(payload);
                int width = charWidths != null && i < charWidths.Count ? charWidths[i] : int.MaxValue;
                cells[i] = ColumnTruncation.Apply(visibleColumns[i].Truncation, raw, width);
            }

            return cells;
        }
    }
}
