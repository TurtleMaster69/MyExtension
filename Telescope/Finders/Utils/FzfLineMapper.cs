using System;
using System.Collections.Generic;

namespace Telescope.Finders
{
    /// <summary>
    /// Maps fzf-matched line strings back to their 1-based line numbers within a single file.
    /// fzf <c>--filter</c> echoes matched input lines verbatim, so identical line text is
    /// ambiguous; each matched string consumes the next unconsumed identical line (ordinal
    /// consumption), mirroring <c>ResultMapper</c>'s duplicate-display strategy. A matched string
    /// with no remaining unconsumed identical line is skipped. Dependency-free (no fzf, no DTE).
    /// </summary>
    internal static class FzfLineMapper
    {
        /// <summary>
        /// The file-boundary marker in the batched candidate list (BP-2/M2): each candidate is
        /// <c>"{fileIndex}\u0001{lineText}"</c>. The SOH control char is untypeable in the query
        /// box, so a query can never collide with the marker.
        /// </summary>
        private const char BoundaryMarker = '\u0001';

        public static IReadOnlyList<int> Map(string[] fileLines, IReadOnlyList<string> matchedLines)
        {
            var result = new List<int>();
            if (fileLines == null || matchedLines == null)
            {
                return result;
            }

            // m6 (BP-7): O(n+m) single pass — build a line-text -> unconsumed 1-based line-number
            // queue, then pop the next unconsumed line per matched string (ordinal consumption).
            var byText = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (int i = 0; i < fileLines.Length; i++)
            {
                if (!byText.TryGetValue(fileLines[i], out Queue<int>? q))
                {
                    q = new Queue<int>();
                    byText[fileLines[i]] = q;
                }
                q.Enqueue(i + 1);
            }

            foreach (string matched in matchedLines)
            {
                if (byText.TryGetValue(matched, out Queue<int>? q) && q.Count > 0)
                {
                    result.Add(q.Dequeue());
                }
            }
            return result;
        }

        /// <summary>
        /// Flattens ALL files' lines into ONE batched candidate list (BP-2/M2): each line becomes
        /// <c>"{fileIndex}\u0001{lineText}"</c> so fzf ranks across every file in a single
        /// <c>--filter</c> call and the output can be mapped back to (file, line).
        /// </summary>
        public static IReadOnlyList<string> BuildCandidates(IReadOnlyList<(string Path, IReadOnlyList<string> Lines)> filesLines)
        {
            var candidates = new List<string>();
            if (filesLines == null)
            {
                return candidates;
            }
            for (int f = 0; f < filesLines.Count; f++)
            {
                IReadOnlyList<string> lines = filesLines[f].Lines;
                if (lines == null)
                {
                    continue;
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    candidates.Add(f.ToString(System.Globalization.CultureInfo.InvariantCulture) + BoundaryMarker + lines[i]);
                }
            }
            return candidates;
        }

        /// <summary>
        /// Maps the batched fzf output back to <c>(fileIndex, lineNumber)</c> pairs in fzf's GLOBAL
        /// rank order (BP-2/M2). Per-file ordinal consumption of duplicate line texts (the same
        /// dictionary technique as <see cref="Map"/>). A matched string with an unknown file index
        /// or no remaining unconsumed identical line is skipped.
        /// </summary>
        public static IReadOnlyList<(int FileIndex, int LineNumber)> MapBatched(
            IReadOnlyList<(string Path, IReadOnlyList<string> Lines)> filesLines,
            IReadOnlyList<string> matchedLines)
        {
            var result = new List<(int FileIndex, int LineNumber)>();
            if (filesLines == null || matchedLines == null)
            {
                return result;
            }

            var perFile = new Dictionary<int, Dictionary<string, Queue<int>>>();
            for (int f = 0; f < filesLines.Count; f++)
            {
                IReadOnlyList<string> lines = filesLines[f].Lines;
                if (lines == null)
                {
                    continue;
                }
                var byText = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!byText.TryGetValue(lines[i], out Queue<int>? q))
                    {
                        q = new Queue<int>();
                        byText[lines[i]] = q;
                    }
                    q.Enqueue(i + 1);
                }
                perFile[f] = byText;
            }

            foreach (string matched in matchedLines)
            {
                int sep = matched.IndexOf(BoundaryMarker);
                if (sep < 0)
                {
                    continue;
                }
                if (!int.TryParse(matched.Substring(0, sep), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int fileIndex))
                {
                    continue;
                }
                string lineText = matched.Substring(sep + 1);
                if (!perFile.TryGetValue(fileIndex, out Dictionary<string, Queue<int>>? byText)
                    || !byText.TryGetValue(lineText, out Queue<int>? q)
                    || q.Count == 0)
                {
                    continue;
                }
                result.Add((fileIndex, q.Dequeue()));
            }
            return result;
        }
    }
}
