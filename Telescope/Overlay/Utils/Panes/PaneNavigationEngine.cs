// Telescope/Overlay/Utils/Panes/PaneNavigationEngine.cs
using System;
using System.Collections.Generic;

namespace Telescope.Overlay
{
    internal enum PaneAxis { X, Y }

    internal enum PaneDirection { Left, Right, Up, Down }

    internal static class PaneDirectionExtensions
    {
        /// <summary>The axis shared with the movement direction (the alignment/adjacency axis) —
        /// mirrors MyExtension.Navigation's DirectionExtensions.PerpendicularAxis.</summary>
        public static PaneAxis PerpendicularAxis(this PaneDirection d)
            => (d == PaneDirection.Up || d == PaneDirection.Down) ? PaneAxis.X : PaneAxis.Y;
    }

    /// <summary>
    /// A pane's layout rect in overlay DIP coordinates (origin top-left) — the pane analogue of
    /// MyExtension.Navigation.WindowRect (NOT shared: Telescope does not reference MyExtension).
    /// </summary>
    internal readonly struct PaneRect
    {
        public static readonly PaneRect Empty = new PaneRect(0, 0, 0, 0);

        public PaneRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => Width == 0 && Height == 0;

        /// <summary>The overlap length with <paramref name="other"/> on <paramref name="axis"/>
        /// (0-floor) — mirrors WindowRect.Adjacency.</summary>
        public int Adjacency(PaneRect other, PaneAxis axis)
        {
            if (axis == PaneAxis.X)
            {
                int start = Math.Max(X, other.X);
                int end = Math.Min(Right, other.Right);
                return Math.Max(0, end - start);
            }
            int yStart = Math.Max(Y, other.Y);
            int yEnd = Math.Min(Bottom, other.Bottom);
            return Math.Max(0, yEnd - yStart);
        }

        /// <summary>The distance from this rect to <paramref name="other"/> along
        /// <paramref name="direction"/> (NEGATIVE = overlapping/behind) — mirrors
        /// WindowRect.GapTo. Called ON THE CANDIDATE with the focused rect as other.</summary>
        public int GapTo(PaneRect other, PaneDirection direction)
        {
            switch (direction)
            {
                case PaneDirection.Up: return other.Y - Bottom;
                case PaneDirection.Down: return Y - other.Bottom;
                case PaneDirection.Left: return other.X - Right;
                case PaneDirection.Right: return X - other.Right;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
    }

    /// <summary>
    /// The pane analogue of <c>MyExtension.Navigation.WindowNavigationEngine</c> (the SAME
    /// pipeline over pane rects — Feature 7 rev 1's geometric directional focus):
    /// in-direction → aligned → closest gap → largest adjacency → ties to the LAST entry in the
    /// iteration (registry) order. MIRRORED, not referenced: Telescope.csproj does not reference
    /// MyExtension (the dependency runs the other way). TWO pinned deviations from the window
    /// engine: (1) the candidate filter adds <c>gap &gt;= 0</c> — window rects are disjoint by
    /// OS construction (a negative gap cannot occur), but pane rects OVERLAP (the full-width
    /// Input underlies both top panes); a negative gap means "overlapping/behind", not "in
    /// direction". (2) the DPI divide is DROPPED — all panes share one window, so the
    /// closest-gap band is <c>gap == minGap</c> (no cross-monitor tolerance to bridge).
    /// Pure and dependency-free — unit-tested with synthetic rects.
    /// </summary>
    internal static class PaneNavigationEngine
    {
        private readonly struct Candidate
        {
            public readonly FocusTarget Id;
            public readonly int Gap;
            public readonly int Adjacency;

            public Candidate(FocusTarget id, int gap, int adjacency)
            {
                Id = id;
                Gap = gap;
                Adjacency = adjacency;
            }
        }

        /// <summary>(the pane rects, the focused pane, the direction) → the target pane, or
        /// null when NO pane lies in that direction (the caller no-ops — no wrap).
        /// <paramref name="panes"/> MUST be in registry order [Input, List, Preview] — the
        /// tie-break iterates it and the LAST tie wins (the <c>&gt;=</c> comparison below).</summary>
        public static FocusTarget? SelectTarget(
            IReadOnlyList<KeyValuePair<FocusTarget, PaneRect>> panes,
            FocusTarget current,
            PaneDirection direction)
        {
            PaneRect active = GetRect(panes, current);
            if (active.IsEmpty)
            {
                return null;   // no layout yet (pre-RefreshLayout) — a safe no-op
            }

            List<Candidate> passing = new List<Candidate>();
            int minGap = int.MaxValue;
            for (int i = 0; i < panes.Count; i++)
            {
                if (panes[i].Key == current)
                {
                    continue;
                }
                PaneRect c = panes[i].Value;
                if (c.IsEmpty || !IsInDirection(c, active, direction) || !IsAligned(c, active, direction))
                {
                    continue;
                }
                int gap = c.GapTo(active, direction);
                if (gap < 0)
                {
                    continue;   // THE PINNED DEVIATION: overlapping/behind — not "in direction"
                }
                passing.Add(new Candidate(panes[i].Key, gap, c.Adjacency(active, direction.PerpendicularAxis())));
                if (gap < minGap)
                {
                    minGap = gap;
                }
            }

            if (passing.Count == 0)
            {
                return null;
            }

            FocusTarget? best = null;
            int bestAdjacency = int.MinValue;
            foreach (Candidate candidate in passing)
            {
                if (candidate.Gap > minGap)
                {
                    continue;   // outside the closest-gap band (the divide is dropped — §1.2)
                }
                if (best == null || candidate.Adjacency >= bestAdjacency)
                {
                    best = candidate.Id;   // ">=" — the LAST tie in registry order wins (pinned)
                    bestAdjacency = candidate.Adjacency;
                }
            }

            return best;
        }

        private static PaneRect GetRect(IReadOnlyList<KeyValuePair<FocusTarget, PaneRect>> panes, FocusTarget id)
        {
            for (int i = 0; i < panes.Count; i++)
            {
                if (panes[i].Key == id)
                {
                    return panes[i].Value;
                }
            }
            return PaneRect.Empty;
        }

        private static bool IsInDirection(PaneRect c, PaneRect a, PaneDirection d)
        {
            switch (d)
            {
                case PaneDirection.Up: return c.Y < a.Y;
                case PaneDirection.Down: return c.Y > a.Y;
                case PaneDirection.Left: return c.X < a.X;
                case PaneDirection.Right: return c.X > a.X;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }

        private static bool IsAligned(PaneRect c, PaneRect a, PaneDirection d)
        {
            switch (d)
            {
                case PaneDirection.Up:
                case PaneDirection.Down: return a.X <= c.Right && c.X <= a.Right;
                case PaneDirection.Left:
                case PaneDirection.Right: return a.Y <= c.Bottom && c.Y <= a.Bottom;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }
    }
}
