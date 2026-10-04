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
            return new LineIndex(editorText).LineOf(Offset(editorText, navigatorCaret));
        }
    }
}
