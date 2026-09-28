using Microsoft.VisualStudio.Utilities;

namespace CardinalNavigation
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

        public static NavigationSettings FromSystemDpi() => FromDpi((int)DpiAwareness.SystemDpiX, (int)DpiAwareness.SystemDpiY);

        public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY)
        {
            int xDivide = (int)(CardinalNavigationConstants.DefaultLogicalXWindowDivide * (systemDpiX / (double)DpiAwareness.DefaultLogicalDpi) * CardinalNavigationConstants.DefaultLogicalSelectorScale);
            int yDivide = (int)(CardinalNavigationConstants.DefaultLogicalTabPaneDivide * (systemDpiY / (double)DpiAwareness.DefaultLogicalDpi) * CardinalNavigationConstants.DefaultLogicalSelectorScale);
            return new NavigationSettings(xDivide, yDivide);
        }
    }
}
