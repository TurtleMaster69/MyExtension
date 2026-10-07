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
    /// <see cref="TextMotionHelper.MapMotion"/> and <see cref="Handle"/> both delegate to this
    /// dispatcher (each surface keeps its own shift source + its own diagnostic log line). The
    /// motion math itself lives in <see cref="TextMotionNavigator"/>.
    /// </summary>
    internal static class TextMotionDispatcher
    {
        /// <summary>
        /// Canonical key identity shared by both surfaces (N36/BP-49): each surface translates its
        /// native key type to this once, then the single <see cref="MapMotion"/> table maps it to a
        /// motion — so the WinForms and WPF mappings cannot drift.
        /// </summary>
        private enum MotionKey
        {
            H,
            L,
            W,
            B,
            E,
            A,
            I,
            J,
            K,
            D0,
            D4,
            G,
        }

        /// <summary>Maps a WinForms <see cref="Keys"/> value (plus shift state) to a text motion.</summary>
        public static TextMotion? MapKey(Keys key, bool shift)
        {
            MotionKey? canonical;
            switch (key)
            {
                case Keys.H: canonical = MotionKey.H; break;
                case Keys.L: canonical = MotionKey.L; break;
                case Keys.W: canonical = MotionKey.W; break;
                case Keys.B: canonical = MotionKey.B; break;
                case Keys.E: canonical = MotionKey.E; break;
                case Keys.A: canonical = MotionKey.A; break;
                case Keys.I: canonical = MotionKey.I; break;
                case Keys.J: canonical = MotionKey.J; break;
                case Keys.K: canonical = MotionKey.K; break;
                case Keys.D0: canonical = MotionKey.D0; break;
                case Keys.D4: canonical = MotionKey.D4; break;
                default: canonical = null; break;
            }
            return canonical == null ? null : MapMotion(canonical.Value, shift);
        }

        /// <summary>
        /// Maps a WPF <see cref="Key"/> value (plus shift state) to a text motion. The keys whose
        /// motions exist in <see cref="TextMotion"/> today (h/l/j/k/w/b/e/0/$/gg/G + a/A/I) are
        /// mapped here. The <c>$</c> drift fix: a bare <see cref="Key.D4"/> (no shift) is NOT a
        /// motion and must NOT LineEnd.
        /// </summary>
        public static TextMotion? MapKey(Key key, bool shift)
        {
            MotionKey? canonical;
            switch (key)
            {
                case Key.H: canonical = MotionKey.H; break;
                case Key.L: canonical = MotionKey.L; break;
                case Key.J: canonical = MotionKey.J; break;
                case Key.K: canonical = MotionKey.K; break;
                case Key.W: canonical = MotionKey.W; break;
                case Key.B: canonical = MotionKey.B; break;
                case Key.E: canonical = MotionKey.E; break;
                case Key.D0: canonical = MotionKey.D0; break;
                case Key.D4: canonical = MotionKey.D4; break;
                case Key.G: canonical = MotionKey.G; break;
                case Key.A: canonical = MotionKey.A; break;
                case Key.I: canonical = MotionKey.I; break;
                default: canonical = null; break;
            }
            return canonical == null ? null : MapMotion(canonical.Value, shift);
        }

        /// <summary>
        /// The single canonical key→motion table (N36/BP-49). Both surfaces translate their native
        /// key to <see cref="MotionKey"/> and delegate here, so the mappings cannot drift.
        /// </summary>
        private static TextMotion? MapMotion(MotionKey key, bool shift)
        {
            switch (key)
            {
                case MotionKey.H: return TextMotion.Left;
                case MotionKey.L: return TextMotion.Right;
                case MotionKey.J: return TextMotion.Down;
                case MotionKey.K: return TextMotion.Up;
                case MotionKey.W: return TextMotion.NextWord;
                case MotionKey.B: return TextMotion.PrevWord;
                case MotionKey.E: return TextMotion.EndWord;
                case MotionKey.D0: return TextMotion.LineStart; // 0
                case MotionKey.D4:
                    // $ drift fix: bare $ (D4 without shift) is not a motion.
                    return shift ? TextMotion.LineEnd : (TextMotion?)null;
                case MotionKey.G:
                    return shift ? TextMotion.Bottom : TextMotion.Top;
                case MotionKey.A:
                    // A (Shift+a) = insert at end of line; a = insert after the caret.
                    return shift ? TextMotion.InsertEnd : TextMotion.InsertAfter;
                case MotionKey.I:
                    // I (Shift+i) = insert at start of line; a bare i is the generic insert.
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
                case TextMotion.InsertAfter: nav.InsertAfter(); insertPlacement = CaretPlacement.AfterCaret; return true;
                case TextMotion.InsertEnd: nav.InsertEnd(); insertPlacement = CaretPlacement.End; return true;
                case TextMotion.InsertStart: nav.InsertStart(); insertPlacement = CaretPlacement.Start; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Dispatches a WPF <see cref="Key"/> (plus shift state) to the navigator: maps the key to
        /// a motion and applies it (n11 — the merged <c>TryDispatch.Handle</c> surface). Returns
        /// true when the key was a motion; the insert placements (a/A/I) report the
        /// <see cref="CaretPlacement"/> the caller should use.
        /// </summary>
        public static bool Handle(Key key, bool shift, TextMotionNavigator nav, out CaretPlacement? insertPlacement)
        {
            TextMotion? motion = MapKey(key, shift);
            if (motion == null)
            {
                insertPlacement = null;
                return false;
            }
            return Apply(motion.Value, nav, out insertPlacement);
        }
    }
}
