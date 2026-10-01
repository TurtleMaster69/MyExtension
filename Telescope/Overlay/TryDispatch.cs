using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// Shared WPF-<see cref="Key"/> vim-motion dispatch: the union of the prompt and preview
    /// motion surfaces (h/l/j/k/w/b/e/0/$/gg/G + the a/A/I insert placements). Pure — the caller
    /// owns the navigator and applies the resulting caret. The <c>$</c> drift fix: a bare
    /// <see cref="Key.D4"/> (no shift) is NOT a motion and must NOT LineEnd. Delegates to the
    /// shared <see cref="TextMotionDispatcher"/> (M19 — the single key→motion table).
    /// </summary>
    internal static class TryDispatch
    {
        public static bool Handle(Key key, bool shift, TextMotionNavigator nav, out CaretPlacement? insertPlacement)
        {
            TextMotion? motion = TextMotionDispatcher.MapKey(key, shift);
            if (motion == null)
            {
                insertPlacement = null;
                return false;
            }
            return TextMotionDispatcher.Apply(motion.Value, nav, out insertPlacement);
        }
    }
}
