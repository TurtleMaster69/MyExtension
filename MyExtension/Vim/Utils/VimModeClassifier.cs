namespace MyExtension.Vim
{
    /// <summary>
    /// Pure classification of a Vim ModeKind value into the typing flag and the friendly mode
    /// name used by the <c>vim-mode=</c> diagnostic. The single owner of the classification truth
    /// table (Normal=1/Insert=2/Replace=7 + the common extra modes Command=3/Visual=4/
    /// VisualBlock=5/Select=6); <see cref="VimModeState"/> and
    /// <see cref="VimModeTracker"/> delegate to it so the table lives in exactly one place.
    /// Dependency-free so it is unit-testable.
    /// </summary>
    internal static class VimModeClassifier
    {
        // Values of the Vim.ModeKind enum (Vim.Core): Normal=1, Insert=2, Command=3, Visual=4,
        // VisualBlock=5, Select=6, Replace=7 (verified by reflecting over Vim.Core.dll). Normal is
        // named for the diagnostic log the E2E harness asserts on; typing modes are Insert/Replace.
        // C1 (M-M7): the common extra modes emit NAMED tokens (not the numeric fallback); a null
        // mode (focus loss / no buffer) emits the documented `Unknown`.
        public const int Normal = 1;
        public const int Insert = 2;
        public const int Command = 3;
        public const int Visual = 4;
        public const int VisualBlock = 5;
        public const int Select = 6;
        public const int Replace = 7;

        public static (bool IsTyping, string Name) Classify(int? mode)
        {
            bool isTyping = mode == Insert || mode == Replace;
            string name = mode switch
            {
                Normal => "Normal",
                Insert => "Insert",
                Command => "Command",
                Visual => "Visual",
                VisualBlock => "VisualBlock",
                Select => "Select",
                Replace => "Replace",
                _ => mode?.ToString() ?? "Unknown",
            };
            return (isTyping, name);
        }
    }
}
