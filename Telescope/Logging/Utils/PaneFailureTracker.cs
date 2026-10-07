using System.Threading;
using Telescope.Overlay;

namespace Telescope.Logging
{
    /// <summary>
    /// Pure one-time fallback for the NeoVisual Output pane: on the FIRST pane failure a single
    /// <c>[NeoVisual] output pane unavailable: &lt;reason&gt;</c> line is written to the log FILE
    /// (never via <see cref="NeoVisualLog.Log"/>, which would re-enter the pane path and recurse).
    /// Mirrors the <see cref="OverlayKeyHandler"/>/<see cref="TextMotionNavigator"/> pattern: a
    /// dependency-free state machine the UI facade delegates to, so it stays unit-testable.
    /// </summary>
    internal sealed class PaneFailureTracker
    {
        // m23 (BP-12): an int (0/1) so concurrent UI/background loggers emit exactly once via
        // Interlocked.Exchange — net472 has no Interlocked.Exchange(ref bool, ...) overload.
        private int _emitted;

        /// <summary>Returns true once (the first call), then false forever.</summary>
        public bool ShouldEmit()
        {
            return Interlocked.Exchange(ref _emitted, 1) == 0;
        }

        /// <summary>Builds the one-time fallback line for the given failure reason.</summary>
        public string FallbackMessage(string reason) => DiagnosticLog.NeoVisual + "output pane unavailable: " + reason;
    }
}
