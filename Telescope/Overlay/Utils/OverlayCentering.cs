using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// Pure centering math for the overlay (m31/BP-28): converts the physical-pixel rect to DIPs
    /// BEFORE subtracting the DIP window size (the old <c>r.Width - Width</c> mixed pixels and
    /// DIPs) and centers the DIP window over the DIP rect — Left = the horizontal center, Top =
    /// one-third down (the pinned placement). <paramref name="scale"/> is the TARGET monitor's DPI
    /// scale (the monitor containing the rect), resolved by the caller — NOT the system DPI.
    /// </summary>
    internal static class OverlayCentering
    {
        public static (double Left, double Top) Center(System.Drawing.Rectangle r, double width, double height, double scale)
        {
            double left = r.Left / scale + (r.Width / scale - width) / 2.0;
            double top = r.Top / scale + (r.Height / scale - height) / 3.0;
            return (left, top);
        }
    }
}
