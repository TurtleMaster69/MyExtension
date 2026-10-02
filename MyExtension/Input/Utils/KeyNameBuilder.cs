using System.Windows.Forms;

namespace MyExtension.Input
{
    /// <summary>
    /// Builds the canonical shortcut string (e.g. Ctrl+H, Shift+F4, Alt+X) used as the config-file
    /// key for simple modifier bindings. Single-allocation via StringBuilder.
    /// </summary>
    internal static class KeyNameBuilder
    {
        public static string Build(Keys key, bool ctrl, bool shift, bool alt)
        {
            var sb = new System.Text.StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (shift) sb.Append("Shift+");
            if (alt) sb.Append("Alt+");
            sb.Append(KeyNames.ToString(key));
            return sb.ToString();
        }
    }
}
