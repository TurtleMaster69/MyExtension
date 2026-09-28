using System;
using System.Collections.Generic;

namespace CardinalNavigation
{
    sealed class WindowNavigationEngine
    {
        private const int DownTolerancePixels = 1;

        private static readonly List<Func<RectCoordinate, RectCoordinate, Direction, bool>> Pipeline =
            new List<Func<RectCoordinate, RectCoordinate, Direction, bool>>
            {
                (c, a, d) => !c.IsEmpty,
                (c, a, d) => IsInDirection(c, a, d),
                (c, a, d) => IsAligned(c, a, d),
            };

        public static int? SelectTarget(RectCoordinate active, IReadOnlyList<RectCoordinate> candidates, Direction direction, NavigationSettings settings)
        {
            int divide = (direction == Direction.Up || direction == Direction.Down) ? settings.YDivide : settings.XDivide;

            int minGap = int.MaxValue;
            bool any = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                RectCoordinate c = candidates[i];
                if (PassesPipeline(c, active, direction))
                {
                    int gap = c.GapTo(active, direction);
                    if (gap < minGap)
                    {
                        minGap = gap;
                    }
                    any = true;
                }
            }

            if (!any)
            {
                return null;
            }

            int? bestIndex = null;
            int bestAdjacency = int.MinValue;
            int upperBound = minGap + divide;
            for (int i = 0; i < candidates.Count; i++)
            {
                RectCoordinate c = candidates[i];
                if (!PassesPipeline(c, active, direction))
                {
                    continue;
                }
                int gap = c.GapTo(active, direction);
                if (gap < minGap || gap > upperBound)
                {
                    continue;
                }
                int adjacency = c.Adjacency(active, direction.Axis());
                if (bestIndex == null || adjacency >= bestAdjacency)
                {
                    bestIndex = i;
                    bestAdjacency = adjacency;
                }
            }

            return bestIndex;
        }

        private static bool PassesPipeline(RectCoordinate c, RectCoordinate a, Direction d)
        {
            foreach (Func<RectCoordinate, RectCoordinate, Direction, bool> predicate in Pipeline)
            {
                if (!predicate(c, a, d))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsInDirection(RectCoordinate c, RectCoordinate a, Direction d)
        {
            switch (d)
            {
                case Direction.Up: return c.Y < a.Y;
                case Direction.Down: return c.Y - a.Y > DownTolerancePixels;
                case Direction.Left: return c.X < a.X;
                case Direction.Right: return c.X > a.X;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }

        private static bool IsAligned(RectCoordinate c, RectCoordinate a, Direction d)
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
    }
}
