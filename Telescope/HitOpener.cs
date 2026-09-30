using System;
using System.IO;

namespace Telescope
{
    /// <summary>
    /// Shared open-at-line helper for finder hit models: guards a null/missing-file hit and
    /// delegates the actual open to the host-provided <paramref name="openAtLine"/> callback.
    /// </summary>
    internal static class HitOpener
    {
        public static void OpenAtLine(IFileLocation hit, Action<string, int> openAtLine)
        {
            if (hit == null || !File.Exists(hit.FilePath))
            {
                return;
            }
            openAtLine(hit.FilePath, hit.LineNumber);
        }
    }
}
