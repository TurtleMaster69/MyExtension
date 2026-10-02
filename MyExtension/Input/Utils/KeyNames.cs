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
                default: return key.ToString();
            }
        }
    }
}
