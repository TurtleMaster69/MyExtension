using System.Collections.Generic;

namespace MyExtension.Input
{
    /// <summary>
    /// One navigable diagnostic entry (file path + 1-based line). A plain readonly struct (NOT
    /// a record struct): net472 has no IsExternalInit, record positional properties are
    /// init-only, and this repo has no IsExternalInit polyfill (section-b.md, plan-claim
    /// correction 2).
    /// </summary>
    internal readonly struct DiagnosticEntry
    {
        internal DiagnosticEntry(string filePath, int line)
        {
            FilePath = filePath;
            Line = line;
        }

        /// <summary>Full path of the file the diagnostic points at.</summary>
        internal string FilePath { get; }

        /// <summary>1-based line number (always &gt; 0 — the gatherer drops Line &lt;= 0).</summary>
        internal int Line { get; }
    }

    /// <summary>
    /// Pure severity-filtered diagnostics navigation (LazyVim <c>]e</c>/<c>[e</c>/<c>]w</c>/<c>[,w</c>):
    /// given the diagnostic entries for ONE severity in the CURRENT file, ordered by line, find
    /// the next (or previous) entry relative to the caret line. In-file only (LazyVim
    /// buffer-local semantics); NO wrap — returns null at the end so the caller logs a no-op.
    /// Dependency-free static seam (the CloseWindowCommand/OverlayKeyHandler pattern): the
    /// VS-coupled caller delegates to it so the selection logic stays unit-testable.
    /// </summary>
    internal static class DiagnosticNavigator
    {
        /// <summary>
        /// Returns the first entry strictly AFTER the caret line, or null when none. CONTRACT:
        /// <paramref name="entries"/> are pre-sorted ascending by Line (the caller's gather
        /// contract — this seam does NOT sort; on unsorted input it scans in list order).
        /// </summary>
        internal static DiagnosticEntry? Next(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
            => Select(entries, caretLine, forward: true);

        /// <summary>
        /// Returns the last entry strictly BEFORE the caret line, or null when none. Same
        /// pre-sorted contract as <see cref="Next"/>.
        /// </summary>
        internal static DiagnosticEntry? Prev(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
            => Select(entries, caretLine, forward: false);

        private static DiagnosticEntry? Select(IReadOnlyList<DiagnosticEntry> entries, int caretLine, bool forward)
        {
            if (forward)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Line > caretLine)
                    {
                        return entries[i];
                    }
                }
                return null;
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Line < caretLine)
                {
                    return entries[i];
                }
            }
            return null;
        }
    }
}
