using Microsoft.VisualStudio.Utilities;

namespace MyExtension.Navigation
{
    sealed class NavigationSettings
    {
        private static NavigationSettings? _cached;

        public int XDivide { get; }
        public int YDivide { get; }

        private NavigationSettings(int xDivide, int yDivide)
        {
            XDivide = xDivide;
            YDivide = yDivide;
        }

        public static NavigationSettings FromSystemDpi()
        {
            if (_cached == null)
            {
                _cached = FromDpi((int)DpiAwareness.SystemDpiX, (int)DpiAwareness.SystemDpiY);
            }
            return _cached;
        }

        // R30: the cache was never invalidated — FromSystemDpi read SystemDpiX once and cached it
        // forever, leaving stale divide tolerances after a mid-session scaling change. Invalidate()
        // is the DPI-source seam: a DPI-change signal (e.g. WM_DPICHANGED) calls it so the next
        // FromSystemDpi re-reads the system DPI.
        public static void Invalidate()
        {
            _cached = null;
        }

        public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY)
        {
            int xDivide = (int)(NavigationConstants.DefaultLogicalXWindowDivide * (systemDpiX / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            int yDivide = (int)(NavigationConstants.DefaultLogicalTabPaneDivide * (systemDpiY / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            return new NavigationSettings(xDivide, yDivide);
        }
    }
}
