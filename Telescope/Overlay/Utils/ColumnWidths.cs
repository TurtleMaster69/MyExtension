using System;
using System.Collections.Generic;
using System.Linq;

namespace Telescope.Overlay
{
    /// <summary>
    /// The pure results-column width engine (plan D2/D3). No WPF/VS dependencies — the
    /// overlay delegates here (the OverlayKeyHandler pattern) and the unit tests pin the
    /// distribution. Inputs/outputs are DEVICE-INDEPENDENT PIXELS; the char model converts
    /// at <see cref="PixelsPerChar"/> (the landed 8px/char factor, TelescopeOverlay
    /// RebuildColumns). Pinned rules (plan D2):
    /// - every column starts at its MinWidth and never goes below it, never above its
    ///   MaxWidth (MaxWidth == int.MaxValue marks the ONE absorbing column per catalog —
    ///   the landed Flexible kind);
    /// - the surplus is distributed in PRIORITY ORDER (MaxWidth ascending, ties stable in
    ///   catalog order — LINQ OrderBy is stable): the narrow semantic columns reach their
    ///   max first, the absorber takes whatever remains;
    /// - EXACT-TOTAL INVARIANT: when the catalog has an absorber (every real catalog does),
    ///   the assigned widths sum to availableWidth EXACTLY; without an absorber (defensive,
    ///   unreachable today) the surplus is dropped and the total is the sum of the maxes;
    /// - DEGENERATE BRANCH: availableWidth &lt;= NeededWidth(columns) → every column gets its
    ///   MinWidth (the total overshoots; <see cref="NeededWidth"/> IS the reported needed
    ///   width — the caller widens the window per D3, which makes the branch unreachable
    ///   except at the work-area cap).
    /// </summary>
    internal static class ColumnWidths
    {
        /// <summary>DIPs per character cell (the landed RebuildColumns factor).</summary>
        internal const double PixelsPerChar = 8.0;

        /// <summary>The preview pane's minimum width (DIPs) — more columns grow the WINDOW,
        /// they never eat the preview (the user's R4).</summary>
        internal const double PreviewMinWidth = 480.0;

        /// <summary>The overlay chrome around the results list: the root border (2) + slack (20).</summary>
        internal const double ChromeWidth = 22.0;

        /// <summary>The vertical-scrollbar allowance inside the results list (the landed
        /// ApplyFlexibleColumnWidth's 18px).</summary>
        internal const int VerticalScrollbarWidth = 18;

        /// <summary>The landed default overlay width (TelescopeOverlay ctor: Width = 760).</summary>
        internal const double DefaultOverlayWidth = 760.0;

        /// <summary>The sum of the columns' min widths (DIPs) — the width the results list
        /// needs for nothing to sit below its min; the degenerate branch's needed width.</summary>
        internal static double NeededWidth(IReadOnlyList<ResultColumn> columns)
        {
            if (columns == null || columns.Count == 0) return 0;
            return columns.Sum(c => c.MinWidth * PixelsPerChar);
        }

        /// <summary>
        /// The per-column widths (DIPs, aligned with <paramref name="columns"/>) that fit
        /// <paramref name="availableWidth"/> exactly (the exact-total invariant). See the
        /// class doc for the pinned distribution rules.
        /// </summary>
        internal static IReadOnlyList<double> Compute(double availableWidth, IReadOnlyList<ResultColumn> columns)
        {
            int n = columns?.Count ?? 0;
            var widths = new double[n];
            if (n == 0) return widths;

            double needed = NeededWidth(columns);
            if (availableWidth <= needed)
            {
                // Degenerate: the MINs win (the caller widens the window per D3).
                for (int i = 0; i < n; i++) widths[i] = columns[i].MinWidth * PixelsPerChar;
                return widths;
            }

            for (int i = 0; i < n; i++) widths[i] = columns[i].MinWidth * PixelsPerChar;
            double surplus = availableWidth - needed;
            var order = Enumerable.Range(0, n).OrderBy(i => columns[i].MaxWidth).ToList();
            foreach (int i in order)
            {
                if (surplus <= 0) break;
                double maxPx = columns[i].MaxWidth == int.MaxValue
                    ? double.MaxValue
                    : columns[i].MaxWidth * PixelsPerChar;
                double take = Math.Min(surplus, maxPx - widths[i]);
                if (take <= 0) continue;
                widths[i] += take;
                surplus -= take;
            }
            return widths;
        }

        /// <summary>
        /// The overlay window width for the visible column set (plan D3):
        /// max(default, NeededWidth + scrollbar + preview + chrome), capped by the work
        /// area. Pure — <paramref name="workAreaWidth"/> is injected (the call site reads
        /// SystemParameters.WorkArea.Width) so this stays unit-testable.
        /// </summary>
        internal static double WindowWidth(double defaultWidth, IReadOnlyList<ResultColumn> visibleColumns, double workAreaWidth)
        {
            double needed = NeededWidth(visibleColumns) + VerticalScrollbarWidth + PreviewMinWidth + ChromeWidth;
            return Math.Min(Math.Max(defaultWidth, needed), workAreaWidth);
        }

        /// <summary>The results list's pixel width for an overlay width (the D3 complement:
        /// everything except the preview + chrome).</summary>
        internal static double ResultsListWidth(double overlayWidth)
            => Math.Max(120, overlayWidth - PreviewMinWidth - ChromeWidth);
    }
}
