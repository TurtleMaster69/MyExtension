using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Telescope.Finders;

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

        /// <summary>
        /// The Files <c>dir</c> cell: the containing directory; with a project root, the tail
        /// after the root (ordinal-ignore-case); a file directly in the root yields ""; a file
        /// outside the root yields the full directory.
        /// </summary>
        private static string DirCell(object? payload, string? projectRoot)
        {
            if (payload is not FileHit hit)
            {
                return string.Empty;
            }

            string dir = Path.GetDirectoryName(hit.FilePath) ?? string.Empty;
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(dir))
            {
                return dir;
            }

            string root = projectRoot.TrimEnd('\\', '/');
            if (root.Length == 0)
            {
                return dir;
            }

            string prefix = root + "\\";
            if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return dir.Substring(prefix.Length);
            }

            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return dir;
        }

        // ---- per-finder catalogs (catalog order = the plan's catalog table order) ----

        internal static IReadOnlyList<ResultColumn> Files(string? projectRoot = null) => new[]
        {
            //                    id      header      width-kind                  chars  min  max  truncation  visible
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<FileHit>(p, h => BaseName(h))),
            new ResultColumn("dir", "Directory", ResultColumnWidth.Flexible,     0,   6,  40, ResultColumnTruncation.Tail, true,
                p => DirCell(p, projectRoot)),
            new ResultColumn("path", "Path",     ResultColumnWidth.Fixed,       60,  10,  60, ResultColumnTruncation.Tail, false,
                p => Cell<FileHit>(p, h => h.FilePath)),
        };

        internal static IReadOnlyList<ResultColumn> Recent() => new[]
        {
            // The Files shape (file+dir visible, path hidden); the dir cell is the FULL
            // directory always — a cross-solution MRU has no single root to trim (the overlay
            // passes no projectRoot), so the getter is Recent-specific, not DirCell (typed to
            // FileHit — the type-disjointness guard).
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<RecentFileHit>(p, h => BaseName(h))),
            new ResultColumn("dir", "Directory", ResultColumnWidth.Flexible,     0,   6,  40, ResultColumnTruncation.Tail, true,
                p => Cell<RecentFileHit>(p, h => Path.GetDirectoryName(h.FilePath) ?? string.Empty)),
            new ResultColumn("path", "Path",     ResultColumnWidth.Fixed,       60,  10,  60, ResultColumnTruncation.Tail, false,
                p => Cell<RecentFileHit>(p, h => h.FilePath)),
        };

        internal static IReadOnlyList<ResultColumn> Issues() => new[]
        {
            new ResultColumn("kind", "Kind",     ResultColumnWidth.Fixed,        6,   3,   8, ResultColumnTruncation.End, true,
                p => Cell<CodeIssue>(p, i => KindAbbreviations.Issue(i.Kind))),
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<CodeIssue>(p, i => BaseName(i))),
            new ResultColumn("message", "Message", ResultColumnWidth.Flexible,   0,  10,  int.MaxValue, ResultColumnTruncation.End, true,
                p => Cell<CodeIssue>(p, i => i.Text)),
            new ResultColumn("line", "Line",     ResultColumnWidth.Fixed,        6,   2,   5, ResultColumnTruncation.End, false,
                p => Cell<CodeIssue>(p, i => Line(i))),
        };

        internal static IReadOnlyList<ResultColumn> References() => new[]
        {
            new ResultColumn("access", "Access", ResultColumnWidth.Fixed,        4,   2,   4, ResultColumnTruncation.End, true,
                p => Cell<ReferenceHit>(p, r => KindAbbreviations.Access(r.IsWrite))),
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<ReferenceHit>(p, r => BaseName(r))),
            new ResultColumn("symbol", "Symbol", ResultColumnWidth.Fixed,       24,   6,  24, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => r.Symbol)),
            new ResultColumn("column", "Column", ResultColumnWidth.Fixed,        8,   2,   8, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => r.Column.ToString(CultureInfo.InvariantCulture))),
            new ResultColumn("line", "Line",     ResultColumnWidth.Fixed,        6,   2,   5, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => Line(r))),
            new ResultColumn("text", "Line text", ResultColumnWidth.Flexible,    0,  10,  int.MaxValue, ResultColumnTruncation.End, false,
                p => Cell<ReferenceHit>(p, r => r.LineText)),
        };

        internal static IReadOnlyList<ResultColumn> Grep() => new[]
        {
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<GrepHit>(p, h => BaseName(h))),
            new ResultColumn("line", "Line",     ResultColumnWidth.Fixed,        6,   2,   5, ResultColumnTruncation.End, true,
                p => Cell<GrepHit>(p, h => Line(h))),
            new ResultColumn("text", "Line text", ResultColumnWidth.Flexible,    0,  10,  int.MaxValue, ResultColumnTruncation.End, true,
                p => Cell<GrepHit>(p, h => h.LineText)),
        };

        internal static IReadOnlyList<ResultColumn> Fzf() => new[]
        {
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<FzfHit>(p, h => BaseName(h))),
            new ResultColumn("line", "Line",     ResultColumnWidth.Fixed,        6,   2,   5, ResultColumnTruncation.End, true,
                p => Cell<FzfHit>(p, h => Line(h))),
            new ResultColumn("text", "Line text", ResultColumnWidth.Flexible,    0,  10,  int.MaxValue, ResultColumnTruncation.End, true,
                p => Cell<FzfHit>(p, h => h.LineText)),
        };

        internal static IReadOnlyList<ResultColumn> Implementation() => new[]
        {
            new ResultColumn("kind", "Kind",     ResultColumnWidth.Fixed,        6,   3,   8, ResultColumnTruncation.End, true,
                p => Cell<ImplementationHit>(p, h => KindAbbreviations.Implementation(h.Kind))),
            new ResultColumn("file", "File",     ResultColumnWidth.Fixed,       28,   6,  30, ResultColumnTruncation.Tail, true,
                p => Cell<ImplementationHit>(p, h => BaseName(h))),
            new ResultColumn("symbol", "Symbol", ResultColumnWidth.Flexible,     0,   6,  int.MaxValue, ResultColumnTruncation.End, false,
                p => Cell<ImplementationHit>(p, h => h.SymbolName)),
            new ResultColumn("line", "Line",     ResultColumnWidth.Fixed,        6,   2,   5, ResultColumnTruncation.End, false,
                p => Cell<ImplementationHit>(p, h => Line(h))),
        };

        /// <summary>
        /// The column catalog for a finder, keyed by its <c>IFinder.Name</c> (ordinal,
        /// case-sensitive). <paramref name="projectRoot"/> only affects the Files
        /// <c>dir</c> column (root-tail trimming). An unknown name yields an empty catalog.
        /// </summary>
        internal static IReadOnlyList<ResultColumn> ForFinder(string finderName, string? projectRoot = null)
        {
            switch (finderName)
            {
                case "Files": return Files(projectRoot);
                case "Recent": return Recent();
                case "Issues": return Issues();
                case "References": return References();
                case "Grep": return Grep();
                case "Fzf": return Fzf();
                case "Implementation": return Implementation();
                default: return Array.Empty<ResultColumn>();
            }
        }
    }
}
