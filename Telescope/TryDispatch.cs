using System.Windows.Input;

namespace Telescope
{
    /// <summary>
    /// Shared WPF-<see cref="Key"/> vim-motion dispatch: the union of the prompt and preview
    /// motion surfaces (h/l/j/k/w/b/e/0/$/gg/G + the a/A/I insert placements). Pure — the caller
    /// owns the navigator and applies the resulting caret. The <c>$</c> drift fix: a bare
    /// <see cref="Key.D4"/> (no shift) is NOT a motion and must NOT LineEnd.
    /// </summary>
    internal static class TryDispatch
    {
        public static bool Handle(Key key, bool shift, TextMotionNavigator nav, out CaretPlacement? insertPlacement)
        {
            insertPlacement = null;
            switch (key)
            {
                case Key.H: nav.Left(); return true;
                case Key.L: nav.Right(); return true;
                case Key.J: nav.Down(); return true;
                case Key.K: nav.Up(); return true;
                case Key.W: nav.NextWord(); return true;
                case Key.B: nav.PrevWord(); return true;
                case Key.E: nav.EndWord(); return true;
                case Key.D0: nav.LineStart(); return true; // 0
                case Key.D4:
                    if (!shift)
                    {
                        return false; // $ drift fix: bare $ is not a motion
                    }
                    nav.LineEnd();
                    return true;
                case Key.G:
                    if (shift)
                    {
                        nav.Bottom();
                    }
                    else
                    {
                        nav.Top();
                    }
                    return true;
                case Key.A:
                    if (shift)
                    {
                        nav.InsertEnd();
                        insertPlacement = CaretPlacement.End;
                    }
                    else
                    {
                        nav.InsertAfter();
                        insertPlacement = CaretPlacement.Current;
                    }
                    return true;
                case Key.I:
                    if (!shift)
                    {
                        return false; // bare i is the generic insert
                    }
                    nav.InsertStart();
                    insertPlacement = CaretPlacement.Start;
                    return true;
                default:
                    return false;
            }
        }
    }
}
