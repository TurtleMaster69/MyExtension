using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MyExtension.Input
{
    /// <summary>
    /// Loads keybindings from an optional external JSON file, falling back to built-in
    /// defaults shipped inside the extension.
    ///
    /// - Built-in defaults: embedded resource <c>default-keybindings.json</c> (project root).
    /// - Optional user overrides: %APPDATA%\MyExtension\keybindings.json — used only if the
    ///   file exists; it is merged over the built-in defaults. The extension never creates
    ///   this file.
    ///
    /// <para/>
    /// <b>Why an embedded resource for the defaults:</b> a VSIX is a single packaged artifact; it
    /// has no project directory to read at runtime. Marking the JSON as an
    /// <c>&lt;EmbeddedResource/&gt;</c> in the .csproj compiles it into the DLL, so it's always
    /// present and read via <see cref="Assembly.GetManifestResourceStream"/>.
    ///
    /// <para/>
    /// <b>Why JavaScriptSerializer for parsing:</b> the target is .NET Framework 4.7.2.
    /// <c>System.Web.Script.Serialization.JavaScriptSerializer</c> (in <c>System.Web.Extensions</c>)
    /// is built into the framework, so it parses JSON with zero extra NuGet dependency —
    /// avoiding a Newtonsoft.Json / System.Text.Json version clash inside VS, which already pins
    /// its own Newtonsoft version.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="Load"/> only reads files + the embedded resource (no VS
    /// service calls), so it can run on any thread; it is invoked during handler construction.
    ///
    /// Format:
    /// {
    ///   "leader": "Space",
    ///   "bindings": {
    ///     "Ctrl+H": "navigate-left",
    ///     "F,F":   "command:Edit.GoToFile",
    ///     ...
    ///   }
    /// }
    ///
    /// The "bindings" map uses the same sequence syntax as the built-in defaults:
    ///   - "Ctrl+H"            -> Ctrl modifier + H
    ///   - "F,F"               -> leader (Space) followed by F then F
    /// A value of null removes the binding so it falls through to the editor.
    ///
    /// Action names:
    ///   - navigate-left / navigate-right / navigate-up / navigate-down
    ///   - "command:&lt;VsCommandName&gt;" runs any VS command, e.g. "command:File.Close".
    /// </summary>
    internal sealed class KeybindingConfig
    {
        private const string FolderName = "MyExtension";
        private const string FileName = "keybindings.json";
        private const string DefaultResourceName = "default-keybindings.json";

        public Keys LeaderKey { get; }
        public Dictionary<string, string> Bindings { get; }

        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName, FileName);

        private KeybindingConfig(Keys leaderKey, Dictionary<string, string> bindings)
        {
            LeaderKey = leaderKey;
            Bindings = bindings;
        }

        public static KeybindingConfig Load()
        {
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var leaderKey = Keys.Space;

            // Base defaults from the embedded project-root JSON.
            try
            {
                ApplyJson(ReadEmbeddedDefault(), ref leaderKey, bindings);
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Failed to load built-in keybindings: {ex.Message}");
            }

            // Optional user overrides, used only when the file exists.
            string path = ConfigPath;
            bool userFileExists = File.Exists(path);
            try
            {
                if (userFileExists)
                {
                    ApplyJson(File.ReadAllText(path), ref leaderKey, bindings);
                }
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Failed to load keybindings from '{path}': {ex.Message}");
            }

            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Keybindings loaded: {bindings.Count} binding(s), leader = {leaderKey} (user file: {(userFileExists ? path : "none")})");

            return new KeybindingConfig(leaderKey, bindings);
        }

        /// <summary>
        /// Test-only seam: builds a config from an explicit JSON string (no embedded resource, no
        /// user file). Mirrors the merge logic of <see cref="Load"/>. Internal so the offline
        /// NeoVisual test project can exercise binding parsing hermetically.
        /// </summary>
        internal static KeybindingConfig LoadFromJson(string json)
        {
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var leaderKey = Keys.Space;
            ApplyJson(json, ref leaderKey, bindings);
            return new KeybindingConfig(leaderKey, bindings);
        }

        /// <summary>Test-only seam: builds a config from ONLY the embedded default-keybindings.json
        /// (no user %APPDATA% file). Internal so the offline NeoVisual test project can assert the
        /// shipped defaults hermetically.</summary>
        internal static KeybindingConfig LoadDefaults()
        {
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var leaderKey = Keys.Space;
            ApplyJson(ReadEmbeddedDefault(), ref leaderKey, bindings);
            return new KeybindingConfig(leaderKey, bindings);
        }

        /// <summary>
        /// Classifies a binding key as a simple modifier shortcut (matched directly) vs a leader
        /// sequence (matched only after the leader key). A simple shortcut is one with a
        /// <c>Ctrl+</c>/<c>Shift+</c>/<c>Alt+</c> modifier prefix; anything else — a bare key
        /// (<c>W</c>) or a leader sequence containing <c>+</c> (<c>F,+</c>) — is a leader binding.
        /// </summary>
        internal static bool IsSimpleShortcut(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            return key.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyJson(string json, ref Keys leaderKey, Dictionary<string, string> bindings)
        {
            // JavaScriptSerializer deserializes a JSON object into Dictionary<string, object>,
            // with nested objects as nested Dictionary<string, object> and scalars as string/bool/num.
            var serializer = new JavaScriptSerializer();
            var root = serializer.Deserialize<Dictionary<string, object>>(json);

            if (root == null)
            {
                return; // empty/invalid JSON -> keep current (default) values
            }

            // "leader" is optional; if present, remap the leader key (e.g. "Space").
            if (root.TryGetValue("leader", out object leaderValue) && leaderValue is string leaderStr)
            {
                leaderKey = ParseLeader(leaderStr);
            }

            // "bindings" entries are merged over the defaults: a value overrides/adds, and a
            // null value removes the binding so the key falls through to the editor.
            if (root.TryGetValue("bindings", out object bindingsValue) &&
                bindingsValue is Dictionary<string, object> fileBindings)
            {
                foreach (var pair in fileBindings)
                {
                    if (pair.Value == null)
                    {
                        bindings.Remove(pair.Key); // null => unbind
                    }
                    else
                    {
                        bindings[pair.Key] = Convert.ToString(pair.Value); // set/override the action name
                    }
                }
            }
        }

        private static string ReadEmbeddedDefault()
        {
            var assembly = typeof(KeybindingConfig).Assembly;

            // The embedded resource's manifest name is "<RootNamespace>.<filename>" (i.e.
            // "MyExtension.default-keybindings.json"). Match by suffix to stay robust to any
            // namespace/root changes, then read it as text.
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(DefaultResourceName, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                throw new InvalidOperationException($"Embedded resource '{DefaultResourceName}' not found.");
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded resource stream was null for '{resourceName}'.");
            }

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static Keys ParseLeader(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out Keys key))
            {
                return key;
            }
            return Keys.Space;
        }
    }
}
