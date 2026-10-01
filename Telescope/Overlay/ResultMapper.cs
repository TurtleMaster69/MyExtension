using System;
using System.Collections.Generic;
using System.Linq;
using Telescope.Finders;
using Telescope.Logging;

namespace Telescope.Overlay
{
    /// <summary>
    /// Maps the fzf-matched display lines back to the original <see cref="FinderEntry"/>
    /// payloads by stable ordinal: each matched display string consumes the next unconsumed
    /// entry with that display, so same-named duplicates (e.g. two <c>Program.cs</c> files in
    /// different folders) each map to their own distinct payload instead of collapsing to the
    /// first. Unknown strings are skipped (M11 — no null-payload <see cref="FinderEntry"/> whose
    /// <c>OnSelected</c> silently no-ops) and logged as a warning.
    /// </summary>
    public static class ResultMapper
    {
        public static IReadOnlyList<FinderEntry> MapBack(IReadOnlyList<string> matched, IReadOnlyList<FinderEntry> snapshot)
        {
            var byDisplay = snapshot
                .Select((entry, index) => (entry, index))
                .GroupBy(x => x.entry.Display, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var consumed = new HashSet<int>();
            var items = new List<FinderEntry>();

            foreach (var m in matched)
            {
                FinderEntry? entry = null;
                if (byDisplay.TryGetValue(m, out var candidates))
                {
                    foreach (var candidate in candidates)
                    {
                        if (!consumed.Contains(candidate.index))
                        {
                            consumed.Add(candidate.index);
                            entry = candidate.entry;
                            break;
                        }
                    }
                }
                if (entry == null)
                {
                    // M11: an unmatched display string must not produce a null-payload entry that
                    // silently no-ops — skip it and log a warning.
                    TelescopeLog.Log($"result-mapper unknown display: {m}");
                    continue;
                }
                items.Add(entry);
            }

            return items;
        }
    }
}
