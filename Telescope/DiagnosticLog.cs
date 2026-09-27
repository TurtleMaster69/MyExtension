namespace Telescope
{
    /// <summary>
    /// Single source for the log-prefix literals emitted by the C# log sites and
    /// asserted on by the e2e harness (tools/test-e2e.ps1) and the unit test
    /// Run_LogPrefixes_Pinned. Keep in sync with the $script:Pfx* variables in
    /// tools/test-e2e.ps1 and tools/iterate-telescope.ps1.
    /// </summary>
    public static class DiagnosticLog
    {
        public const string NeoVisual = "[NeoVisual] ";
        public const string Telescope = "[Telescope] ";
        public const string Hook = "[Hook] ";
        public const string MyExtension = "[MyExtension] ";
        public const string GlobalKeyboard = "[GlobalKeyboard] ";
    }
}