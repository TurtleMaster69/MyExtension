namespace MyExtension.Input
{
    /// <summary>
    /// Pure focus-aware close-window decision (the OverlayKeyHandler pattern: a dependency-free
    /// static seam the VS-coupled caller delegates to, so the choice stays unit-testable). There
    /// is no single VS "close whatever is active" command: a focused tool window closes via
    /// <c>Window.CloseToolWindow</c>, anything else (editor/document) via
    /// <c>Window.CloseDocumentWindow</c>.
    /// </summary>
    internal static class CloseWindowCommand
    {
        /// <summary>Returns the native VS command name that closes the focused surface class.</summary>
        internal static string For(bool isToolWindow)
            => isToolWindow ? "Window.CloseToolWindow" : "Window.CloseDocumentWindow";
    }
}
