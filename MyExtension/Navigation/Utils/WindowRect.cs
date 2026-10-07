
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
        // m10 (BP-13): the Adjacency/GapTo mirrors of the shared GeometricSelectionEngine formulas
        // are DELETED — the engine at GeometricSelectionEngine.cs is the single source.
        int IGeometricRect.X => X;
        int IGeometricRect.Y => Y;
    }
}
