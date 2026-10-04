namespace Telescope.Controller
{
    /// <summary>The outcome of the goto single/multi-hit decision.</summary>
    internal enum GotoDecision
    {
        /// <summary>Exactly 1 hit: open it directly (no overlay).</summary>
        DirectJump,

        /// <summary>0 hits or multiple hits: open the Telescope overlay with the finder.</summary>
        OpenOverlay,
    }

    /// <summary>
    /// The pure single/multi-hit decision for the goto commands (a dependency-free static
    /// decision the VS-coupled command handler delegates to). Pinned 0-hits rule: 0 hits →
    /// OpenOverlay (LazyVim parity — an empty result list is SHOWN, never a silent no-op; the
    /// overlay's empty state is already a tested no-op surface and the open emits the existing
    /// `open finder=... candidates=0` diagnostic, so the outcome stays traceable).
    /// </summary>
    internal static class GotoDispatcher
    {
        public static GotoDecision Decide(int hitCount)
            => hitCount == 1 ? GotoDecision.DirectJump : GotoDecision.OpenOverlay;
    }
}
