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
        public static bool ShouldConsume(Key key, bool shift, out CaretPlacement? insertPlacement)
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
                    insertPlacement = CaretPlacement.Current;
                    return false;
                case TextMotion.InsertEnd:
                    insertPlacement = CaretPlacement.End;
                    return false;
                case TextMotion.InsertStart:
                    insertPlacement = CaretPlacement.Start;
                    return false;
                default:
                    insertPlacement = null;
                    return true;
            }
        }
    }
}
