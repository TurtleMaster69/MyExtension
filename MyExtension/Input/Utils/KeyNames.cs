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
            return ToString(key);   // non-letters: printable mapping, shift-insensitive
        }
    }
}
