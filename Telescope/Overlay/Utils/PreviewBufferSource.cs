namespace Telescope.Overlay
{
    /// <summary>
    /// Which buffer source the preview host's current view is built over (the
    /// workspace-attach fix, plan-preview-buffer D1/D2).
    /// </summary>
    internal enum PreviewBufferKind
    {
        /// <summary>The LIVE workspace buffer (Roslyn <c>Document.GetTextBuffer()</c> — the Peek
        /// model): the full Roslyn classifier chain attaches (syntactic + semantic). NOT owned by
        /// the host — the workspace owns it; the read-only view (no Editable role) never mutates
        /// it, so sharing with the main editor is safe.</summary>
        Workspace,

        /// <summary>The standalone content-type document (<c>CreateAndLoadTextDocument</c> —
        /// classifier highlighting only). Owned by the host: created per rebuild, disposed in
        /// CloseView (the pre-fix lifetime).</summary>
        Standalone
    }

    /// <summary>The decision result: which buffer source + who owns the document's lifetime.</summary>
    internal readonly struct PreviewBufferDecision
    {
        internal PreviewBufferKind Kind { get; }

        /// <summary>True when the host owns (and must dispose) the <c>ITextDocument</c> — the
        /// standalone path only. The workspace buffer is NEVER disposed by the host.</summary>
        internal bool OwnsDocument { get; }

        internal PreviewBufferDecision(PreviewBufferKind kind, bool ownsDocument)
        {
            Kind = kind;
            OwnsDocument = ownsDocument;
        }
    }

    /// <summary>
    /// The try-workspace-then-fallback decision (pure, dependency-free — the OverlayKeyHandler
    /// pattern: the VS-coupled host delegates the decision here so it stays unit-pinned).
    /// <see cref="Resolve"/> is the WHOLE decision: the workspace buffer only when the workspace
    /// resolved AND it knows the path; every other combination falls back to the standalone
    /// document the host owns and disposes.
    /// </summary>
    internal static class PreviewBufferSource
    {
        /// <summary>
        /// (true, true) → <see cref="PreviewBufferKind.Workspace"/>, <c>OwnsDocument=false</c>
        /// (never disposed by the host). Anything else →
        /// <see cref="PreviewBufferKind.Standalone"/>, <c>OwnsDocument=true</c> (the host
        /// disposes it in CloseView).
        /// </summary>
        internal static PreviewBufferDecision Resolve(bool workspaceAvailable, bool documentFound)
            => workspaceAvailable && documentFound
                ? new PreviewBufferDecision(PreviewBufferKind.Workspace, ownsDocument: false)
                : new PreviewBufferDecision(PreviewBufferKind.Standalone, ownsDocument: true);
    }
}
