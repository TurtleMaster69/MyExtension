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
    /// List pane is never focused in insert mode). Pure step math so it is unit-testable.
    /// </summary>
    internal static class PaneSelectionSync
    {
        /// <summary>The number of Down (+) / Up (−) gestures to replay from <paramref name="from"/>
        /// to <paramref name="to"/>. 0 when equal. The SIGN is the direction — pinned by
        /// Run_PaneSelectionSync_Steps.</summary>
        public static int Steps(int from, int to) => to - from;
    }
}
