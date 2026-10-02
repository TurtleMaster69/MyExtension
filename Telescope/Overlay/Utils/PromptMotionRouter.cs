using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// The single decision point for whether a key is a prompt/preview vim MOTION (consumed by the
    /// motion handler) or an INSERT PLACEMENT (a/A/I — falls through to the overlay state machine,
    /// or is ignored in the read-only preview). Pure — delegates to the shared
    /// <see cref="TextMotionDispatcher"/> key→motion table and mirrors its placement mapping.
    /// </summary>
    internal static class PromptMotionRouter
    {
        /// <summary>
        /// Decides whether a key is a prompt/preview vim MOTION (consumed by the motion handler) or
        /// an INSERT PLACEMENT (a/A/I — reported via <paramref name="insertPlacement"/> and routed by
        /// the caller). R1: the restriction is <b>surface-aware</b> — the PROMPT surface
        /// (<paramref name="previewSurface"/> false) restricts the motion set to h/l/w/b/e/0/$ so
        /// j/k/g/G fall through to the overlay's selection navigation; the PREVIEW surface
        /// (<paramref name="previewSurface"/> true) keeps the full set (j/k/g/G navigate the code).
        /// </summary>
        public static bool ShouldConsume(Key key, bool shift, out CaretPlacement? insertPlacement, bool previewSurface = false)
        {
            TextMotion? motion = TextMotionDispatcher.MapKey(key, shift);
            if (motion == null)
            {
                // Bare i is the generic insert at the current position (OverlayKey.I -> Current).
                insertPlacement = key == Key.I ? CaretPlacement.Current : (CaretPlacement?)null;
                return false;
            }
            switch (motion.Value)
            {
                case TextMotion.InsertAfter:
                    insertPlacement = CaretPlacement.AfterCaret;
                    return false;
                case TextMotion.InsertEnd:
                    insertPlacement = CaretPlacement.End;
                    return false;
                case TextMotion.InsertStart:
                    insertPlacement = CaretPlacement.Start;
                    return false;
                case TextMotion.Down:
                case TextMotion.Up:
                case TextMotion.Top:
                case TextMotion.Bottom:
                    // R1: j/k/g/G are selection-navigation keys in the prompt (Down/Up/Top/Bottom)
                    // and must fall through to OverlayKeyHandler; the preview still needs them.
                    insertPlacement = null;
                    return previewSurface;
                default:
                    insertPlacement = null;
                    return true;
            }
        }
    }
}
