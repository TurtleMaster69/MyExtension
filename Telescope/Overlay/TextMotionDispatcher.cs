using System.Windows.Forms;
using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// The vim text motions available across the extension's motion surfaces. Pure mapping
    /// (key + shift state -> motion) so it can be unit-tested hermetically; the motion math itself
    /// lives in the shared <see cref="TextMotionNavigator"/>.
    /// </summary>
    public enum TextMotion
    {
        Left,
        Right,
        NextWord,
        PrevWord,
        EndWord,
        InsertAfter,
        InsertEnd,
        InsertStart,
        Down,
        Up,
        LineStart,
        LineEnd,
        Top,
        Bottom,
    }

    /// <summary>
    /// The single shared key→motion dispatch table for BOTH vim-motion surfaces (M19):
    ///
    /// <list type="bullet">
    /// <item>the tool-window text surfaces (Command Window, Solution Explorer search box, ...) —
    ///   a WinForms-<see cref="Keys"/> mapping (<see cref="MapKey(Keys, bool)"/>), and</item>
    /// <item>the Telescope prompt/preview surfaces — a WPF-<see cref="Key"/> mapping
    ///   (<see cref="MapKey(Key, bool)"/>).</item>
    /// </list>
    ///
    /// <see cref="TextMotionHelper.MapMotion"/> and <see cref="TryDispatch.Handle"/> both delegate
    /// to this dispatcher (each surface keeps its own shift source + its own diagnostic log line).
    /// The motion math itself lives in <see cref="TextMotionNavigator"/>.
    /// </summary>
    internal static class TextMotionDispatcher
    {
        /// <summary>Maps a WinForms <see cref="Keys"/> value (plus shift state) to a text motion.</summary>
        public static TextMotion? MapKey(Keys key, bool shift)
        {
            switch (key)
            {
                case Keys.H: return TextMotion.Left;
                case Keys.L: return TextMotion.Right;
                case Keys.W: return TextMotion.NextWord;
                case Keys.B: return TextMotion.PrevWord;
                case Keys.E: return TextMotion.EndWord;
                case Keys.A:
                    // A (Shift+a) = insert at end of line; a = insert after the caret.
                    return shift ? TextMotion.InsertEnd : TextMotion.InsertAfter;
                case Keys.I:
                    // I (Shift+i) = insert at start of line; a bare i is the generic insert.
                    return shift ? TextMotion.InsertStart : (TextMotion?)null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Maps a WPF <see cref="Key"/> value (plus shift state) to a text motion. The keys whose
        /// motions exist in <see cref="TextMotion"/> today (h/l/j/k/w/b/e/0/$/gg/G + a/A/I) are
        /// mapped here. The <c>$</c> drift fix: a bare <see cref="Key.D4"/> (no shift) is NOT a
        /// motion and must NOT LineEnd.
        /// </summary>
        public static TextMotion? MapKey(Key key, bool shift)
        {
            switch (key)
            {
                case Key.H: return TextMotion.Left;
                case Key.L: return TextMotion.Right;
                case Key.J: return TextMotion.Down;
                case Key.K: return TextMotion.Up;
                case Key.W: return TextMotion.NextWord;
                case Key.B: return TextMotion.PrevWord;
                case Key.E: return TextMotion.EndWord;
                case Key.D0: return TextMotion.LineStart; // 0
                case Key.D4:
                    // $ drift fix: bare $ (D4 without shift) is not a motion.
                    return shift ? TextMotion.LineEnd : (TextMotion?)null;
                case Key.G:
                    return shift ? TextMotion.Bottom : TextMotion.Top;
                case Key.A:
                    return shift ? TextMotion.InsertEnd : TextMotion.InsertAfter;
                case Key.I:
                    return shift ? TextMotion.InsertStart : (TextMotion?)null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Applies a motion to the navigator; returns true when the motion is known. The insert
        /// placements (a/A/I) also report the <see cref="CaretPlacement"/> the caller should use.
        /// </summary>
        public static bool Apply(TextMotion motion, TextMotionNavigator nav, out CaretPlacement? insertPlacement)
        {
            insertPlacement = null;
            switch (motion)
            {
                case TextMotion.Left: nav.Left(); return true;
                case TextMotion.Right: nav.Right(); return true;
                case TextMotion.NextWord: nav.NextWord(); return true;
                case TextMotion.PrevWord: nav.PrevWord(); return true;
                case TextMotion.EndWord: nav.EndWord(); return true;
                case TextMotion.Down: nav.Down(); return true;
                case TextMotion.Up: nav.Up(); return true;
                case TextMotion.LineStart: nav.LineStart(); return true;
                case TextMotion.LineEnd: nav.LineEnd(); return true;
                case TextMotion.Top: nav.Top(); return true;
                case TextMotion.Bottom: nav.Bottom(); return true;
                case TextMotion.InsertAfter: nav.InsertAfter(); insertPlacement = CaretPlacement.Current; return true;
                case TextMotion.InsertEnd: nav.InsertEnd(); insertPlacement = CaretPlacement.End; return true;
                case TextMotion.InsertStart: nav.InsertStart(); insertPlacement = CaretPlacement.Start; return true;
                default: return false;
            }
        }
    }
}
