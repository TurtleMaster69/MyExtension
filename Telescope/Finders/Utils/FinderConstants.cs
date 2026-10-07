namespace Telescope.Finders
{
    /// <summary>
    /// Shared finder constants (m29/BP-19): the total-hit cap is ONE constant referenced by every
    /// capped finder (GrepFinder/FzfFinder/RecentFilesFinder), not three per-finder copies.
    /// </summary>
    internal static class FinderConstants
    {
        /// <summary>Total-hit cap: a query that matches everything must not stall the UI.</summary>
        public const int HitCap = 200;
    }
}
