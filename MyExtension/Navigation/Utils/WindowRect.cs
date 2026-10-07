
using System;
using Telescope.Overlay;

namespace MyExtension.Navigation
{
    readonly struct WindowRect : IGeometricRect
    {
        public static readonly WindowRect Empty = new WindowRect(0, 0, 0, 0);

        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public WindowRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => X == 0 && Y == 0 && Width == 0 && Height == 0;

        // M4 (BP-5): the shared IGeometricRect contract — the public readonly FIELDS X/Y need
        // explicit interface properties (Right/Bottom/IsEmpty are already public properties).
        int IGeometricRect.X => X;
        int IGeometricRect.Y => Y;

        public int Adjacency(WindowRect other, Axis axis)
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

        public int GapTo(WindowRect other, Direction direction)
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
