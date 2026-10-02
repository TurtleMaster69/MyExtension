namespace Telescope.Overlay
{
    /// <summary>
    /// State-based guard for the overlay's deferred <c>ShowDialog()</c>. The overlay defers
    /// <c>ShowDialog()</c> to <c>DispatcherPriority.ApplicationIdle</c> (fire-and-forget); if
    /// <c>CloseOverlay()</c> runs first, the pending <c>ShowDialog()</c> would fire on an
    /// already-closed window. The decision is STATE-based (<see cref="RequestShow"/> /
    /// <see cref="Close"/> flags), NOT visibility-based — <c>IsVisible</c> is false at
    /// ApplicationIdle time, so a visibility guard would silently never open the overlay.
    /// Dependency-free (no WPF/VS) so it stays unit-testable.
    /// </summary>
    internal sealed class OverlayShowState
    {
        private bool _requested;
        private bool _closed;

        public void RequestShow() => _requested = true;

        public void Close() => _closed = true;

        public bool ShouldShowDialog() => _requested && !_closed;
    }
}
