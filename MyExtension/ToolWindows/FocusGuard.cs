namespace MyExtension
{
    /// <summary>
    /// Pure decision helper for tool-window key routing. A tool-window controller's action keys
    /// (Solution Explorer <c>o</c>/<c>r</c>/<c>m</c>/<c>a</c>, hjkl moves, the search-box
    /// <c>i</c>/text motions) may only consume keys while that tool window actually holds keyboard
    /// focus. VS's window-frame selection can lag behind real WPF focus, so the frame-derived
    /// tool-window flag is combined with the event-driven <c>IsEditorFocused</c> truth: when an
    /// editor holds focus, no tool window does, and the key must fall through to VS.
    ///
    /// <para/>
    /// Dependency-free so it is unit-testable (the <c>OverlayKeyHandler</c>/<c>HierarchyResolver</c>
    /// pattern). No VS types are touched here.
    /// </summary>
    internal static class FocusGuard
    {
        /// <summary>
        /// True when the hook pre-filter should treat a tool window's action keys as interesting.
        /// False while an editor is focused, so <c>m</c>/<c>o</c>/<c>r</c>/<c>a</c> are never
        /// inspected/consumed by the hook in that state. A text-input surface (Command Window,
        /// Find, ...) owns its keyboard even when the editor-focus flag is stale, so it is exempt
        /// from the editor veto.
        /// </summary>
        public static bool HasToolWindowActionKeys(
            bool isToolWindow, bool isInputMode, int actionKeyCount, bool editorFocused, bool isTextInputSurface)
            => isToolWindow && !isInputMode && actionKeyCount > 0 && (!editorFocused || isTextInputSurface);

        /// <summary>
        /// True when a key should be routed to the focused tool window's controller. False when an
        /// editor is focused: the tool window does not hold the keyboard, so the key falls through.
        /// A tool window in input mode, or a text-input surface, owns its keyboard even when the
        /// editor-focus flag is stale.
        /// </summary>
        public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface)
            => isToolWindow && !(editorFocused && !isInputMode && !isTextInputSurface);

        /// <summary>
        /// True when the user is typing, so the leader key must type a literal space. A tool window
        /// in input mode is always typing; otherwise an editor's typing mode (insert/replace) wins;
        /// a tool window in normal mode is not typing.
        /// </summary>
        public static bool IsTyping(
            bool isToolWindow, bool isInputMode, bool editorFocused, bool editorInTypingMode)
            => isInputMode
                ? true
                : (editorFocused ? editorInTypingMode : (isToolWindow ? isInputMode : editorInTypingMode));
    }
}
