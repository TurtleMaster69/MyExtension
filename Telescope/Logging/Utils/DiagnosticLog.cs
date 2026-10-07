namespace Telescope.Logging
{
    /// <summary>
    /// Single source for the log-prefix literals emitted by the C# log sites and
    /// asserted on by the e2e harness (tools/harness/test-e2e.ps1) and the unit test
    /// Run_LogPrefixes_Pinned. Keep in sync with the $script:Pfx* variables in
    /// tools/harness/test-e2e.ps1 and tools/harness/iterate-telescope.ps1.
    /// </summary>
    public static class DiagnosticLog
    {
        public const string NeoVisual = "[NeoVisual] ";
        public const string Telescope = "[Telescope] ";
        public const string Hook = "[Hook] ";
        public const string MyExtension = "[MyExtension] ";
        public const string GlobalKeyboard = "[GlobalKeyboard] ";

        /// <summary>
        /// Replaces control characters (newlines, tabs, ...) with spaces so user-controlled text
        /// interpolated into a log line can never split it (N43/BP-57, the R39 sample-sanitization
        /// pattern). Returns the original string when it is already clean.
        /// </summary>
        internal static string SanitizeText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            string value = text!;
            var chars = value.ToCharArray();
            bool dirty = false;
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                {
                    chars[i] = ' ';
                    dirty = true;
                }
            }
            return dirty ? new string(chars) : value;
        }
    }
}