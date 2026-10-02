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
        private bool _emitted;
        private bool _retryAllowed = true;

        /// <summary>Returns true once (the first call), then false forever.</summary>
        public bool ShouldEmit()
        {
            if (_emitted)
            {
                return false;
            }
            _emitted = true;
            return true;
        }

        /// <summary>Builds the one-time fallback line for the given failure reason.</summary>
        public string FallbackMessage(string reason) => DiagnosticLog.NeoVisual + "output pane unavailable: " + reason;

        /// <summary>
        /// True when a pane-init attempt may be (re)tried. A transient failure must not permanently
        /// disable the pane, so a recorded failure keeps the retry latch open for a later call.
        /// </summary>
        public bool ShouldRetry() => _retryAllowed;

        /// <summary>Records a pane-init attempt. A failed attempt keeps the retry latch open.</summary>
        public void RecordAttempt()
        {
            _retryAllowed = true;
        }
    }
}
