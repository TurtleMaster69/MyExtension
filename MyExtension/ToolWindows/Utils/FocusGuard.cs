namespace MyExtension.ToolWindows
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
        /// True when a tool window genuinely owns the keyboard despite a stale editor-focus flag: a
        /// controller in input mode, or a text-input surface (Command Window, Find, ...) that is
        /// focused. The single exemption used by all three routing formulations below.
        /// </summary>
        public static bool OwnsKeyboard(bool isInputMode, bool isTextInputSurface, bool textInputSurfaceFocused)
            => isInputMode || (isTextInputSurface && textInputSurfaceFocused);

        /// <summary>
        /// True when the hook pre-filter should treat a tool window's action keys as interesting.
        /// False while an editor is focused, so <c>m</c>/<c>o</c>/<c>r</c>/<c>a</c> are never
        /// inspected/consumed by the hook in that state. A text-input surface (Command Window,
        /// Find, ...) owns its keyboard even when the editor-focus flag is stale, so it is exempt
        /// from the editor veto.
        /// </summary>
        public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool ownsKeyboard)
            => isToolWindow && !(editorFocused && !ownsKeyboard);

        /// <summary>
        /// True when a key should be routed to the focused tool window's controller. False when an
        /// editor is focused: the tool window does not hold the keyboard, so the key falls through.
        /// A tool window that owns the keyboard (input mode, or a genuinely focused text-input
        /// surface) is exempt from the editor veto.
        /// </summary>
        public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface, bool textInputSurfaceFocused)
            => ShouldRouteToolWindowKey(isToolWindow, editorFocused, OwnsKeyboard(isInputMode, isTextInputSurface, textInputSurfaceFocused));

        /// <summary>
        /// R10: shift-aware routing — shift gates the action keys of NON-text-input controllers
        /// (<c>Shift+O/R/M/A/G</c> in Solution Explorer must not fire tree actions and swallow the
        /// key); text-input controllers still need shift to tell <c>I</c>/<c>i</c> and <c>A</c>/<c>a</c>
        /// apart, so they are exempt from the shift gate.
        /// </summary>
        public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface, bool textInputSurfaceFocused, bool shiftHeld, bool textBoxFocused = false)
            => ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface, textInputSurfaceFocused)
               && !(shiftHeld && !isTextInputSurface && !textBoxFocused);

        /// <summary>
        /// True when the user is typing, so the leader key must type a literal space. A tool window
        /// in input mode is always typing; otherwise an editor's typing mode (insert/replace) wins;
        /// a tool window in normal mode is not typing.
        /// </summary>
        public static bool IsTyping(
            bool isToolWindow, bool isInputMode, bool editorFocusedVeto, bool editorInTypingMode)
            => isInputMode
                ? true
                : (editorFocusedVeto ? editorInTypingMode : (isToolWindow ? false : editorInTypingMode));
    }
}
