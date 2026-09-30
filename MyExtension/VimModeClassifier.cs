namespace MyExtension
{
    /// <summary>
    /// Pure classification of a Vim ModeKind value into the typing flag and the friendly mode
    /// name used by the <c>vim-mode=</c> diagnostic. The single owner of the classification truth
    /// table (Normal=1/Insert=2/Replace=7); <see cref="VimModeState"/> and
    /// <see cref="VimModeTracker"/> delegate to it so the table lives in exactly one place.
    /// Dependency-free so it is unit-testable.
    /// </summary>
    internal static class VimModeClassifier
    {
        // Values of the Vim.ModeKind enum (Vim.Core): Normal=1, Insert=2, Replace=7 (verified by
        // reflecting over Vim.Core.dll). Normal is named for the diagnostic log the E2E harness
        // asserts on; typing modes are Insert/Replace.
        public const int Normal = 1;
        public const int Insert = 2;
        public const int Replace = 7;

        public static (bool IsTyping, string Name) Classify(int? mode)
        {
            bool isTyping = mode == Insert || mode == Replace;
            string name = mode switch
            {
                Normal => "Normal",
                Insert => "Insert",
                Replace => "Replace",
                _ => mode?.ToString() ?? "Unknown",
            };
            return (isTyping, name);
        }
    }
}
