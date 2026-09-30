using System.Collections.Generic;

namespace CardinalNavigation
{
    /// <summary>
    /// A single-pass snapshot of the navigation candidates: the active rect is derived from the
    /// snapshot's candidate list, never a separate fetch (no N+1 GetWindowScreenRect COM calls).
    /// </summary>
    sealed class NavigationSnapshot
    {
        public IReadOnlyList<RectCoordinate> Candidates { get; }

        public int ActiveIndex { get; }

        public RectCoordinate Active => Candidates[ActiveIndex];

        private NavigationSnapshot(IReadOnlyList<RectCoordinate> candidates, int activeIndex)
        {
            Candidates = candidates;
            ActiveIndex = activeIndex;
        }

        public static NavigationSnapshot? Capture(IReadOnlyList<RectCoordinate> candidates, int activeIndex)
        {
            if (activeIndex < 0 || activeIndex >= candidates.Count)
            {
                return null;
            }
            return new NavigationSnapshot(candidates, activeIndex);
        }
    }
}
