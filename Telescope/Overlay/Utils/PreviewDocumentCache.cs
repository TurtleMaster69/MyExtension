using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope.Overlay
{
    /// <summary>
    /// Mtime-keyed "content changed?" decision for the preview document rebuild (R2). The WPF
    /// <see cref="PreviewRenderer"/> rebuilds the whole FlowDocument + line pointers +
    /// ScrollToHome() on every selection change; this pure seam gates that rebuild on the file's
    /// <c>LastWriteTimeUtc</c> (mirrors <see cref="PreviewTokenCache"/> — holds NO WPF types).
    /// Injected timestamp delegate keeps the unit tests hermetic.
    /// </summary>
    internal sealed class PreviewDocumentCache
    {
        private readonly Func<string, DateTime> _timestamp;
        private readonly Dictionary<string, DateTime> _entries =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public PreviewDocumentCache(Func<string, DateTime>? timestamp = null)
        {
            _timestamp = timestamp ?? File.GetLastWriteTimeUtc;
        }

        /// <summary>
        /// Returns true when the file's content changed since the last call (mtime differs, or no
        /// cached mtime yet) and records the current mtime. The WPF document rebuild is gated on
        /// this — an unchanged mtime skips the rebuild.
        /// </summary>
        public bool ShouldRebuild(string path)
        {
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out DateTime last) && last == stamp)
            {
                return false;
            }
            _entries[path] = stamp;
            return true;
        }
    }
}
