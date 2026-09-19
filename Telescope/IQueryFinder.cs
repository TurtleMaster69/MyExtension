using System.Collections.Generic;

namespace Telescope
{
    /// <summary>
    /// A Telescope finder that is <b>query-driven</b>: every query change re-gathers candidates
    /// from <see cref="GetCandidates(string)"/> and renders them directly (skipping fzf — grep
    /// semantics are literal, not fuzzy). The overlay branches on this capability and applies a
    /// debounce so a full-solution scan runs after typing settles, not per keystroke.
    /// </summary>
    public interface IQueryFinder
    {
        IReadOnlyList<FinderEntry> GetCandidates(string query);
    }
}