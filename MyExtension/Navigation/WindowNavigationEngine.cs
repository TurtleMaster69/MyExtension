using System;
using System.Collections.Generic;
using Telescope.Overlay;

namespace MyExtension.Navigation
{
    sealed class WindowNavigationEngine
    {
        public static int? SelectTarget(WindowRect active, IReadOnlyList<WindowRect> candidates, Direction direction, NavigationSettings settings)
        {
            int divide = (direction == Direction.Up || direction == Direction.Down) ? settings.YDivide : settings.XDivide;
            // M4 (BP-5): delegate to the shared GeometricSelectionEngine with the WINDOW parameters
            // (allowNegativeGap:true — window rects are disjoint by OS construction; divide: the
            // DPI-scaled tolerance; strictEdge:true — post-M7 Down c.Y > a.Bottom / Up c.Bottom < a.Y).
            return GeometricSelectionEngine.SelectTarget(
                active, candidates, (int)direction, allowNegativeGap: true, divide: divide, strictEdge: true);
        }
    }
}
