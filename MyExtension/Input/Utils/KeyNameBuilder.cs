using System.Collections.Generic;
using System.Windows.Forms;

namespace MyExtension.Input
{
    /// <summary>
    /// Builds the canonical shortcut string (e.g. Ctrl+H, Shift+F4, Alt+X) used as the config-file
    /// key for simple modifier bindings. N30: the built string is cached (the hook hot path calls
    /// this per key-down) so a repeated key/modifier combination does not allocate a fresh
    /// StringBuilder + string each time. The canonical output is unchanged.
    /// </summary>
    internal static class KeyNameBuilder
    {
        // Keyed by ((int)key << 3) | ctrl/shift/alt bits. The hook runs on the UI thread only, so
        // a plain dictionary is safe (no concurrent writers).
        private static readonly Dictionary<int, string> Cache = new Dictionary<int, string>();

        public static string Build(Keys key, bool ctrl, bool shift, bool alt)
        {
            int cacheKey = ((int)key << 3) | (ctrl ? 4 : 0) | (shift ? 2 : 0) | (alt ? 1 : 0);
            if (Cache.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }

            var sb = new System.Text.StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (shift) sb.Append("Shift+");
            if (alt) sb.Append("Alt+");
            sb.Append(KeyNames.ToString(key));
            string built = sb.ToString();
            Cache[cacheKey] = built;
            return built;
        }
    }
}
