using System;
using System.Collections.Generic;

namespace Telescope.Overlay
{
    /// <summary>
    /// The pure geometric rect contract shared by BOTH selection surfaces (M4/BP-3): the overlay's
    /// pane focus (<see cref="PaneRect"/>) and the window navigation (<c>MyExtension.Navigation.WindowRect</c>).
    /// Dependency-free (no WPF/VS) so the shared engine is unit-testable hermetically.
    /// </summary>
    internal interface IGeometricRect
    {
        int X { get; }
        int Y { get; }
        int Right { get; }
        int Bottom { get; }
        bool IsEmpty { get; }
    }

    /// <summary>
    /// The shared geometric selection engine (M4/BP-3): the single pure pipeline both
    /// <see cref="FocusTargetModel"/> (pane focus) and <c>MyExtension.Navigation.WindowNavigationEngine</c>
    /// (window navigation) delegate to. The <c>GapTo</c>/<c>Adjacency</c> formulas are identical in
    /// both surfaces (verified), so one engine replaces the mirrored pipelines. Parameterized by
    /// <b>(allowNegativeGap, divide, strictEdge)</b>:
    /// <list type="bullet">
    /// <item><b>allowNegativeGap</b> — the pane needs <c>false</c> (pane rects OVERLAP — the
    ///   full-width Input underlies both top panes; a negative gap means "overlapping/behind",
    ///   not "in direction"); the window needs <c>true</c> (window rects are disjoint by OS
    ///   construction).</item>
    /// <item><b>divide</b> — the closest-gap band width (<c>gap &lt;= minGap + divide</c>); the
    ///   pane drops it (<c>0</c> — all panes share one window, no cross-monitor tolerance), the
    ///   window passes its DPI-scaled divide.</item>
    /// <item><b>strictEdge</b> — affects ONLY Down/Up: the pane needs <c>false</c> (Down
    ///   <c>c.Y &gt; a.Y</c>, pinned by <c>Run_FocusTarget_CtrlJFromListMovesToInput</c>), the
    ///   window needs <c>true</c> (post-M7 Down <c>c.Y &gt; a.Bottom</c>).</item>
    /// </list>
    /// The tie-break is <c>&gt;=</c> — the LAST entry in iteration order wins (the pinned Cardinal
    /// rule). Returns the winning candidate's index, or null when no candidate qualifies.
    /// </summary>
    internal static class GeometricSelectionEngine
    {
        /// <summary>Direction tokens: 0=Up, 1=Down, 2=Left, 3=Right (matches
        /// <c>MyExtension.Navigation.Direction</c> and the pane's <c>PaneFocusKey</c> mapping).</summary>
        public const int Up = 0;
        public const int Down = 1;
        public const int Left = 2;
        public const int Right = 3;

        public static int? SelectTarget<T>(T active, IReadOnlyList<T> candidates, int direction, bool allowNegativeGap, int divide, bool strictEdge)
            where T : IGeometricRect
        {
            if (active.IsEmpty)
            {
                return null;   // no layout yet — a safe no-op
            }

            List<Candidate> passing = new List<Candidate>();
            int minGap = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                T c = candidates[i];
                if (c.IsEmpty || !IsInDirection(c, active, direction, strictEdge) || !IsAligned(c, active, direction))
                {
                    continue;
                }
                int gap = GapTo(c, active, direction);
                if (!allowNegativeGap && gap < 0)
                {
                    continue;   // overlapping/behind — not "in direction"
                }
                passing.Add(new Candidate(i, gap, Adjacency(c, active, direction)));
                if (gap < minGap)
                {
                    minGap = gap;
                }
            }

            if (passing.Count == 0)
            {
                return null;
            }

            int upperBound = minGap + divide;
            int? bestIndex = null;
            int bestAdjacency = int.MinValue;
            foreach (Candidate candidate in passing)
            {
                if (candidate.Gap > upperBound)
                {
                    continue;   // outside the closest-gap band
                }
                if (bestIndex == null || candidate.Adjacency >= bestAdjacency)
                {
                    bestIndex = candidate.Index;   // ">=" — the LAST tie in iteration order wins (pinned)
                    bestAdjacency = candidate.Adjacency;
                }
            }

            return bestIndex;
        }

        private static bool IsInDirection<T>(T c, T a, int direction, bool strictEdge)
            where T : IGeometricRect
        {
            switch (direction)
            {
                case Up: return strictEdge ? c.Bottom < a.Y : c.Y < a.Y;
                case Down: return strictEdge ? c.Y > a.Bottom : c.Y > a.Y;
                case Left: return c.X < a.X;
                case Right: return c.X > a.X;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        private static bool IsAligned<T>(T c, T a, int direction)
            where T : IGeometricRect
        {
            switch (direction)
            {
                case Up:
                case Down: return a.X <= c.Right && c.X <= a.Right;
                case Left:
                case Right: return a.Y <= c.Bottom && c.Y <= a.Bottom;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        private static int GapTo<T>(T c, T a, int direction)
            where T : IGeometricRect
        {
            switch (direction)
            {
                case Up: return a.Y - c.Bottom;
                case Down: return c.Y - a.Bottom;
                case Left: return a.X - c.Right;
                case Right: return c.X - a.Right;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        private static int Adjacency<T>(T c, T a, int direction)
            where T : IGeometricRect
        {
            if (direction == Up || direction == Down)
            {
                int start = Math.Max(c.X, a.X);
                int end = Math.Min(c.Right, a.Right);
                return Math.Max(0, end - start);
            }
            int yStart = Math.Max(c.Y, a.Y);
            int yEnd = Math.Min(c.Bottom, a.Bottom);
            return Math.Max(0, yEnd - yStart);
        }

        private readonly struct Candidate
        {
            public readonly int Index;
            public readonly int Gap;
            public readonly int Adjacency;

            public Candidate(int index, int gap, int adjacency)
            {
                Index = index;
                Gap = gap;
                Adjacency = adjacency;
            }
        }
    }
}
