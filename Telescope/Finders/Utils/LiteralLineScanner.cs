using System;
using System.Collections.Generic;

namespace Telescope.Finders
{
    /// <summary>
    /// Case-insensitive literal substring scan over a file's lines, returning 1-based line numbers
    /// up to a cap. Shared by <see cref="GrepFinder.ScanFile"/> and the <see cref="FzfFinder"/>
    /// literal fallback (when fzf is unavailable), so the scan semantics have a single source of
    /// truth. Dependency-free.
    /// </summary>
    internal static class LiteralLineScanner
    {
        public static IReadOnlyList<int> Scan(IReadOnlyList<string> lines, string query, int cap)
        {
            var result = new List<int>();
            if (lines == null || string.IsNullOrEmpty(query) || cap <= 0)
            {
                return result;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (result.Count >= cap)
                {
                    break;
                }
                if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(i + 1);
                }
            }
            return result;
        }
    }
}
