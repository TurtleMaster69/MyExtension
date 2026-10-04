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
    }
}
