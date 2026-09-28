namespace CardinalNavigation
{
    enum Axis { X, Y }
    enum Direction { Up, Down, Left, Right }

    static class DirectionExtensions
    {
        public static Axis Axis(this Direction d) => (d == Direction.Up || d == Direction.Down) ? CardinalNavigation.Axis.X : CardinalNavigation.Axis.Y;

        public static int Sign(this Direction d) => (d == Direction.Up || d == Direction.Left) ? -1 : 1;
    }
}
