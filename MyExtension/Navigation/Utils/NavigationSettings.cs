using Microsoft.VisualStudio.Utilities;

namespace MyExtension.Navigation
{
    sealed class NavigationSettings
    {
        public int XDivide { get; }
        public int YDivide { get; }

        private NavigationSettings(int xDivide, int yDivide)
        {
            XDivide = xDivide;
            YDivide = yDivide;
        }

        // N6: no cache — FromSystemDpi re-reads the system DPI per navigation (cheap), so a
        // mid-session scaling change can never leave stale divide tolerances. The old `_cached`
        // static + Invalidate() seam had zero callers (verified via Trailmark).
        public static NavigationSettings FromSystemDpi()
        {
            return FromDpi((int)DpiAwareness.SystemDpiX, (int)DpiAwareness.SystemDpiY);
        }

        public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY)
        {
            int xDivide = (int)(NavigationConstants.DefaultLogicalXWindowDivide * (systemDpiX / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            int yDivide = (int)(NavigationConstants.DefaultLogicalTabPaneDivide * (systemDpiY / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            return new NavigationSettings(xDivide, yDivide);
        }
    }
}
