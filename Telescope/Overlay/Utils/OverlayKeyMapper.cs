using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// The single shared WPF <see cref="Key"/> → <see cref="OverlayKey"/> table (m24/BP-13): both
    /// the List pane's <see cref="ListKeyMap"/> and the overlay's <c>TelescopeOverlay.MapKey</c>
    /// delegate here so the two surfaces cannot drift. The List pane's Up/Down → <see cref="OverlayKey.Other"/>
    /// pin (the native arrows stay live) is preserved via <paramref name="mapArrows"/> — false (the
    /// default) is the List pane's contract; true is the Input pane's, where Up/Down are selection
    /// gestures. Shift-aware for A/I (m28/BP-25): bare a/i vs shift A/I map to different keys.
    /// </summary>
    internal static class OverlayKeyMapper
    {
        public static OverlayKey Map(Key key, bool shift, bool mapArrows = false)
        {
            switch (key)
            {
                case Key.Escape: return OverlayKey.Escape;
                case Key.Q: return OverlayKey.Q;
                case Key.Enter: return OverlayKey.Enter;
                case Key.Up: return mapArrows ? OverlayKey.Up : OverlayKey.Other;
                case Key.Down: return mapArrows ? OverlayKey.Down : OverlayKey.Other;
                case Key.J: return OverlayKey.J;
                case Key.K: return OverlayKey.K;
                case Key.G: return shift ? OverlayKey.ShiftG : OverlayKey.G;
                case Key.I: return shift ? OverlayKey.ShiftI : OverlayKey.I;
                case Key.A: return shift ? OverlayKey.ShiftA : OverlayKey.A;
                default: return OverlayKey.Other;
            }
        }
    }
}
