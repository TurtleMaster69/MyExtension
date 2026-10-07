using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Telescope.Finders;
using ImplementationHit = Telescope.Finders.DefinitionHit;

namespace Telescope.Overlay
{
    /// <summary>
    /// The per-finder results-column catalogs (the user's 2026-10-04 column decision:
    /// implement EVERY cataloged column; the marked subset is DefaultVisible=true).
    /// Looked up by the finder's <c>Name</c> ("Files"/"Issues"/"References"/"Grep"/"Fzf"/
    /// "Implementation"/"Recent" — the exact IFinder.Name constants). Pure data — the getters only
    /// read the hit models; a null/foreign payload yields the empty cell. The narrow
    /// kind/access cells render the D2a abbreviated forms via <see cref="KindAbbreviations"/>
    /// (the Display strings keep the long forms — untouched, plan D3).
    /// </summary>
    internal static class FinderColumns
    {
        // ---- cell helpers ----------------------------------------------------------

        /// <summary>Types the payload once; a null/foreign payload yields the empty cell.</summary>
        private static string Cell<THit>(object? payload, Func<THit, string> get)
            => payload is THit hit ? get(hit) : string.Empty;

        private static string BaseName(FileLocation hit) => Path.GetFileName(hit.FilePath);

        private static string Line(FileLocation hit) => hit.LineNumber.ToString(CultureInfo.InvariantCulture);

        // ---- shared column builders (m41/BP-16) ------------------------------------

        /// <summary>The shared <c>file</c> column builder (id/header/min/max/truncation/
        /// defaultVisible + the cell getter). The width kind derives from max (max ==
        /// int.MaxValue → the absorbing flexible column).</summary>
        internal static ResultColumn FileColumn<THit>(string id, string header, int min, int max, ResultColumnTruncation truncation, bool defaultVisible, Func<THit, string> getter)
            where THit : FileLocation
            => new ResultColumn(id, header, min, max, truncation, defaultVisible, p => Cell<THit>(p, getter));

        /// <summary>The shared <c>line</c> column builder.</summary>
        internal static ResultColumn LineColumn<THit>(string id, string header, int min, int max, ResultColumnTruncation truncation, bool defaultVisible, Func<THit, string> getter)
            where THit : FileLocation
            => new ResultColumn(id, header, min, max, truncation, defaultVisible, p => Cell<THit>(p, getter));

        /// <summary>The shared <c>text</c> column builder.</summary>
        internal static ResultColumn TextColumn<THit>(string id, string header, int min, int max, ResultColumnTruncation truncation, bool defaultVisible, Func<THit, string> getter)
            where THit : FileLocation
            => new ResultColumn(id, header, min, max, truncation, defaultVisible, p => Cell<THit>(p, getter));

        /// <summary>The shared <c>kind</c> column builder.</summary>
        internal static ResultColumn KindColumn<THit>(string id, string header, int min, int max, ResultColumnTruncation truncation, bool defaultVisible, Func<THit, string> getter)
            where THit : FileLocation
            => new ResultColumn(id, header, min, max, truncation, defaultVisible, p => Cell<THit>(p, getter));

        // ---- per-finder catalogs (catalog order = the plan's catalog table order) ----

        internal static IReadOnlyList<ResultColumn> Files() =>
            // The Files shape (file+dir visible, path hidden); the dir cell is the FULL
            // containing directory always (m47/BP-19 — the projectRoot root-trim is deleted).
            FileDirPathColumns<FileHit>(h => Path.GetDirectoryName(h.FilePath) ?? string.Empty);

        internal static IReadOnlyList<ResultColumn> Recent() =>
            // The Files shape (file+dir visible, path hidden); the dir cell is the FULL
            // directory always (m37/BP-15 — RecentFileHit is merged into FileHit, so both
            // catalogs render FileHit and share the identical full-dir behavior).
            FileDirPathColumns<FileHit>(h => Path.GetDirectoryName(h.FilePath) ?? string.Empty);

        /// <summary>
        /// The shared Files/Recent catalog shape (m28/BP-18): file+dir visible, path hidden.
        /// Parameterized by the hit type; the dir cell is the full containing directory for
        /// BOTH catalogs (m47/BP-19 — no root trim remains). The dir column is a TRUE absorber
        /// (MaxWidth == int.MaxValue, m46/BP-31) so the exact-total invariant holds at a wide list.
        /// </summary>
        private static IReadOnlyList<ResultColumn> FileDirPathColumns<THit>(Func<THit, string> dirGetter) where THit : FileLocation => new[]
        {
            FileColumn<THit>("file", "File", 6, 30, ResultColumnTruncation.Tail, true, h => BaseName(h)),
            FileColumn<THit>("dir", "Directory", 6, int.MaxValue, ResultColumnTruncation.Tail, true, dirGetter),
            FileColumn<THit>("path", "Path", 10, 60, ResultColumnTruncation.Tail, false, h => h.FilePath),
        };

        internal static IReadOnlyList<ResultColumn> Issues() => new[]
        {
            KindColumn<CodeIssue>("kind", "Kind", 3, 8, ResultColumnTruncation.End, true, i => KindAbbreviations.Issue(i.Kind)),
            FileColumn<CodeIssue>("file", "File", 6, 30, ResultColumnTruncation.Tail, true, i => BaseName(i)),
            TextColumn<CodeIssue>("message", "Message", 10, int.MaxValue, ResultColumnTruncation.End, true, i => i.Text),
            LineColumn<CodeIssue>("line", "Line", 2, 5, ResultColumnTruncation.End, false, i => Line(i)),
        };

        internal static IReadOnlyList<ResultColumn> References() => new[]
        {
            new ResultColumn("access", "Access", 2, 4, ResultColumnTruncation.End, true,
                p => Cell<ReferenceHit>(p, r => KindAbbreviations.Access(r.IsWrite))),
            FileColumn<ReferenceHit>("file", "File", 6, 30, ResultColumnTruncation.Tail, true, r => BaseName(r)),
            new ResultColumn("symbol", "Symbol", 6, 24, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => r.Symbol)),
            new ResultColumn("column", "Column", 2, 8, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => r.Column.ToString(CultureInfo.InvariantCulture))),
            LineColumn<ReferenceHit>("line", "Line", 2, 5, ResultColumnTruncation.End, false, r => Line(r)),
            TextColumn<ReferenceHit>("text", "Line text", 10, int.MaxValue, ResultColumnTruncation.End, false, r => r.LineText),
        };

        internal static IReadOnlyList<ResultColumn> Grep() => GrepFzf();

        internal static IReadOnlyList<ResultColumn> Fzf() => GrepFzf();

        /// <summary>The shared Grep/Fzf catalog (m27/BP-17 — byte-identical post-m25, both use GrepHit).</summary>
        private static IReadOnlyList<ResultColumn> GrepFzf() => new[]
        {
            FileColumn<GrepHit>("file", "File", 6, 30, ResultColumnTruncation.Tail, true, h => BaseName(h)),
            LineColumn<GrepHit>("line", "Line", 2, 5, ResultColumnTruncation.End, true, h => Line(h)),
            TextColumn<GrepHit>("text", "Line text", 10, int.MaxValue, ResultColumnTruncation.End, true, h => h.LineText),
        };

        internal static IReadOnlyList<ResultColumn> Implementation() => new[]
        {
            KindColumn<ImplementationHit>("kind", "Kind", 3, 8, ResultColumnTruncation.End, true, h => KindAbbreviations.Implementation(h.Kind)),
            FileColumn<ImplementationHit>("file", "File", 6, 30, ResultColumnTruncation.Tail, true, h => BaseName(h)),
            TextColumn<ImplementationHit>("symbol", "Symbol", 6, int.MaxValue, ResultColumnTruncation.End, false, h => h.SymbolName),
            LineColumn<ImplementationHit>("line", "Line", 2, 5, ResultColumnTruncation.End, false, h => Line(h)),
        };

        // ---- the data-driven lookup (m45/BP-18) -------------------------------------

        /// <summary>m45 (BP-18): the capability seam — ForFinder is a Dictionary keyed by the
        /// finder <c>Name</c> (ordinal), not a hardcoded switch.</summary>
        internal static bool UsesDataDrivenLookup => true;

        private static readonly Dictionary<string, Func<IReadOnlyList<ResultColumn>>> Catalog =
            new Dictionary<string, Func<IReadOnlyList<ResultColumn>>>(StringComparer.Ordinal)
            {
                ["Files"] = Files,
                ["Recent"] = Recent,
                ["Issues"] = Issues,
                ["References"] = References,
                ["Grep"] = Grep,
                ["Fzf"] = Fzf,
                ["Implementation"] = Implementation,
            };

        /// <summary>
        /// The column catalog for a finder, keyed by its <c>IFinder.Name</c> (ordinal,
        /// case-sensitive). An unknown name yields an empty catalog.
        /// </summary>
        internal static IReadOnlyList<ResultColumn> ForFinder(string finderName)
        {
            return Catalog.TryGetValue(finderName ?? string.Empty, out var factory)
                ? factory()
                : Array.Empty<ResultColumn>();
        }
    }
}
