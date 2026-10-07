using System.Windows.Forms;

namespace MyExtension.Input
{
    /// <summary>
    /// Shared printable-key mapping used by the leader-sequence and simple-shortcut paths. A few
    /// non-alphanumeric keys have awkward enum names (e.g. the "/" key maps to
    /// <c>Keys.OemQuestion</c>/<c>Oem2</c>, "+" to <c>Keys.Oemplus</c>), so they map to their
    /// printable character for a readable default config.
    /// </summary>
    internal static class KeyNames
    {
        public static string ToString(Keys key)
        {
            switch (key)
            {
                case Keys.OemQuestion: return "/";   // 191, same value as Keys.Oem2
                case Keys.Oemplus: return "+";       // 187
                case Keys.OemMinus: return "-";      // 189
                case Keys.OemPipe: return "|";       // 220 (0xDC); Shift+OemPipe types '|'
                case Keys.OemCloseBrackets: return "]";   // 221 (0xDD); Shift+OemCloseBrackets types '}'
                case Keys.OemOpenBrackets: return "[";    // 219 (0xDB); Shift+OemOpenBrackets types '{'
                default: return key.ToString();
            }
        }

        /// <summary>
        /// Shift-aware key name for LEADER sequences: a letter's case encodes Shift
        /// (lowercase = unshifted, uppercase = Shift+letter) so "s,g" and "s,G" are distinct
        /// bindings. Non-letter keys delegate to the printable mapping (shift-insensitive —
        /// shift is not noted in the config for non-letters). The single-arg ToString stays
        /// the SIMPLE-shortcut contract (KeyNameBuilder prepends "Ctrl+"/"Shift+"/"Alt+" itself
        /// and keeps the uppercase letter format).
        /// </summary>
        public static string ToString(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char lower = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpperInvariant(lower).ToString() : lower.ToString();
            }
            // m5 (BP-7): the shift-aware overload maps the SHIFTED printable characters for the
            // non-letter keys, so a binding like `w,}` (Shift+]) fires distinctly from `w,]`.
            if (shift)
            {
                switch (key)
                {
                    case Keys.OemCloseBrackets: return "}";   // 221 (0xDD); Shift+OemCloseBrackets types '}'
                    case Keys.OemOpenBrackets: return "{";    // 219 (0xDB); Shift+OemOpenBrackets types '{'
                    case Keys.OemQuestion: return "?";        // 191; Shift+OemQuestion types '?'
                    case Keys.OemMinus: return "_";           // 189; Shift+OemMinus types '_'
                    case Keys.Oemplus: return "+";            // 187; both
                    case Keys.OemPipe: return "|";            // 220 (0xDC); both
                }
            }
            return ToString(key);   // non-letters: printable mapping (shift-insensitive for the rest)
        }

        /// <summary>
        /// m2 (BP-4): the SINGLE source of truth for which VKs are physical modifier keys
        /// (Shift/Ctrl/Alt/Win, left and right variants — all 11). Both
        /// <see cref="LeaderSequenceMatcher"/> and <see cref="KeybindingConfig"/> delegate here so
        /// the set cannot drift (the two lists had already diverged on LWin/RWin).
        /// </summary>
        internal static bool IsPhysicalModifierKey(Keys key)
        {
            return key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
                || key == Keys.LWin || key == Keys.RWin;
        }
    }
}
