using System;
using System.Collections.Generic;
using Telescope.Finders;

namespace Telescope.Overlay
{
    /// <summary>
    /// The abbreviated cell values for the narrow results columns (plan D2a — the user's
    /// 2026-10-04 "shorten what you can" instruction: write=W, read=R, error=err,
    /// interface=inf, ...). Pure mapping — no WPF/VS dependencies; total (never throws,
    /// never returns null). The row's Display string is UNTOUCHED (plan D3): fzf still
    /// filters the long forms, and the <c>opened reference: ... access=read|write</c>
    /// diagnostic keeps the long forms too.
    /// </summary>
    internal static class KindAbbreviations
    {
        // ---- Access (References) ---------------------------------------------------

        /// <summary>The References <c>access</c> cell: write → <c>W</c>, read → <c>R</c> (user-specified).</summary>
        internal static string Access(bool isWrite) => isWrite ? "W" : "R";

        // ---- Issues kind (CodeIssueKind) ---------------------------------------------

        /// <summary>
        /// The Issues <c>kind</c> cell: Todo→<c>todo</c>, Error→<c>err</c> (user-specified),
        /// Warning→<c>warn</c>, Info→<c>info</c>. Exhaustive over <see cref="CodeIssueKind"/>
        /// (CodeIssue.cs:4-17); a value added to the enum later renders unabbreviated.
        /// NOT the legacy display markers — <c>CodeIssuesFinder.ToEntry</c> keeps
        /// <c>[ERR]/[WARN]/[TODO]/[INFO]</c> in the Display string (untouched, plan D3).
        /// </summary>
        internal static string Issue(CodeIssueKind kind)
        {
            switch (kind)
            {
                case CodeIssueKind.Todo: return "todo";
                case CodeIssueKind.Error: return "err";
                case CodeIssueKind.Warning: return "warn";
                case CodeIssueKind.Info: return "info";
                default: return kind.ToString();
            }
        }

        // ---- Implementation kind (free-form string from Roslyn) -----------------------

        // ImplementationHit.Kind is a STRING (ImplementationHit.cs:19-20) produced at exactly
        // one production site (RoslynGatherers.cs:141-144): impl is INamedTypeSymbol
        // ? TypeKind.ToString() : SymbolKind.ToString(). The map covers the 7 realistically
        // reachable kinds (via SymbolFinder.FindImplementationsAsync) + the 2 user-literal
        // entries; any other value falls back to the lowercase ≤4-char rule (m42/BP-5 — the
        // ~20 defensive Roslyn TypeKind/SymbolKind entries are unreachable in production and
        // are dropped; the Fallback renders them).
        private static readonly Dictionary<string, string> ImplMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Realistically reachable via SymbolFinder.FindImplementationsAsync:
            ["Class"] = "cls",
            ["Interface"] = "inf",          // user-specified
            ["Struct"] = "str",
            ["Enum"] = "enm",
            ["Method"] = "func",            // user-specified ("function=func") — Method is the function-like kind
            ["Property"] = "prop",
            ["Event"] = "evt",
            // User-literal defensive entries (the user's words carried verbatim; no
            // production path produces these values today):
            ["Implementation"] = "imp",     // user-specified
            ["Function"] = "func",          // user-specified (same output as Method)
        };

        /// <summary>
        /// The Implementation <c>kind</c> cell: the abbreviated form of the hit's kind string
        /// (see <see cref="ImplMap"/>). A null/empty kind yields the empty cell; an unmapped
        /// value falls back to lowercase truncated to 4 chars — total, never throws.
        /// </summary>
        internal static string Implementation(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return string.Empty;
            }

            return ImplMap.TryGetValue(kind, out var abbr) ? abbr : Fallback(kind);
        }

        /// <summary>Fallback for a kind value not in the map (e.g. a future Roslyn enum
        /// value): lowercase, truncated to at most 4 characters.</summary>
        private static string Fallback(string kind)
            => kind.Length <= 4 ? kind.ToLowerInvariant() : kind.Substring(0, 4).ToLowerInvariant();
    }
}
