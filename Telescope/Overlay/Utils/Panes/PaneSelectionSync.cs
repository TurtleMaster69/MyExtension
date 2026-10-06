// Telescope/Overlay/Utils/Panes/PaneSelectionSync.cs
namespace Telescope.Overlay
{
    /// <summary>
    /// The List pane's native-selection sync (Feature 7): the ListView's own Up/Down/PageUp/...
    /// handling moves its SelectedIndex (the overlay does NOT claim the arrows — the pinned
    /// consume-vs-fallthrough contract, plan §1.3 R3), and the overlay must adopt that index into
    /// the untouched OverlayKeyHandler. The handler has no index setter, so the adoption REPLAYS
    /// the delta through the machine's own MoveUp/MoveDown gestures
    /// (Handle(OverlayKey.Up/Down) — normal mode only, which the pane invariant guarantees: the
    /// List pane is never focused in insert mode). n6 (BP-18): the one-line step math
    /// (<c>to - from</c>) is inlined at the call site (TelescopeOverlay.OnListNativeSelectionChanged)
    /// — the dedicated <c>Steps</c> wrapper is deleted.
    /// </summary>
    internal static class PaneSelectionSync
    {
    }
}
