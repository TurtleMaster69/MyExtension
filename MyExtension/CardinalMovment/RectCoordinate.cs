
using System;

namespace CardinalNavigation
{
    readonly struct RectCoordinate
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public RectCoordinate(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => X == 0 && Y == 0 && Width == 0 && Height == 0;

        public int Adjacency(RectCoordinate other, Axis axis)
        {
            if (axis == Axis.X)
            {
                int start = Math.Max(X, other.X);
                int end = Math.Min(X + Width, other.X + other.Width);
                return Math.Max(0, end - start);
            }
            else
            {
                int start = Math.Max(Y, other.Y);
                int end = Math.Min(Y + Height, other.Y + other.Height);
                return Math.Max(0, end - start);
            }
        }

        public int GapTo(RectCoordinate other, Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return other.Y - Bottom;
                case Direction.Down: return Y - other.Bottom;
                case Direction.Left: return other.X - Right;
                case Direction.Right: return X - other.Right;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
    }
}
