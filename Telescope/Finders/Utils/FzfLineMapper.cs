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
        public static IReadOnlyList<int> Map(string[] fileLines, IReadOnlyList<string> matchedLines)
        {
            var result = new List<int>();
            if (fileLines == null || matchedLines == null)
            {
                return result;
            }

            var consumed = new bool[fileLines.Length];
            foreach (string matched in matchedLines)
            {
                for (int i = 0; i < fileLines.Length; i++)
                {
                    if (!consumed[i] && string.Equals(fileLines[i], matched, StringComparison.Ordinal))
                    {
                        consumed[i] = true;
                        result.Add(i + 1);
                        break;
                    }
                }
            }
            return result;
        }
    }
}
