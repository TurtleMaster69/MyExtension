namespace Telescope.Overlay
{
    /// <summary>
    /// The preview diagnostics' message bodies — byte-stable under the preview→editor migration
    /// (Section P D-P2). TelescopeLog supplies the "[Telescope] " prefix; these helpers return the
    /// message body only, EXACTLY the formats the retired PreviewRenderer emitted
    /// (PreviewRenderer.cs:58/:60). Pure so the unit suite pins the byte-exact formats the harness
    /// asserts (Section C's Run_PreviewDiagnostics_*). The <c>preview tokens=</c> literal is
    /// deliberately NOT here — it is emitted only by the host's classifier pass (BP-P3) and no
    /// unit test pins it.
    /// </summary>
    internal static class PreviewDiagnostics
    {
        /// <summary>"preview caret={offset} line={line}" — col 0-based, line 1-based.</summary>
        internal static string Caret(int offset, int line)
            => $"preview caret={offset} line={line}";

        /// <summary>"preview file={path} chars={chars}" — the load diagnostic's exact format.</summary>
        internal static string File(string path, int chars)
            => $"preview file={path} chars={chars}";
    }
}
