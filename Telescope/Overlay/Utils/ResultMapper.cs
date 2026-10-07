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
    public sealed class ResultMapper
    {
        // m33: the byDisplay map is derived from the snapshot and rebuilt per keystroke; cache it
        // per snapshot (reference-keyed single entry — the snapshot is the overlay's candidate list,
        // which does not change while filtering). Rebuilt only when a different snapshot arrives.
        // R40: the cache is INSTANCE-scoped (not static) so a closed overlay's candidate list is
        // not pinned in memory. The cache is NOT synchronized — the mapper is only safe on the
        // UI thread (the overlay's whole lifecycle is UI-thread-only; n20/BP-12).
        private IReadOnlyList<FinderEntry>? _cachedSnapshot;
        private Dictionary<string, List<(FinderEntry entry, int index)>>? _cachedByDisplay;

        public IReadOnlyList<FinderEntry> MapBack(IReadOnlyList<string> matched, IReadOnlyList<FinderEntry> snapshot)
        {
            if (!ReferenceEquals(_cachedSnapshot, snapshot))
            {
                _cachedSnapshot = snapshot;
                // BP-13 (D12): the byDisplay map groups with ORDINAL (not OrdinalIgnoreCase) — a
                // case-colliding duplicate ("Foo.cs"/"foo.cs") must map to its OWN payload, not
                // collapse to the first case-insensitive match.
                _cachedByDisplay = snapshot
                    .Select((entry, index) => (entry, index))
                    .GroupBy(x => x.entry.Display, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            }
            var byDisplay = _cachedByDisplay!;
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
                    TelescopeLog.Log($"result-mapper unknown display: {DiagnosticLog.SanitizeText(m)}");
                    continue;
                }
                items.Add(entry);
            }

            return items;
        }
    }
}
