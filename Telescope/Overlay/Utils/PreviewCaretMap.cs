using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// Pure navigator-offset → editor-caret mapping for the hosted read-only editor preview
    /// (Section P). The editor view's caret is set from the motion model's target; this helper
    /// clamps the target against the ACTUAL editor text (the buffer snapshot may be SHORTER than
    /// the navigator's text when the file changed on disk between SetText and the document load —
    /// an unclamped offset makes SnapshotPoint throw ArgumentOutOfRangeException) and resolves the
    /// 1-based line for scrolling + the <c>preview caret=</c> diagnostic. String-based on purpose:
    /// the unit suite must pin it hermetically (the retired CaretToPointer was WPF TextPointer —
    /// untestable).
    /// </summary>
    internal static class PreviewCaretMap
    {
        /// <summary>
        /// The navigator's target offset clamped to <paramref name="editorText"/>'s length
        /// (negative → 0; beyond the end → the length; empty text → 0).
        /// </summary>
        internal static int Offset(string editorText, int navigatorCaret)
        {
            if (string.IsNullOrEmpty(editorText))
            {
                return 0;
            }
            if (navigatorCaret < 0)
            {
                return 0;
            }
            return navigatorCaret > editorText.Length ? editorText.Length : navigatorCaret;
        }

        /// <summary>
        /// 1-based line of the CLAMPED offset (the scroll target + the diagnostic's line value).
        /// An offset AT a '\n' belongs to the line it ENDS (matches the retired CaretToPointer's
        /// "land at the end of the line" behavior, PreviewRenderer.cs:236-238, and
        /// <see cref="LineIndex.LineOf"/>'s "largest line start ≤ index" rule). Empty text → 1.
        /// </summary>
        internal static int Line(string editorText, int navigatorCaret)
        {
            if (string.IsNullOrEmpty(editorText))
            {
                return 1;
            }
            return Line(new LineIndex(editorText), navigatorCaret);
        }

        /// <summary>
        /// m23 (BP-22): the <see cref="LineIndex"/>-based overload — the overlay passes the
        /// navigator's CACHED LineIndex (built by <see cref="TextMotionNavigator.SetText"/>) so no
        /// full LineIndex is rebuilt per preview load. <see cref="LineIndex.LineOf"/> clamps the
        /// offset internally (negative → 0, beyond the end → the length), matching the string-based
        /// <see cref="Line(string, int)"/>'s clamped-offset semantics.
        /// </summary>
        internal static int Line(LineIndex index, int navigatorCaret)
        {
            return index.LineOf(navigatorCaret);
        }
    }
}
