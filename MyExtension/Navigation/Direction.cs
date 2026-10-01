namespace MyExtension.Navigation
{
    using System;

    enum Axis { X, Y }
    enum Direction { Up, Down, Left, Right }

    static class DirectionExtensions
    {
        public static Axis PerpendicularAxis(this Direction d) => (d == Direction.Up || d == Direction.Down) ? Axis.X : Axis.Y;

        public static char ToChar(this Direction d)
        {
            switch (d)
            {
                case Direction.Left: return 'L';
                case Direction.Right: return 'R';
                case Direction.Up: return 'U';
                case Direction.Down: return 'D';
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }
    }
}
