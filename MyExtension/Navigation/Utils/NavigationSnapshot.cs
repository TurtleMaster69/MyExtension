using System.Collections.Generic;

namespace MyExtension.Navigation
{
    /// <summary>
    /// A single-pass snapshot of the navigation candidates: the active rect is derived from the
    /// snapshot's candidate list, never a separate fetch (no N+1 GetWindowScreenRect COM calls).
    /// </summary>
    readonly struct NavigationSnapshot
    {
        public IReadOnlyList<WindowRect> Candidates { get; }

        public int ActiveIndex { get; }

        public WindowRect Active => Candidates[ActiveIndex];

        private NavigationSnapshot(IReadOnlyList<WindowRect> candidates, int activeIndex)
        {
            Candidates = candidates;
            ActiveIndex = activeIndex;
        }

        public static NavigationSnapshot? Capture(IReadOnlyList<WindowRect> candidates, int activeIndex)
        {
            if (activeIndex < 0 || activeIndex >= candidates.Count)
            {
                return null;
            }
            return new NavigationSnapshot(candidates, activeIndex);
        }
    }
}
