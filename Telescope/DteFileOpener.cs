using EnvDTE;

namespace Telescope
{
    /// <summary>
    /// Opens a file in the editor and jumps the caret to a 1-based line via DTE. The single
    /// open-file-at-line implementation shared by the issues/grep finders and the
    /// references/implementation host openers.
    /// </summary>
    internal static class DteFileOpener
    {
        public static void OpenAtLine(DTE dte, string path, int line)
        {
            dte.ItemOperations.OpenFile(path);
            if (dte.ActiveDocument?.Selection is TextSelection sel && line > 0)
            {
                sel.GotoLine(line, false);
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}goto line={line}");
            }
        }
    }
}
