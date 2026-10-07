using System;

namespace MyExtension.Navigation
{
    enum Direction { Up, Down, Left, Right }

    static class DirectionExtensions
    {
        // m10 (BP-13): PerpendicularAxis + the Axis enum are DELETED — they only fed the dead
        // WindowRect.Adjacency mirror; the shared GeometricSelectionEngine owns the formulas now.
        // ToChar is LIVE (the navigate direction= diagnostic).

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
