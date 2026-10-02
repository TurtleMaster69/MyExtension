using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope.Overlay
{
    /// <summary>
    /// Mtime-keyed cache of the tokenized syntax segments for a preview file, so
    /// <see cref="PreviewRenderer"/> re-tokenizes only when the file's
    /// <c>LastWriteTimeUtc</c> changes (the FlowDocument rebuild stays the renderer's job — this
    /// cache holds NO WPF types). Injected timestamp/content-reader delegates keep the unit tests
    /// hermetic (mirrors <see cref="Telescope.Finders.FileContentCache"/>).
    /// </summary>
    internal sealed class PreviewTokenCache
    {
        private readonly Func<string, DateTime> _timestamp;
        private readonly Func<string, string> _contentReader;

        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public PreviewTokenCache(Func<string, DateTime>? timestamp = null, Func<string, string>? contentReader = null)
        {
            _timestamp = timestamp ?? File.GetLastWriteTimeUtc;
            _contentReader = contentReader ?? File.ReadAllText;
        }

        /// <summary>
        /// Returns the cached segments when the file's <c>LastWriteTimeUtc</c> is unchanged, else
        /// re-reads + re-tokenizes and caches them. The tokenizer is invoked only on a cache miss.
        /// </summary>
        public IReadOnlyList<SyntaxSegment> GetSegments(string path, Func<string, IReadOnlyList<SyntaxSegment>> tokenize)
        {
            return GetSegments(path, _contentReader(path), tokenize);
        }

        /// <summary>
        /// Returns the cached segments when the file's <c>LastWriteTimeUtc</c> is unchanged, else
        /// tokenizes the supplied <paramref name="content"/> and caches the result (N34/BP-47: the
        /// caller already read the content, so a cache miss must not read the file a second time).
        /// </summary>
        public IReadOnlyList<SyntaxSegment> GetSegments(string path, string content, Func<string, IReadOnlyList<SyntaxSegment>> tokenize)
        {
            DateTime stamp = _timestamp(path);
            if (_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp)
            {
                return entry.Segments;
            }
            var segments = tokenize(content);
            _entries[path] = new CacheEntry(stamp, segments);
            return segments;
        }

        /// <summary>
        /// Returns true when the file's content changed since the last <see cref="GetSegments"/>
        /// call (mtime differs, or no cached entry yet). The WPF FlowDocument rebuild is gated on
        /// this — an unchanged mtime skips the rebuild (N33/BP-46: served from this cache, which
        /// subsumes the deleted <c>PreviewDocumentCache</c>).
        /// </summary>
        public bool ShouldRebuild(string path)
        {
            DateTime stamp = _timestamp(path);
            return !(_entries.TryGetValue(path, out CacheEntry entry) && entry.Timestamp == stamp);
        }

        private sealed class CacheEntry
        {
            public CacheEntry(DateTime timestamp, IReadOnlyList<SyntaxSegment> segments)
            {
                Timestamp = timestamp;
                Segments = segments;
            }

            public DateTime Timestamp { get; }
            public IReadOnlyList<SyntaxSegment> Segments { get; }
        }
    }
}
