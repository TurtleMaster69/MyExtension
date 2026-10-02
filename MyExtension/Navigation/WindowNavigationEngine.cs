using System;
using System.Collections.Generic;

namespace MyExtension.Navigation
{
    sealed class WindowNavigationEngine
    {
        public static int? SelectTarget(WindowRect active, IReadOnlyList<WindowRect> candidates, Direction direction, NavigationSettings settings)
        {
            int divide = (direction == Direction.Up || direction == Direction.Down) ? settings.YDivide : settings.XDivide;

            List<Candidate> passing = new List<Candidate>();
            int minGap = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                WindowRect c = candidates[i];
                if (c.IsEmpty || !IsInDirection(c, active, direction) || !IsAligned(c, active, direction))
                {
                    continue;
                }
                int gap = c.GapTo(active, direction);
                passing.Add(new Candidate(i, gap, c.Adjacency(active, direction.PerpendicularAxis())));
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
                    continue;
                }
                if (bestIndex == null || candidate.Adjacency >= bestAdjacency)
                {
                    bestIndex = candidate.Index;
                    bestAdjacency = candidate.Adjacency;
                }
            }

            return bestIndex;
        }

        private static bool IsInDirection(WindowRect c, WindowRect a, Direction d)
        {
            switch (d)
            {
                case Direction.Up: return c.Y < a.Y;
                case Direction.Down: return c.Y > a.Y;
                case Direction.Left: return c.X < a.X;
                case Direction.Right: return c.X > a.X;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }

        private static bool IsAligned(WindowRect c, WindowRect a, Direction d)
        {
            switch (d)
            {
                case Direction.Up:
                case Direction.Down: return a.X <= c.Right && c.X <= a.Right;
                case Direction.Left:
                case Direction.Right: return a.Y <= c.Bottom && c.Y <= a.Bottom;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
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
