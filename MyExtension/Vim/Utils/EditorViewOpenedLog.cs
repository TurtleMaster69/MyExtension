using System;

namespace MyExtension.Vim
{
    /// <summary>
    /// N24: single guarded emitter for the <c>editor-view-opened file=...</c> diagnostic. Both the
    /// <see cref="VimModeTracker.TextViewCreated"/> path and the
    /// <c>SolutionExplorerController.SelectFirstSourceFile</c> direct path route through it, so a
    /// single open cannot emit the line twice (split/peek/preview views and the
    /// SelectFirstSourceFile + TextViewCreated double-count). The diagnostic format stays
    /// byte-identical.
    /// </summary>
    internal static class EditorViewOpenedLog
    {
        // A duplicate emission for the SAME path within this window is suppressed.
        private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);
        private static readonly object Gate = new object();
        private static string? _lastPath;
        private static DateTime _lastEmitUtc;

        /// <summary>
        /// Emits <c>[NeoVisual] editor-view-opened file={path}</c> unless the SAME path was emitted
        /// within the dedupe window. Returns true when the line was emitted.
        /// </summary>
        public static bool Emit(string? path)
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                if (string.Equals(_lastPath, path, StringComparison.Ordinal) &&
                    now - _lastEmitUtc < Window)
                {
                    return false;
                }
                _lastPath = path;
                _lastEmitUtc = now;
            }

            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}editor-view-opened file={path}");
            return true;
        }
    }
}
