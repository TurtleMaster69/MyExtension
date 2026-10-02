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

        public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY)
        {
            int xDivide = (int)(NavigationConstants.DefaultLogicalXWindowDivide * (systemDpiX / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            int yDivide = (int)(NavigationConstants.DefaultLogicalTabPaneDivide * (systemDpiY / (double)DpiAwareness.DefaultLogicalDpi) * NavigationConstants.DefaultLogicalSelectorScale);
            return new NavigationSettings(xDivide, yDivide);
        }
    }
}
