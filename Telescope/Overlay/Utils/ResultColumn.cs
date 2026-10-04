using System;
using System.Collections.Generic;
using System.Linq;

namespace Telescope.Overlay
{
    /// <summary>
    /// How a results column is sized (the Telescope.nvim <c>entry_display</c> pattern): a
    /// fixed character width, or the single flexible column that absorbs the remaining width.
    /// </summary>
    internal enum ResultColumnWidth
    {
        /// <summary>Fixed character width (<see cref="ResultColumn.WidthChars"/> is meaningful).</summary>
        Fixed,

        /// <summary>
        /// Flexible: absorbs the remaining width (Telescope.nvim's <c>remaining = true</c>).
        /// <see cref="ResultColumn.WidthChars"/> is 0 and ignored.
        /// </summary>
        Flexible,
    }

    /// <summary>
    /// One column in a finder's results list: a stable id (the lowercase-hyphenated token the
    /// <c>[Telescope] results columns=</c> diagnostic prints), a human header, a width kind,
    /// a cell getter over the row's payload (the finder's hit model), and the default
    /// visibility (the user's 2026-10-04 catalog marks). Pure data — no WPF/VS dependencies.
    /// </summary>
    internal sealed class ResultColumn
    {
        /// <summary>Stable lowercase-hyphenated id (e.g. <c>access</c>, <c>line</c>).</summary>
        internal string Id { get; }

        /// <summary>Human header shown in the GridView (e.g. <c>Access</c>, <c>Line text</c>).</summary>
        internal string Header { get; }

        /// <summary>Fixed-chars vs flexible-remaining (Telescope.nvim entry_display pattern).</summary>
        internal ResultColumnWidth Width { get; }

        /// <summary>Fixed width in characters; 0 and ignored when <see cref="Width"/> is Flexible.</summary>
        internal int WidthChars { get; }

        /// <summary>Whether the column starts visible (the user's catalog marks).</summary>
        internal bool DefaultVisible { get; }

        /// <summary>
        /// Cell getter: maps the row payload (the hit model, or null) to the cell text. A
        /// null/foreign payload yields the empty string — never throws.
        /// </summary>
        internal Func<object?, string> Getter { get; }

        internal ResultColumn(
            string id,
            string header,
            ResultColumnWidth width,
            int widthChars,
            bool defaultVisible,
            Func<object?, string> getter)
        {
            Id = id ?? string.Empty;
            Header = header ?? string.Empty;
            Width = width;
            WidthChars = width == ResultColumnWidth.Flexible ? 0 : widthChars;
            DefaultVisible = defaultVisible;
            Getter = getter ?? throw new ArgumentNullException(nameof(getter));
        }
    }

    /// <summary>
    /// The visibility state of one finder's column catalog: the ordered column ids (catalog
    /// order, immutable) plus the visible subset. Pure state machine — the header chooser
    /// (Section B/D4) toggles through <see cref="Toggle"/> and rebuilds the visible columns
    /// from <see cref="VisibleColumns"/>.
    ///
    /// <para/>Pinned rules (plan D2/D5):
    /// - ORDER STABILITY: <see cref="VisibleIds"/> is always the catalog order filtered to
    ///   the visible set, so a toggled-off-then-on column returns to its catalog position.
    /// - ALL-OFF RULE: the LAST visible column cannot be hidden — <see cref="Toggle"/> on
    ///   the only visible column is a no-op (returns false). The chooser may never leave
    ///   zero columns (an empty header row renders broken and the <c>results columns=</c>
    ///   diagnostic would become an ambiguous empty suffix).
    /// - ID FORMAT: <see cref="VisibleIdsJoined"/> is the comma-joined visible ids with no
    ///   spaces — the exact payload of <c>[Telescope] results columns=...</c>.
    /// </summary>
    internal sealed class ColumnVisibilityModel
    {
        private readonly List<ResultColumn> _catalog;
        private readonly HashSet<string> _visible;

        internal ColumnVisibilityModel(IReadOnlyList<ResultColumn> catalog)
        {
            _catalog = catalog.ToList();   // defensive copy — catalog order frozen here
            _visible = new HashSet<string>(
                _catalog.Where(c => c.DefaultVisible).Select(c => c.Id),
                StringComparer.Ordinal);
        }

        /// <summary>All column ids in catalog order (visible or not) — the chooser menu's source.</summary>
        internal IReadOnlyList<string> Ids => _catalog.Select(c => c.Id).ToList();

        /// <summary>All columns in catalog order.</summary>
        internal IReadOnlyList<ResultColumn> Catalog => _catalog;

        /// <summary>The visible columns in catalog order — Section B builds the GridView from this.</summary>
        internal IReadOnlyList<ResultColumn> VisibleColumns =>
            _catalog.Where(c => _visible.Contains(c.Id)).ToList();

        /// <summary>The visible column ids in catalog order — the <c>results columns=</c> payload.</summary>
        internal IReadOnlyList<string> VisibleIds =>
            _catalog.Where(c => _visible.Contains(c.Id)).Select(c => c.Id).ToList();

        /// <summary>
        /// The diagnostic id list: comma-joined visible ids, no spaces (e.g. <c>access,file</c>).
        /// Section B logs it verbatim: <c>TelescopeLog.Log($"results columns={model.VisibleIdsJoined}")</c>.
        /// </summary>
        internal string VisibleIdsJoined => string.Join(",", VisibleIds);

        internal bool IsVisible(string id) => _visible.Contains(id);

        /// <summary>
        /// Toggles a column's visibility. Returns true when the visibility CHANGED; false on a
        /// no-op (unknown id, or hiding the only visible column — the all-off rule).
        /// </summary>
        internal bool Toggle(string id)
        {
            if (string.IsNullOrEmpty(id) || _catalog.All(c => c.Id != id))
            {
                return false;
            }

            if (_visible.Contains(id))
            {
                if (_visible.Count == 1)
                {
                    return false;   // all-off rule: the last visible column cannot hide
                }

                _visible.Remove(id);
                return true;
            }

            _visible.Add(id);
            return true;
        }
    }
}
